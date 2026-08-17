using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Engines.LanguageTool;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App.Engines;

/// <summary>
/// Spell checking through the API that ships with Windows itself (the one Edge and Word use).
///
/// This is what makes the downloaded executable useful straight away: no Java, no server, no
/// installation. It only corrects spelling, not grammar, so LanguageTool stays the better
/// engine and is tried first.
/// </summary>
internal sealed class WindowsSpellCheckEngine : ITextEngine
{
    /// <summary>Checked in this order; the first one Windows supports is used.</summary>
    private static readonly string[] GermanTags = ["de-CH", "de-DE", "de-AT", "de"];
    private static readonly string[] EnglishTags = ["en-US", "en-GB", "en"];

    /// <summary>
    /// COM objects are expensive to create. The factory and one checker per language are built
    /// once and reused; creating them per call made every correction noticeably slow.
    /// </summary>
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, NativeMethods.ISpellChecker?> Checkers =
        new(StringComparer.OrdinalIgnoreCase);

    private static NativeMethods.ISpellCheckerFactory? _factory;
    private static bool _factoryFailed;

    private readonly Func<AppSettings> _settings;

    public WindowsSpellCheckEngine(Func<AppSettings> settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    private static NativeMethods.ISpellCheckerFactory? Factory()
    {
        lock (CacheLock)
        {
            if (_factory is not null || _factoryFailed)
            {
                return _factory;
            }

            try
            {
                _factory = (NativeMethods.ISpellCheckerFactory)Activator.CreateInstance(
                    Type.GetTypeFromCLSID(NativeMethods.SpellCheckerFactoryClsid)!)!;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException
                                           or NullReferenceException or NotSupportedException)
            {
                Log.Warn("The Windows spell checking factory is not available.", ex);
                _factoryFailed = true;
            }

            return _factory;
        }
    }

    /// <summary>The cached checker for a language, so other users of the dictionaries share it.</summary>
    internal static NativeMethods.ISpellChecker? CheckerFor(string languageTag) => Checker(languageTag);

    /// <summary>The languages this machine can check, best first. Empty when there are none.</summary>
    internal static IReadOnlyList<string> AvailableLanguageTags(Core.Configuration.AppSettings settings) =>
        new WindowsSpellCheckEngine(() => settings).ResolveTags();

    /// <summary>Returns null when Windows has no dictionary for the language.</summary>
    private static NativeMethods.ISpellChecker? Checker(string languageTag)
    {
        lock (CacheLock)
        {
            if (Checkers.TryGetValue(languageTag, out var cached))
            {
                return cached;
            }

            NativeMethods.ISpellChecker? checker = null;

            try
            {
                var factory = Factory();
                if (factory is not null)
                {
                    factory.IsSupported(languageTag, out var supported);
                    if (supported)
                    {
                        factory.CreateSpellChecker(languageTag, out checker);
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException
                                           or NullReferenceException or NotSupportedException)
            {
                Log.Warn($"Windows spell check is not usable for {languageTag}.", ex);
                checker = null;
            }

            Checkers[languageTag] = checker;
            return checker;
        }
    }

    public string Name => "Windows";

    public bool SupportsMode(ProcessingMode mode) => mode == ProcessingMode.Correct;

    public Task<bool> IsAvailableAsync(CancellationToken ct) =>
        Task.FromResult(ResolveTags().Count > 0);

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

        // The COM calls are synchronous and fast, but they should not run on the UI thread.
        var corrected = await Task.Run(() => Correct(input), ct).ConfigureAwait(false);
        yield return corrected;
    }

    private string Correct(string input)
    {
        var tags = ResolveTags();
        if (tags.Count == 0)
        {
            throw new EngineUnavailableException(UiText.NoSpellCheckLanguage);
        }

        // With more than one candidate language the one with fewer complaints wins. For the
        // short pieces of text this tool works on that is a reliable enough guess.
        List<TextCorrection>? best = null;

        foreach (var tag in tags)
        {
            var corrections = CollectCorrections(tag, input);
            if (corrections is null)
            {
                continue;
            }

            if (best is null || corrections.Count < best.Count)
            {
                best = corrections;
            }

            if (best.Count == 0)
            {
                break;
            }
        }

        if (best is null)
        {
            throw new EngineUnavailableException(UiText.NoSpellCheckLanguage);
        }

        Log.Debug($"Windows spell check produced {best.Count} corrections for {Log.Describe(input)}.");
        return CorrectionApplier.Apply(input, best);
    }

    /// <summary>Returns null when the language is not usable at all.</summary>
    private static List<TextCorrection>? CollectCorrections(string languageTag, string input)
    {
        var checker = Checker(languageTag);
        if (checker is null)
        {
            return null;
        }

        try
        {
            return ReadErrors(checker, input);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException
                                       or ArgumentException or NullReferenceException)
        {
            Log.Warn($"Windows spell check failed for {languageTag}.", ex);
            return null;
        }
    }

    private static List<TextCorrection> ReadErrors(NativeMethods.ISpellChecker checker, string input)
    {
        var corrections = new List<TextCorrection>();

        checker.Check(input, out var errors);
        try
        {
            while (errors.Next(out var error) == 0 && error is not null)
            {
                try
                {
                    error.get_StartIndex(out var start);
                    error.get_Length(out var length);
                    error.get_CorrectiveAction(out var action);

                    switch (action)
                    {
                        case NativeMethods.CORRECTIVE_ACTION_REPLACE:
                            error.get_Replacement(out var replacement);
                            if (!string.IsNullOrEmpty(replacement))
                            {
                                corrections.Add(new TextCorrection((int)start, (int)length, replacement));
                            }

                            break;

                        case NativeMethods.CORRECTIVE_ACTION_GET_SUGGESTIONS:
                            var word = input.Substring((int)start, (int)length);
                            var suggestion = FirstSuggestion(checker, word);
                            if (suggestion is not null)
                            {
                                corrections.Add(new TextCorrection((int)start, (int)length, suggestion));
                            }

                            break;

                        // DELETE and NONE are left alone: silently removing words is worse than
                        // leaving a wrong one in place.
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(error);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(errors);
        }

        return corrections;
    }

    private static string? FirstSuggestion(NativeMethods.ISpellChecker checker, string word)
    {
        checker.Suggest(word, out var suggestions);

        try
        {
            var buffer = new string[1];
            var fetched = IntPtr.Zero;

            if (suggestions.Next(1, buffer, fetched) == 0 && !string.IsNullOrEmpty(buffer[0]))
            {
                return buffer[0];
            }
        }
        catch (COMException ex)
        {
            Log.Debug("No suggestion available: " + ex.Message);
        }
        finally
        {
            Marshal.ReleaseComObject(suggestions);
        }

        return null;
    }

    /// <summary>
    /// Language tags to try. Automatic detection uses at most two: the best available German and
    /// English variant. Checking every variant of both meant up to seven full passes over the
    /// text for a single correction.
    /// </summary>
    private List<string> ResolveTags()
    {
        var configured = _settings().Language;

        if (!LanguageOptions.IsAutomatic(configured))
        {
            var preferred = configured.StartsWith("de", StringComparison.OrdinalIgnoreCase)
                ? GermanTags
                : EnglishTags;

            var explicitTag = FirstSupported([configured, .. preferred]);
            return explicitTag is null ? [] : [explicitTag];
        }

        var tags = new List<string>(2);

        if (FirstSupported(GermanTags) is { } german)
        {
            tags.Add(german);
        }

        if (FirstSupported(EnglishTags) is { } english)
        {
            tags.Add(english);
        }

        return tags;
    }

    private static string? FirstSupported(IEnumerable<string> candidates) =>
        candidates.FirstOrDefault(tag => Checker(tag) is not null);
}
