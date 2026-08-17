using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoCorrect.Core.Caching;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Localization;
using AutoCorrect.Core.Privacy;

namespace AutoCorrect.Core.Engines.Llm;

/// <summary>
/// Talks to an OpenAI compatible endpoint (/v1/chat/completions), so llama.cpp, Ollama or a
/// server of your own can be swapped without touching anything else.
///
/// Handles rephrasing, formal wording and shortening; spelling stays with the other engines.
/// </summary>
public sealed class LlmEngine : ITextEngine
{
    public const string DefaultEndpoint = "http://localhost:11434/v1";

    /// <summary>
    /// Short tag on purpose. "qwen2.5:3b-instruct-q4_K_M" names the same weights but has to be
    /// typed exactly, and getting it slightly wrong produced a "model not loaded" error next to
    /// a perfectly good installation.
    /// </summary>
    public const string DefaultModel = "qwen2.5:3b";

    /// <summary>A model that is not loaded yet needs a while for the first token.</summary>
    private const int FirstTokenTimeoutSeconds = 120;

    /// <summary>Timeout for the two cheap calls: the availability probe and the model list.</summary>
    private const int ProbeTimeoutSeconds = 5;

    private static readonly TimeSpan UnavailableFor = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly ResultCache? _cache;

    private DateTimeOffset _unavailableUntil = DateTimeOffset.MinValue;

    /// <summary>
    /// What the configured name resolved to on this server, so the model list is fetched once
    /// rather than before every request.
    /// </summary>
    private string? _resolvedFor;
    private string? _resolvedModel;

    /// <summary>
    /// How many details the last request replaced before sending. Read by the user interface so
    /// it can say that the text was masked - a promise the user cannot check is worth little.
    /// Written from the request thread, read from the UI thread; an int assignment is atomic.
    /// </summary>
    private int _lastMaskedCount;

    /// <param name="cache">Optional. Null disables caching entirely.</param>
    public LlmEngine(HttpClient http, Func<AppSettings> settingsProvider, ResultCache? cache = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _cache = cache;
    }

    public string Name => UiText.EngineLlmName;

    /// <summary>Details replaced in the most recent request. Zero means nothing was masked.</summary>
    public int LastMaskedCount => _lastMaskedCount;

    /// <summary>
    /// Every mode, correcting included. A spell checker only knows whether a word exists, so
    /// "Halo dass ist ein tEst." passes it untouched - every word in it is real. Catching that
    /// needs grammar in context, which is what the model is for. LanguageTool is still tried
    /// first and is both faster and more predictable when it runs.
    /// </summary>
    public bool SupportsMode(ProcessingMode mode) => true;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

            var settings = _settingsProvider();
            using var probe = new HttpRequestMessage(HttpMethod.Get, ModelsUrl(settings));
            Authorize(probe, settings);

            using var response = await _http.SendAsync(probe, timeout.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _unavailableUntil = DateTimeOffset.MinValue;
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or UriFormatException)
        {
            return false;
        }
    }

    public async IAsyncEnumerable<string> ProcessAsync(
        string input,
        ProcessingMode mode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!SupportsMode(mode))
        {
            throw new NotSupportedException($"{Name} does not support mode {mode}.");
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            yield return input;
            yield break;
        }

        var model = await ResolveModelAsync(ct).ConfigureAwait(false);

        // A repeated request is answered from the database instead of costing another
        // generation. The whole answer arrives in one piece, which is exactly what the user
        // wants when they already waited for it once.
        var cached = _cache?.TryGet(input, mode, model);
        if (!string.IsNullOrEmpty(cached))
        {
            Log.Debug($"Cache hit for mode {mode}.");

            // Nothing was sent, so nothing was masked. Saying otherwise would be a false claim.
            _lastMaskedCount = 0;
            yield return cached;
            yield break;
        }

        // Masked before anything is sent, and only the masked text ever reaches the endpoint.
        var settings = _settingsProvider();
        var mask = MasksNames(settings) ? PrivacyMask.Create(input, settings.LlmProtectedTerms) : null;
        var sent = mask?.Masked ?? input;

        _lastMaskedCount = mask?.ReplacementCount ?? 0;

        if (_lastMaskedCount > 0)
        {
            Log.Debug($"Masked {_lastMaskedCount} detail(s) before sending.");
        }

        var session = await OpenAsync(sent, mode, model, ct).ConfigureAwait(false);
        var filter = new ResponseFilter(sent);
        var restorer = mask is null ? null : new MaskRestorer(mask);
        var complete = new StringBuilder();
        var finished = false;

        await using (session.ConfigureAwait(false))
        {
            while (!finished)
            {
                string? line;

                try
                {
                    line = await session.Reader.ReadLineAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException)
                {
                    Log.Warn("The model connection broke while streaming.", ex);
                    throw new EngineUnavailableException(UiText.LlmUnavailable, ex);
                }

                if (line is null)
                {
                    // The server closed the stream without a [DONE] marker; still a complete answer.
                    finished = true;
                    break;
                }

                if (line.Length == 0 || !line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = line["data: ".Length..];
                if (payload == "[DONE]")
                {
                    finished = true;
                    break;
                }

                var token = ReadToken(payload);
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                // The filter holds tokens back until it can tell text from wrapping, then the
                // restorer puts the real names back before anything reaches the screen.
                var visible = filter.Push(token);
                if (restorer is not null)
                {
                    visible = restorer.Push(visible);
                }

                if (visible.Length > 0)
                {
                    complete.Append(visible);
                    yield return visible;
                }
            }
        }

        if (!finished)
        {
            yield break;
        }

        var rest = filter.Finish();
        if (restorer is not null)
        {
            rest = restorer.Push(rest) + restorer.Finish();
        }

        if (rest.Length > 0)
        {
            complete.Append(rest);
            yield return rest;
        }

        // Only a full answer is worth storing. A cancelled or aborted run would cache a fragment
        // and hand it back as if it were the finished text.
        if (!ct.IsCancellationRequested && complete.Length > 0)
        {
            _cache?.Set(input, mode, model, complete.ToString());
        }
    }

    /// <summary>Sends the request and returns the open stream, or throws when nothing answers.</summary>
    private async Task<StreamSession> OpenAsync(
        string input,
        ProcessingMode mode,
        string model,
        CancellationToken ct)
    {
        var settings = _settingsProvider();

        if (DateTimeOffset.UtcNow < _unavailableUntil)
        {
            throw new EngineUnavailableException(UiText.LlmUnavailable);
        }

        var request = new ChatRequest
        {
            Model = model,
            Stream = true,
            MaxTokens = 512,
            Temperature = 0.3,
            Stop = Prompts.Stop,
            Messages =
            [
                new ChatMessage { Role = "system", Content = Prompts.System },
                new ChatMessage { Role = "user", Content = Prompts.User(mode, input) },
            ],
        };

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(FirstTokenTimeoutSeconds));

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, ChatUrl(settings))
            {
                // Source generated overload; the reflection based one is neither trim nor AOT safe.
                Content = JsonContent.Create(request, ChatJsonContext.Default.ChatRequest),
            };

            Authorize(message, settings);

            // Without ResponseHeadersRead the client buffers the whole answer and nothing is
            // visible until generation has finished. This is the entire point of streaming.
            var response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                response.Dispose();

                Log.Warn($"The model endpoint answered with status {status}.");

                // A missing model is the common mistake and deserves its own message. By this
                // point the name has already been checked against what is installed, so the
                // message can say whether anything usable is there at all.
                if (status is 401 or 403)
                {
                    throw new EngineUnavailableException(UiText.LlmNotAuthorised);
                }

                if (status == 404 || body.Contains("model", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EngineUnavailableException(UiText.LlmModelMissing(request.Model));
                }

                throw new EngineUnavailableException(UiText.LlmUnavailable);
            }

            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);

            // The timeout covers the wait for the first token only. Leaving the timer armed
            // would cancel the request that this stream belongs to in the middle of a long
            // answer; from here on the caller's own token is what stops generation.
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);

            return new StreamSession(response, stream, timeout);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            timeout.Dispose();
            throw;
        }
        catch (OperationCanceledException ex)
        {
            timeout.Dispose();
            Log.Warn("The model did not answer in time.", ex);
            throw new EngineUnavailableException(UiText.LlmTimeout, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or InvalidOperationException)
        {
            timeout.Dispose();
            _unavailableUntil = DateTimeOffset.UtcNow.Add(UnavailableFor);
            Log.Warn("The model endpoint is not reachable.", ex);
            throw new EngineUnavailableException(UiText.LlmUnavailable, ex);
        }
    }

    private static string? ReadToken(string payload)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize(payload, ChatJsonContext.Default.ChatChunk);
            return chunk?.Choices is { Count: > 0 } choices ? choices[0].Delta?.Content : null;
        }
        catch (JsonException)
        {
            // A malformed chunk is not worth aborting a running answer for.
            return null;
        }
    }

    /// <summary>
    /// Decides which model this request actually goes to. The configured name is used as it is
    /// whenever the server has it; otherwise whatever usable model is installed is taken
    /// instead, because failing next to a working installation helps nobody.
    /// </summary>
    private async Task<string> ResolveModelAsync(CancellationToken ct)
    {
        // Checked here and not only in OpenAsync: without it a server that is known to be down
        // would still be asked for its model list before every single correction, and that
        // wasted attempt is exactly what makes the fallback feel slow.
        if (DateTimeOffset.UtcNow < _unavailableUntil)
        {
            throw new EngineUnavailableException(UiText.LlmUnavailable);
        }

        var configured = ModelOf(_settingsProvider());

        if (string.Equals(_resolvedFor, configured, StringComparison.Ordinal) && _resolvedModel is not null)
        {
            return _resolvedModel;
        }

        var installed = await ListModelsAsync(ct).ConfigureAwait(false);

        // The list could not be read at all - the server is probably not running. Send the
        // configured name and let the request produce the real error, which also arms the
        // negative cache above.
        if (installed is null)
        {
            return configured;
        }

        var chosen = ModelCatalogue.Choose(configured, installed);
        if (chosen is null)
        {
            // Nothing usable is installed. Naming the configured model in the error is right:
            // that is the one the pull command should fetch.
            throw new EngineUnavailableException(UiText.LlmModelMissing(configured));
        }

        if (!string.Equals(chosen, configured, StringComparison.OrdinalIgnoreCase))
        {
            Log.Info($"Model '{configured}' is not installed; using '{chosen}' instead.");
        }

        _resolvedFor = configured;
        _resolvedModel = chosen;
        return chosen;
    }

    /// <summary>Names of the installed models, or null when the list could not be read.</summary>
    private async Task<IReadOnlyList<string>?> ListModelsAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

            var settings = _settingsProvider();
            using var request = new HttpRequestMessage(HttpMethod.Get, ModelsUrl(settings));
            Authorize(request, settings);

            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize(body, ChatJsonContext.Default.ModelList);

            return list?.Data?.Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList()
                   ?? (IReadOnlyList<string>)[];
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or JsonException)
        {
            Log.Warn("The list of installed models could not be read.", ex);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warn("The list of installed models timed out.");
            return null;
        }
    }

    /// <summary>Forgets a resolved model, so a changed setting takes effect at once.</summary>
    internal void ResetResolvedModel()
    {
        _resolvedFor = null;
        _resolvedModel = null;
    }

    /// <summary>
    /// Adds the bearer token when one is configured. Set per request rather than on the shared
    /// client, so a changed key takes effect without a restart.
    ///
    /// The key is never logged, and it is never put into a message shown to the user.
    /// </summary>
    private static void Authorize(HttpRequestMessage message, AppSettings settings)
    {
        var key = settings.LlmApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
    }

    /// <summary>
    /// Whether the details are replaced before sending. "auto" means: only when the endpoint is
    /// not on this machine or the local network, because a local model sees the text regardless
    /// and masking costs a little accuracy.
    /// </summary>
    internal static bool MasksNames(AppSettings settings) => settings.LlmMaskNames switch
    {
        AppSettings.MaskNamesAlways => true,
        AppSettings.MaskNamesNever => false,
        _ => IsExternal(settings.LlmEndpoint),
    };

    /// <summary>True when the address is neither this machine nor the local network.</summary>
    internal static bool IsExternal(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            // An address that cannot be read is not demonstrably local, so treat it as external.
            return !string.IsNullOrWhiteSpace(endpoint);
        }

        var host = uri.Host;

        if (uri.IsLoopback ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !System.Net.IPAddress.TryParse(host, out var address) || !IsPrivate(address);
    }

    /// <summary>The ranges that RFC 1918 and RFC 4193 reserve for a local network.</summary>
    private static bool IsPrivate(System.Net.IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || IsUniqueLocal(address);
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            127 => true,
            169 when octets[1] == 254 => true,
            172 when octets[1] is >= 16 and <= 31 => true,
            192 when octets[1] == 168 => true,
            _ => false,
        };

        static bool IsUniqueLocal(System.Net.IPAddress value) => (value.GetAddressBytes()[0] & 0xfe) == 0xfc;
    }

    internal static string ModelOf(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.LlmModel) ? DefaultModel : settings.LlmModel;

    internal static string ChatUrl(AppSettings settings) =>
        Combine(settings.LlmEndpoint, "chat/completions");

    internal static string ModelsUrl(AppSettings settings) =>
        Combine(settings.LlmEndpoint, "models");

    private static string Combine(string? baseAddress, string relative)
    {
        var trimmed = (string.IsNullOrWhiteSpace(baseAddress) ? DefaultEndpoint : baseAddress).TrimEnd('/');
        return $"{trimmed}/{relative}";
    }

    /// <summary>Keeps response, stream and reader alive for as long as the iterator runs.</summary>
    private sealed class StreamSession : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _stream;
        private readonly CancellationTokenSource _timeout;

        public StreamSession(HttpResponseMessage response, Stream stream, CancellationTokenSource timeout)
        {
            _response = response;
            _stream = stream;
            _timeout = timeout;
            Reader = new StreamReader(stream);
        }

        public StreamReader Reader { get; }

        public async ValueTask DisposeAsync()
        {
            Reader.Dispose();
            await _stream.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            _timeout.Dispose();
        }
    }
}

// ---------------------------------------------------------------- wire format

internal sealed class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("stop")]
    public string[]? Stop { get; set; }
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

internal sealed class ChatChunk
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }
}

internal sealed class ChatChoice
{
    [JsonPropertyName("delta")]
    public ChatDelta? Delta { get; set; }
}

internal sealed class ChatDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>Answer of GET /v1/models.</summary>
internal sealed class ModelList
{
    [JsonPropertyName("data")]
    public List<ModelEntry>? Data { get; set; }
}

internal sealed class ModelEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatChunk))]
[JsonSerializable(typeof(ModelList))]
internal sealed partial class ChatJsonContext : JsonSerializerContext
{
}
