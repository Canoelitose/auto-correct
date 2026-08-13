using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoCorrect.Core.Caching;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Localization;

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
    public const string DefaultModel = "qwen2.5:3b-instruct-q4_K_M";

    /// <summary>A model that is not loaded yet needs a while for the first token.</summary>
    private const int FirstTokenTimeoutSeconds = 120;

    private static readonly TimeSpan UnavailableFor = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Func<AppSettings> _settingsProvider;
    private readonly ResultCache? _cache;

    private DateTimeOffset _unavailableUntil = DateTimeOffset.MinValue;

    /// <param name="cache">Optional. Null disables caching entirely.</param>
    public LlmEngine(HttpClient http, Func<AppSettings> settingsProvider, ResultCache? cache = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _cache = cache;
    }

    public string Name => UiText.EngineLlmName;

    public bool SupportsMode(ProcessingMode mode) =>
        mode is ProcessingMode.Rephrase or ProcessingMode.Formal or ProcessingMode.Shorten;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using var response = await _http.GetAsync(ModelsUrl(_settingsProvider()), timeout.Token)
                .ConfigureAwait(false);

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

        var model = ModelOf(_settingsProvider());

        // A repeated request is answered from the database instead of costing another
        // generation. The whole answer arrives in one piece, which is exactly what the user
        // wants when they already waited for it once.
        var cached = _cache?.TryGet(input, mode, model);
        if (!string.IsNullOrEmpty(cached))
        {
            Log.Debug($"Cache hit for mode {mode}.");
            yield return cached;
            yield break;
        }

        var session = await OpenAsync(input, mode, ct).ConfigureAwait(false);
        var filter = new ResponseFilter(input);
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

                // The filter holds tokens back until it can tell text from wrapping.
                var visible = filter.Push(token);
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
    private async Task<StreamSession> OpenAsync(string input, ProcessingMode mode, CancellationToken ct)
    {
        var settings = _settingsProvider();

        if (DateTimeOffset.UtcNow < _unavailableUntil)
        {
            throw new EngineUnavailableException(UiText.LlmUnavailable);
        }

        var request = new ChatRequest
        {
            Model = ModelOf(settings),
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

                // A missing model is the common mistake and deserves its own message.
                throw new EngineUnavailableException(
                    status == 404 || body.Contains("model", StringComparison.OrdinalIgnoreCase)
                        ? UiText.LlmModelMissing(request.Model)
                        : UiText.LlmUnavailable);
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

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatChunk))]
internal sealed partial class ChatJsonContext : JsonSerializerContext
{
}
