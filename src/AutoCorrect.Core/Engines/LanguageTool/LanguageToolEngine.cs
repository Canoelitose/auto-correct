using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.Core.Engines.LanguageTool;

/// <summary>
/// Talks to a local languagetool-standalone HTTP server. Handles <see cref="ProcessingMode.Correct"/>
/// only; everything else is left to the LLM engine added in phase 2.
/// </summary>
public sealed class LanguageToolEngine : ITextEngine
{
    public const string DefaultEndpoint = "http://localhost:8081/v2/check";
    public const string DefaultLanguage = "de-CH";

    /// <summary>
    /// Timeout of the availability probe. A freshly started LanguageTool loads its language
    /// models on the first request and needs clearly more than a second to answer, so a short
    /// timeout would report a healthy server as offline.
    /// </summary>
    public const int ProbeTimeoutSeconds = 8;

    /// <summary>
    /// How long a failed connection is remembered. Without this every single correction pays
    /// another doomed connection attempt while no server is running, which the user feels as
    /// a delay before the built-in spell checker takes over.
    /// </summary>
    private static readonly TimeSpan UnavailableFor = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Func<AppSettings> _settingsProvider;

    private DateTimeOffset _unavailableUntil = DateTimeOffset.MinValue;
    private string _unavailableEndpoint = string.Empty;

    /// <param name="http">Shared singleton. Creating one client per request exhausts sockets.</param>
    /// <param name="settingsProvider">Read late so endpoint changes take effect without a restart.</param>
    public LanguageToolEngine(HttpClient http, Func<AppSettings> settingsProvider)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    public string Name => "LanguageTool";

    public bool SupportsMode(ProcessingMode mode) => mode == ProcessingMode.Correct;

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

            using var response = await _http
                .GetAsync(BuildLanguagesUrl(_settingsProvider().LanguageToolEndpoint), timeout.Token)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                ResetAvailability();
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

        // The network call sits in its own method: a try/catch around a yield return is
        // not allowed inside an iterator.
        var corrected = await CheckAsync(input, ct).ConfigureAwait(false);
        yield return corrected;
    }

    private async Task<string> CheckAsync(string input, CancellationToken ct)
    {
        var settings = _settingsProvider();

        // A server that just refused the connection is not going to answer a moment later.
        // Changing the address in the settings clears the memory immediately.
        if (DateTimeOffset.UtcNow < _unavailableUntil &&
            string.Equals(_unavailableEndpoint, settings.LanguageToolEndpoint, StringComparison.Ordinal))
        {
            throw new EngineUnavailableException(UiText.LanguageToolUnavailable);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        string body;
        try
        {
            var fields = new Dictionary<string, string>
            {
                ["text"] = input,
                ["language"] = LanguageOptions.IsAutomatic(settings.Language)
                    ? LanguageOptions.AutomaticDetection
                    : settings.Language,
                ["enabledOnly"] = "false",
            };

            if (LanguageOptions.IsAutomatic(settings.Language))
            {
                // Detection alone would pick de-DE for Swiss text and start proposing "ß";
                // the preferred variants pin it to de-CH and en-US.
                fields["preferredVariants"] = string.IsNullOrWhiteSpace(settings.PreferredVariants)
                    ? LanguageOptions.DefaultPreferredVariants
                    : settings.PreferredVariants;
            }

            using var content = new FormUrlEncodedContent(fields);

            using var response = await _http
                .PostAsync(settings.LanguageToolEndpoint, content, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"LanguageTool responded with status {(int)response.StatusCode}.");
                throw new EngineUnavailableException(UiText.LanguageToolUnavailable);
            }

            body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Our own timeout fired, not the caller's cancellation.
            Log.Warn("LanguageTool request timed out.", ex);
            throw new EngineUnavailableException(UiText.LanguageToolUnavailable, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or InvalidOperationException)
        {
            Log.Warn("LanguageTool is not reachable.", ex);
            RememberUnavailable(settings.LanguageToolEndpoint);
            throw new EngineUnavailableException(UiText.LanguageToolUnavailable, ex);
        }

        LtResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(body, LanguageToolJsonContext.Default.LtResponse);
        }
        catch (JsonException ex)
        {
            Log.Warn("LanguageTool returned a response that could not be parsed.", ex);
            throw new EngineUnavailableException(UiText.LanguageToolUnavailable, ex);
        }

        var corrections = ToCorrections(parsed);
        Log.Debug($"LanguageTool returned {corrections.Count} usable matches for {Log.Describe(input)}.");

        return CorrectionApplier.Apply(input, corrections);
    }

    private void RememberUnavailable(string endpoint)
    {
        _unavailableEndpoint = endpoint;
        _unavailableUntil = DateTimeOffset.UtcNow.Add(UnavailableFor);
    }

    /// <summary>Forgets a remembered failure, for example after a successful probe.</summary>
    public void ResetAvailability()
    {
        _unavailableUntil = DateTimeOffset.MinValue;
        _unavailableEndpoint = string.Empty;
    }

    /// <summary>Takes the first suggestion of every match that has one.</summary>
    private static List<TextCorrection> ToCorrections(LtResponse? response)
    {
        var result = new List<TextCorrection>();
        if (response?.Matches is not { Count: > 0 } matches)
        {
            return result;
        }

        foreach (var match in matches)
        {
            var replacement = match.Replacements?.FirstOrDefault()?.Value;
            if (replacement is null)
            {
                // Matches without a suggestion are pure hints, there is nothing to apply.
                continue;
            }

            result.Add(new TextCorrection(match.Offset, match.Length, replacement));
        }

        return result;
    }

    /// <summary>Derives the /v2/languages probe URL from the configured /v2/check URL.</summary>
    internal static string BuildLanguagesUrl(string checkEndpoint)
    {
        if (string.IsNullOrWhiteSpace(checkEndpoint))
        {
            checkEndpoint = DefaultEndpoint;
        }

        var trimmed = checkEndpoint.TrimEnd('/');
        return trimmed.EndsWith("/check", StringComparison.OrdinalIgnoreCase)
            ? string.Concat(trimmed.AsSpan(0, trimmed.Length - "/check".Length), "/languages")
            : trimmed + "/languages";
    }
}
