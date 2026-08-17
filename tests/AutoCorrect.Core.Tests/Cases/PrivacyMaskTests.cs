using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines.Llm;
using AutoCorrect.Core.Privacy;

namespace AutoCorrect.Core.Tests.Cases;

public static class PrivacyMaskTests
{
    /// <summary>
    /// Stands in for the Windows dictionaries: it knows a handful of ordinary words and treats
    /// a near miss as a typo, which is exactly the contract the real one has to fulfil.
    /// </summary>
    private static readonly FakeDictionary Dictionary = new(
    [
        "Ich", "habe", "gestern", "mit", "über", "die", "Rechnung", "gesprochen", "kommt", "am",
        "Montag", "Die", "für", "das", "Büro", "kommt", "im", "Januar", "liegt", "auf", "dem",
        "Tisch", "Das", "und", "brauchen", "Version", "Format", "treffen", "Herrn", "und",
    ]);

    public static void Register(TestRunner runner)
    {
        // ------------------------------------------------------------ what is found

        runner.Add("Mask: a first name and the surname after it are replaced", () =>
        {
            var mask = PrivacyMask.Create("Anna Meier kommt am Montag.");

            Assert.False(mask.Masked.Contains("Anna", StringComparison.Ordinal), mask.Masked);
            Assert.False(mask.Masked.Contains("Meier", StringComparison.Ordinal), mask.Masked);
            Assert.Contains("kommt am Montag.", mask.Masked);
        });

        runner.Add("Mask: a name after a salutation is replaced", () =>
        {
            var mask = PrivacyMask.Create("Sehr geehrter Herr Brunnenwieser, danke für Ihre Nachricht.");

            // The surname alone is unknown to any list; the salutation is what identifies it.
            Assert.False(mask.Masked.Contains("Brunnenwieser", StringComparison.Ordinal), mask.Masked);
            Assert.Contains("Sehr geehrter Herr ", mask.Masked);
            Assert.Contains("danke für Ihre Nachricht.", mask.Masked);
        });

        runner.Add("Mask: a title between salutation and name is handled", () =>
        {
            var mask = PrivacyMask.Create("Guten Tag Frau Dr. Hollenstein");
            Assert.False(mask.Masked.Contains("Hollenstein", StringComparison.Ordinal), mask.Masked);
        });

        runner.Add("Mask: words the user listed are always replaced", () =>
        {
            var mask = PrivacyMask.Create(
                "Das Projekt Nordwind läuft bei Firma Zwiebelturm gut.",
                ["Nordwind", "Zwiebelturm"]);

            Assert.False(mask.Masked.Contains("Nordwind", StringComparison.Ordinal), mask.Masked);
            Assert.False(mask.Masked.Contains("Zwiebelturm", StringComparison.Ordinal), mask.Masked);
        });

        runner.Add("Mask: a listed word is not matched inside another word", () =>
        {
            // "Ana" must not turn "Banane" into "Bananane".
            var mask = PrivacyMask.Create("Die Banane liegt da.", ["ana"]);
            Assert.Equal("Die Banane liegt da.", mask.Masked);
        });

        runner.Add("Mask: e-mail addresses are replaced", () =>
        {
            var mask = PrivacyMask.Create("Schreib an nils.engeli@example.org, danke.");

            Assert.False(mask.Masked.Contains("nils.engeli", StringComparison.Ordinal), mask.Masked);
            Assert.False(mask.Masked.Contains("example.org", StringComparison.Ordinal), mask.Masked);
            Assert.Contains("Schreib an ", mask.Masked);
            Assert.Contains(", danke.", mask.Masked);
        });

        runner.Add("Mask: phone numbers are replaced, ordinary numbers are not", () =>
        {
            var phone = PrivacyMask.Create("Ruf mich an: 079 123 45 67.");
            Assert.False(phone.Masked.Contains("079 123 45 67", StringComparison.Ordinal), phone.Masked);

            // A year, a price and a house number have to survive untouched.
            const string harmless = "Im Jahr 2024 kostete es 1500 Franken, Bahnhofstrasse 12.";
            Assert.Equal(harmless, PrivacyMask.Create(harmless).Masked);
        });

        runner.Add("Mask: an IBAN is replaced", () =>
        {
            var mask = PrivacyMask.Create("Bitte auf CH93 0076 2011 6238 5295 7 überweisen.");

            Assert.False(mask.Masked.Contains("6238", StringComparison.Ordinal), mask.Masked);
            Assert.Contains("überweisen.", mask.Masked);
        });

        // ------------------------------------------------------------ with a dictionary

        runner.Add("Mask: a surname nobody listed is found because it is not a word", () =>
        {
            // The rule that does the heavy lifting: German capitalises every noun, so the
            // question is not "is it capitalised" but "is it a word at all".
            var mask = PrivacyMask.Create(
                "Ich habe gestern mit Brunnenwieser über die Rechnung gesprochen.",
                null,
                Dictionary);

            Assert.False(mask.Masked.Contains("Brunnenwieser", StringComparison.Ordinal), mask.Masked);
            Assert.Contains("über die Rechnung gesprochen.", mask.Masked);
        });

        runner.Add("Mask: ordinary nouns stay even with a dictionary", () =>
        {
            const string input = "Die Rechnung für das Büro kommt im Januar.";
            Assert.Equal(input, PrivacyMask.Create(input, null, Dictionary).Masked);
        });

        runner.Add("Mask: a misspelled noun is corrected, not masked", () =>
        {
            // The trap: treat a typo as a name and it is replaced before sending, so the model
            // never sees it and the correction silently skips that word.
            const string input = "Die Rechnnung liegt auf dem Tisch.";
            Assert.Equal(input, PrivacyMask.Create(input, null, Dictionary).Masked);
        });

        runner.Add("Mask: abbreviations and product codes are not masked", () =>
        {
            // Capitalised, unknown, and not a person.
            const string input = "Das PDF und die AG brauchen Version2 im Format A4.";
            Assert.Equal(input, PrivacyMask.Create(input, null, Dictionary).Masked);
        });

        runner.Add("Mask: a name at the start of a sentence is found too", () =>
        {
            var mask = PrivacyMask.Create("Brunnenwieser kommt am Montag.", null, Dictionary);
            Assert.False(mask.Masked.Contains("Brunnenwieser", StringComparison.Ordinal), mask.Masked);
        });

        runner.Add("Mask: without a dictionary the same sentence keeps the name", () =>
        {
            // Honest about the limit: no dictionary, no detection of an unintroduced surname.
            const string input = "Ich habe gestern mit Brunnenwieser gesprochen.";
            Assert.Equal(input, PrivacyMask.Create(input).Masked);
        });

        runner.Add("Mask: a dictionary round trip still restores exactly", () =>
        {
            const string input = "Brunnenwieser und Anna Meier treffen Herrn Hollenstein.";
            var mask = PrivacyMask.Create(input, null, Dictionary);
            var restorer = new MaskRestorer(mask);

            Assert.Equal(input, restorer.Push(mask.Masked) + restorer.Finish());
        });

        // ------------------------------------------------------------ what is left alone

        runner.Add("Mask: a text without personal details is sent unchanged", () =>
        {
            const string input = "Der Termin für die Sitzung wird auf nächste Woche verschoben.";
            var mask = PrivacyMask.Create(input);

            Assert.Equal(input, mask.Masked);
            Assert.Equal(0, mask.ReplacementCount);
        });

        runner.Add("Mask: ordinary German nouns are not mistaken for names", () =>
        {
            // Every noun in German is capitalised. Masking on capitalisation alone would
            // destroy the sentence, so the detection must not do that.
            const string input = "Die Rechnung für das Büro und den Drucker kommt im Januar.";
            Assert.Equal(input, PrivacyMask.Create(input).Masked);
        });

        runner.Add("Mask: the word after a name at the end of a sentence stays", () =>
        {
            var mask = PrivacyMask.Create("Hallo Anna. Der Termin steht.");

            // "Der" opens the next sentence and is not a surname.
            Assert.Contains("Der Termin steht.", mask.Masked);
        });

        runner.Add("Mask: a sentence keeps its shape", () =>
        {
            var mask = PrivacyMask.Create("Anna Meier schreibt an peter@example.com wegen der Rechnung.");

            Assert.Contains(" schreibt an ", mask.Masked);
            Assert.Contains(" wegen der Rechnung.", mask.Masked);
        });

        // ------------------------------------------------------------ putting it back

        runner.Add("Mask: what went out comes back in", () =>
        {
            const string input = "Sehr geehrter Herr Brunnenwieser, Anna Meier meldet sich.";
            var mask = PrivacyMask.Create(input);

            // The model answers with the masked text unchanged; restoring must reproduce it.
            var restorer = new MaskRestorer(mask);
            var restored = restorer.Push(mask.Masked) + restorer.Finish();

            Assert.Equal(input, restored);
        });

        runner.Add("Mask: restoring works whatever way the answer is chopped up", () =>
        {
            const string input = "Anna Meier und Herr Brunnenwieser treffen sich.";
            var mask = PrivacyMask.Create(input);

            foreach (var size in new[] { 1, 2, 3, 5, 8, 13, 40 })
            {
                var restorer = new MaskRestorer(mask);
                var result = new System.Text.StringBuilder();

                for (var offset = 0; offset < mask.Masked.Length; offset += size)
                {
                    var chunk = mask.Masked.Substring(offset, Math.Min(size, mask.Masked.Length - offset));
                    result.Append(restorer.Push(chunk));
                }

                result.Append(restorer.Finish());
                Assert.Equal(input, result.ToString());
            }
        });

        runner.Add("Mask: the same person is always the same stand-in", () =>
        {
            var mask = PrivacyMask.Create("Anna Meier rief an. Anna Meier kommt später.");

            // Two different stand-ins for one person would let the service see there are two,
            // and would read as a mistake in the answer.
            Assert.Equal(2, mask.ReplacementCount);
        });

        runner.Add("Mask: two people get two different stand-ins", () =>
        {
            var mask = PrivacyMask.Create("Anna Meier schreibt an Peter Vogt.");
            var restorer = new MaskRestorer(mask);

            Assert.Equal(
                "Anna Meier schreibt an Peter Vogt.",
                restorer.Push(mask.Masked) + restorer.Finish());
        });

        runner.Add("Mask: a stand-in that occurs in the text is not used", () =>
        {
            // Otherwise restoring would put the wrong name over a word the user wrote.
            var mask = PrivacyMask.Create("Anna Muster und Alex Berger sitzen im Raum Muster.");
            var restorer = new MaskRestorer(mask);

            Assert.Equal(
                "Anna Muster und Alex Berger sitzen im Raum Muster.",
                restorer.Push(mask.Masked) + restorer.Finish());
        });

        runner.Add("Mask: nothing to restore lets the text straight through", () =>
        {
            var mask = PrivacyMask.Create("Der Termin steht.");
            var restorer = new MaskRestorer(mask);

            Assert.True(restorer.IsEmpty, "an empty mask should not buffer anything");
            Assert.Equal("Hallo", restorer.Push("Hallo"));
        });

        runner.Add("Mask: the same text always produces the same masked version", () =>
        {
            // The result cache is keyed on the original text; a mask that varied between runs
            // would make the cached answer belong to a different sentence.
            const string input = "Anna Meier schreibt an Peter Vogt wegen der Rechnung.";
            Assert.Equal(PrivacyMask.Create(input).Masked, PrivacyMask.Create(input).Masked);
        });

        // ------------------------------------------------------------ when it is used

        runner.Add("Mask: localhost and the local network count as local", () =>
        {
            Assert.False(LlmEngine.IsExternal("http://localhost:11434/v1"));
            Assert.False(LlmEngine.IsExternal("http://127.0.0.1:11434/v1"));
            Assert.False(LlmEngine.IsExternal("http://192.168.1.20:11434/v1"));
            Assert.False(LlmEngine.IsExternal("http://10.0.0.5:8080/v1"));
            Assert.False(LlmEngine.IsExternal("http://172.16.3.9:8080/v1"));
            Assert.False(LlmEngine.IsExternal("http://nas.local:11434/v1"));
        });

        runner.Add("Mask: an address on the internet counts as external", () =>
        {
            Assert.True(LlmEngine.IsExternal("https://integrate.api.nvidia.com/v1"));
            Assert.True(LlmEngine.IsExternal("https://api.example.com/v1"));
            Assert.True(LlmEngine.IsExternal("http://8.8.8.8/v1"));
        });

        runner.Add("Mask: automatic means local models see the text, hosted ones do not", () =>
        {
            var local = new AppSettings { LlmEndpoint = "http://localhost:11434/v1" };
            Assert.False(LlmEngine.MasksNames(local));

            var hosted = new AppSettings { LlmEndpoint = "https://integrate.api.nvidia.com/v1" };
            Assert.True(LlmEngine.MasksNames(hosted));
        });

        runner.Add("Mask: a cloud model on a local address is still masked", () =>
        {
            // Ollama serves its hosted models through the same localhost address as the local
            // ones. Judging by the address alone would report "kimi-k3:cloud" as local and
            // switch the masking off while the text travels to a data centre.
            var settings = new AppSettings
            {
                LlmEndpoint = "http://localhost:11434/v1",
                LlmModel = "kimi-k3:cloud",
            };

            Assert.True(LlmEngine.MasksNames(settings));
            Assert.True(LlmEngine.MasksNames(settings, "kimi-k3:cloud"));
        });

        runner.Add("Mask: a local model on a local address is not masked", () =>
        {
            var settings = new AppSettings { LlmEndpoint = "http://localhost:11434/v1" };

            Assert.False(LlmEngine.MasksNames(settings, "qwen2.5:3b"));
            Assert.False(LlmEngine.MasksNames(settings, "llama3.2:1b"));
        });

        runner.Add("Mask: only the tag counts as cloud, not the word inside a name", () =>
        {
            Assert.True(LlmEngine.IsHostedModel("kimi-k3:cloud"));
            Assert.True(LlmEngine.IsHostedModel("gpt-oss:120b-cloud"));

            // A model that merely has "cloud" somewhere in its name is not hosted.
            Assert.False(LlmEngine.IsHostedModel("cloudy-llama:7b"));
            Assert.False(LlmEngine.IsHostedModel("qwen2.5:3b"));
            Assert.False(LlmEngine.IsHostedModel(""));
            Assert.False(LlmEngine.IsHostedModel(null));
        });

        runner.Add("Mask: always and never override the automatic decision", () =>
        {
            var always = new AppSettings
            {
                LlmEndpoint = "http://localhost:11434/v1",
                LlmMaskNames = AppSettings.MaskNamesAlways,
            };
            Assert.True(LlmEngine.MasksNames(always));

            var never = new AppSettings
            {
                LlmEndpoint = "https://integrate.api.nvidia.com/v1",
                LlmMaskNames = AppSettings.MaskNamesNever,
            };
            Assert.False(LlmEngine.MasksNames(never));
        });

        runner.Add("Mask: an unreadable setting falls back to automatic", () =>
        {
            var settings = new AppSettings { LlmMaskNames = "vielleicht" };
            settings.Normalize();
            Assert.Equal(AppSettings.MaskNamesAuto, settings.LlmMaskNames);
        });
    }
}

/// <summary>A dictionary for the tests: known words, plus anything within one edit of one.</summary>
internal sealed class FakeDictionary : IWordKnowledge
{
    private readonly HashSet<string> _words;

    public FakeDictionary(IEnumerable<string> words) =>
        _words = new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);

    public bool IsDictionaryWord(string word)
    {
        if (_words.Contains(word))
        {
            return true;
        }

        // "Rechnnung" is a typo for "Rechnung", not a surname.
        return _words.Any(known => Math.Abs(known.Length - word.Length) <= 2 && Distance(known, word) <= 2);
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
