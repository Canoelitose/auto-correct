using System.Net.Http;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Engines.LanguageTool;

namespace AutoCorrect.App.Engines;

/// <summary>
/// Composition root for the engines. This is the only place in the application that knows the
/// concrete implementations; everything else works against <see cref="ITextEngine"/>.
///
/// Phase 2 adds the LLM engine here, phase 3 the server engine in front of it.
/// </summary>
internal static class EngineFactory
{
    public static EngineRouter CreateRouter(HttpClient http, Func<AppSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(settings);

        // Order matters: LanguageTool also finds grammar problems and wins when it is running.
        // The Windows spell checker needs no installation at all and catches the common case of
        // a fresh download without any server.
        return new EngineRouter(
            new LanguageToolEngine(http, settings),
            new WindowsSpellCheckEngine(settings));
    }

    /// <summary>
    /// Availability probe for a settings candidate that has not been saved yet, used by the
    /// "Verbindung testen" button.
    /// </summary>
    public static async Task<bool> ProbeAsync(AppSettings candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        using var http = new HttpClient();
        var engine = CreateRouter(http, () => candidate);
        return await engine.IsAvailableAsync(ct).ConfigureAwait(false);
    }
}
