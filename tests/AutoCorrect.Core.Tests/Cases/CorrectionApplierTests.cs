using AutoCorrect.Core.Engines.LanguageTool;

namespace AutoCorrect.Core.Tests.Cases;

public static class CorrectionApplierTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("CorrectionApplier: no corrections returns input unchanged", () =>
        {
            var input = "Das ist ein Satz.";
            Assert.Equal(input, CorrectionApplier.Apply(input, Array.Empty<TextCorrection>()));
        });

        runner.Add("CorrectionApplier: single replacement", () =>
        {
            var input = "Das ist ein Fehlar.";
            var result = CorrectionApplier.Apply(input, new[] { new TextCorrection(12, 6, "Fehler") });
            Assert.Equal("Das ist ein Fehler.", result);
        });

        runner.Add("CorrectionApplier: multiple replacements stay aligned (back to front)", () =>
        {
            // Two corrections where the first one changes the length. Applying front to back
            // would shift the second offset and corrupt the text.
            var input = "Ich habe gestern ein Buch gelest und dan geschlafen.";
            //           0123456789...
            var gelest = input.IndexOf("gelest", StringComparison.Ordinal);
            var dan = input.IndexOf("dan", StringComparison.Ordinal);

            var result = CorrectionApplier.Apply(input, new[]
            {
                new TextCorrection(gelest, 6, "gelesen"),
                new TextCorrection(dan, 3, "dann"),
            });

            Assert.Equal("Ich habe gestern ein Buch gelesen und dann geschlafen.", result);
        });

        runner.Add("CorrectionApplier: input order does not matter", () =>
        {
            var input = "aaa bbb ccc";
            var reversed = CorrectionApplier.Apply(input, new[]
            {
                new TextCorrection(8, 3, "CCC"),
                new TextCorrection(0, 3, "AAA"),
                new TextCorrection(4, 3, "BBB"),
            });

            Assert.Equal("AAA BBB CCC", reversed);
        });

        runner.Add("CorrectionApplier: replacement shorter than original", () =>
        {
            var input = "Er ist sehr sehr gross.";
            var result = CorrectionApplier.Apply(input, new[] { new TextCorrection(7, 9, "sehr") });
            Assert.Equal("Er ist sehr gross.", result);
        });

        runner.Add("CorrectionApplier: overlapping matches are skipped", () =>
        {
            var input = "das ist falsch";
            // Back to front means the match with the higher offset is applied first. The
            // earlier match overlaps the already rewritten region and is dropped, so the
            // result never mixes two conflicting suggestions.
            var result = CorrectionApplier.Apply(input, new[]
            {
                new TextCorrection(4, 10, "ist richtig"),
                new TextCorrection(8, 6, "korrekt"),
            });

            Assert.Equal("das ist korrekt", result);
        });

        runner.Add("CorrectionApplier: same offset prefers the longer match", () =>
        {
            var input = "z z z";
            var result = CorrectionApplier.Apply(input, new[]
            {
                new TextCorrection(0, 1, "kurz"),
                new TextCorrection(0, 3, "lang"),
            });

            Assert.Equal("lang z", result);
        });

        runner.Add("CorrectionApplier: out of range offsets are ignored", () =>
        {
            var input = "kurz";
            var result = CorrectionApplier.Apply(input, new[]
            {
                new TextCorrection(10, 5, "x"),
                new TextCorrection(-2, 2, "y"),
                new TextCorrection(2, 99, "z"),
            });

            Assert.Equal("kurz", result);
        });

        runner.Add("CorrectionApplier: no-op replacements leave text untouched", () =>
        {
            var input = "alles gut";
            var result = CorrectionApplier.Apply(input, new[] { new TextCorrection(0, 5, "alles") });
            Assert.Equal("alles gut", result);
        });

        runner.Add("CorrectionApplier: insertion via zero length match", () =>
        {
            var input = "Hallo Welt";
            var result = CorrectionApplier.Apply(input, new[] { new TextCorrection(10, 0, "!") });
            Assert.Equal("Hallo Welt!", result);
        });

        runner.Add("CorrectionApplier: offsets are UTF-16 code units, surrogate pairs stay intact", () =>
        {
            // LanguageTool is a Java service and reports Java char indices, which are UTF-16
            // code units, the same unit .NET strings use.
            var input = "Test \U0001F600 schoen";
            var offset = input.IndexOf("schoen", StringComparison.Ordinal);
            var result = CorrectionApplier.Apply(input, new[] { new TextCorrection(offset, 6, "schön") });
            Assert.Equal("Test \U0001F600 schön", result);
        });

        runner.Add("CorrectionApplier: Swiss spelling replacement", () =>
        {
            var input = "Die Strasse ist gross.";
            var result = CorrectionApplier.Apply(input, new[] { new TextCorrection(4, 7, "Strasse") });
            Assert.Equal("Die Strasse ist gross.", result);
        });
    }
}
