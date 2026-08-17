using System.Runtime.InteropServices;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Privacy;

namespace AutoCorrect.App.Engines;

/// <summary>
/// Answers "is this an ordinary word" out of the dictionaries that ship with Windows.
///
/// It is the same spell checking API the correction uses, asked a different question. A
/// capitalised word that neither the German nor the English dictionary knows, and that the
/// dictionary cannot suggest a close correction for, is a name - and that is what catches the
/// surnames no list could ever contain.
///
/// The distinction against a mere typo matters more than it looks. Without it a misspelled
/// noun would be taken for a name, replaced before sending, and come back from the model
/// exactly as misspelled as it went in - the correction would silently skip that word.
/// </summary>
internal sealed class WindowsWordKnowledge : IWordKnowledge
{
    /// <summary>
    /// How far a suggestion may be from the word for it to count as "just a typo". Two edits
    /// covers the ordinary slips; a surname is nowhere near a dictionary word.
    /// </summary>
    private const int TypoDistance = 2;

    private readonly Func<Core.Configuration.AppSettings> _settings;
    private readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private string? _tagsFor;
    private string[] _languageTags = [];

    public WindowsWordKnowledge(Func<Core.Configuration.AppSettings> settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool IsDictionaryWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return true;
        }

        EnsureLanguages();

        if (_languageTags.Length == 0)
        {
            // No dictionary at all: claim everything is a word, so nothing is masked on a
            // guess. Under-masking here is caught by the other rules; over-masking would
            // scramble the text for no reason.
            return true;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(word, out var cached))
            {
                return cached;
            }

            var known = Ask(word);

            // A short text asks about the same word repeatedly; the COM round trip is the
            // expensive part here.
            if (_cache.Count < 4096)
            {
                _cache[word] = known;
            }

            return known;
        }
    }

    /// <summary>Resolves the dictionaries once, and again when the language setting changed.</summary>
    private void EnsureLanguages()
    {
        var settings = _settings();
        var key = settings.Language ?? string.Empty;

        lock (_gate)
        {
            if (string.Equals(_tagsFor, key, StringComparison.Ordinal))
            {
                return;
            }

            _languageTags = [.. WindowsSpellCheckEngine.AvailableLanguageTags(settings)];
            _tagsFor = key;
            _cache.Clear();
        }
    }

    private bool Ask(string word)
    {
        foreach (var tag in _languageTags)
        {
            var checker = WindowsSpellCheckEngine.CheckerFor(tag);
            if (checker is null)
            {
                continue;
            }

            try
            {
                checker.Check(word, out var errors);
                if (errors is null || errors.Next(out var first) != 0 || first is null)
                {
                    // No complaint from this language: an ordinary word.
                    return true;
                }

                if (HasCloseSuggestion(checker, word))
                {
                    return true;
                }
            }
            catch (COMException ex)
            {
                Log.Debug($"Word lookup failed for {tag}: 0x{ex.HResult:X8}");
            }
        }

        return false;
    }

    /// <summary>True when the dictionary offers something close enough to be a typo.</summary>
    private static bool HasCloseSuggestion(NativeMethods.ISpellChecker checker, string word)
    {
        checker.Suggest(word, out var suggestions);
        if (suggestions is null)
        {
            return false;
        }

        var buffer = new string[1];
        var fetched = IntPtr.Zero;

        try
        {
            // Only the first few suggestions matter: they come back best first, so if none of
            // those is close, none of the rest is either.
            for (var index = 0; index < 5; index++)
            {
                if (suggestions.Next(1, buffer, fetched) != 0 || buffer[0] is null)
                {
                    return false;
                }

                if (Distance(word, buffer[0], TypoDistance) <= TypoDistance)
                {
                    return true;
                }
            }
        }
        catch (COMException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Levenshtein distance, given up on as soon as it exceeds <paramref name="limit"/>. The
    /// exact value beyond the limit is of no interest and computing it would only cost time.
    /// </summary>
    internal static int Distance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var best = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] +
                    (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1);

                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
                best = Math.Min(best, current[j]);
            }

            if (best > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
