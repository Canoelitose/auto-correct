using AutoCorrect.Core.Engines.Llm;

namespace AutoCorrect.Core.Tests.Cases;

public static class ResponseFilterTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Filter: clean text is passed through unchanged", () =>
        {
            const string answer = "Wir treffen uns am Montag um 14 Uhr im Büro.";
            Assert.Equal(answer, Run(answer));
        });

        runner.Add("Filter: an announcement line is removed", () =>
        {
            Assert.Equal(
                "Wir treffen uns am Montag.",
                Run("Hier ist die umformulierte Version:\nWir treffen uns am Montag."));

            Assert.Equal(
                "We are meeting on Monday.",
                Run("Sure, here is the rephrased text:\nWe are meeting on Monday."));
        });

        runner.Add("Filter: a colon line that belongs to the text stays", () =>
        {
            // The dangerous false positive: a heading the user actually wrote.
            const string answer = "Wichtig:\nDas Treffen faellt aus.";
            Assert.Equal(answer, Run(answer));

            const string list = "Traktanden:\nEins, zwei, drei.";
            Assert.Equal(list, Run(list));
        });

        runner.Add("Filter: quotation marks around the whole answer are removed", () =>
        {
            Assert.Equal("Der Text ist besser.", Run("\"Der Text ist besser.\""));
            Assert.Equal("Der Text ist besser.", Run("„Der Text ist besser.“"));
            Assert.Equal("Der Text ist besser.", Run("«Der Text ist besser.»"));
        });

        runner.Add("Filter: a quoted original keeps its quotation marks", () =>
        {
            // The input was quoted, so a quoted answer is the correct answer.
            Assert.Equal(
                "\"Das ist meine Meinung\", sagte er.",
                Run("\"Das ist meine Meinung\", sagte er.", input: "\"Das ist meine Ansicht\", sagte er."));
        });

        runner.Add("Filter: a quotation mark only at the start is kept", () =>
        {
            // Nothing closes it, so it is part of the text, not wrapping.
            Assert.Equal("\"Ein angefangenes Zitat", Run("\"Ein angefangenes Zitat"));
        });

        runner.Add("Filter: an answer that quotes something itself is left alone", () =>
        {
            // Stripping the outer pair here would take the wrong two marks and wreck the text.
            const string answer = "\"Hallo\", sagte er, und sie antwortete: \"Tschuess\"";
            Assert.Equal(answer, Run(answer));
        });

        runner.Add("Filter: echoed fences are removed", () =>
        {
            Assert.Equal("Der neue Text.", Run("---\nDer neue Text.\n---"));
            Assert.Equal("Der neue Text.", Run("Der neue Text.\n---"));
        });

        runner.Add("Filter: a dashed line inside the text survives", () =>
        {
            const string answer = "Erstens - zweitens - drittens.";
            Assert.Equal(answer, Run(answer));
        });

        runner.Add("Filter: an announcement and quotes together are both removed", () =>
        {
            Assert.Equal(
                "Bitte senden Sie mir die Unterlagen zu.",
                Run("Hier ist die förmliche Version:\n\n\"Bitte senden Sie mir die Unterlagen zu.\""));
        });

        runner.Add("Filter: leading and trailing whitespace is removed", () =>
        {
            Assert.Equal("Kurz und knapp.", Run("\n\n   Kurz und knapp.   \n\n"));
        });

        runner.Add("Filter: paragraphs inside the answer are preserved", () =>
        {
            const string answer = "Erster Absatz.\n\nZweiter Absatz.\n\nDritter Absatz.";
            Assert.Equal(answer, Run(answer));
        });

        runner.Add("Filter: the token boundaries do not change the result", () =>
        {
            const string raw = "Hier ist die umformulierte Version:\n\"Ein deutlich besserer Satz.\"";
            const string expected = "Ein deutlich besserer Satz.";

            // Whatever way the model chops the answer up, the same text must come out.
            Assert.Equal(expected, RunChunked(raw, size: 1));
            Assert.Equal(expected, RunChunked(raw, size: 3));
            Assert.Equal(expected, RunChunked(raw, size: 7));
            Assert.Equal(expected, RunChunked(raw, size: 25));
            Assert.Equal(expected, RunChunked(raw, size: raw.Length));
        });

        runner.Add("Filter: a text that merely starts like an announcement survives", () =>
        {
            // "Klar" is an announcement opener, but this is the answer itself.
            const string answer = "Klar strukturierte Sätze lesen sich besser.";
            Assert.Equal(answer, Run(answer));

            // An announcement with nothing after it is all there is, so it stays.
            Assert.Equal("Hier ist die neue Fassung:", Run("Hier ist die neue Fassung:"));
        });

        runner.Add("Filter: an empty answer stays empty", () =>
        {
            Assert.Equal("", Run(""));
            Assert.Equal("", Run("   \n  "));
        });

        runner.Add("Filter: a long answer without a line break is not held back", () =>
        {
            // Nothing may be swallowed just because the answer is one long paragraph.
            var answer = string.Join(" ", Enumerable.Repeat("Ein ziemlich langer Satz.", 40));
            Assert.Equal(answer, Run(answer));
        });
    }

    /// <summary>Pushes the answer through in one piece.</summary>
    private static string Run(string answer, string input = "Ein Ausgangstext.") =>
        RunChunked(answer, answer.Length == 0 ? 1 : answer.Length, input);

    /// <summary>Pushes the answer through in fixed size chunks, the way tokens arrive.</summary>
    private static string RunChunked(string answer, int size, string input = "Ein Ausgangstext.")
    {
        var filter = new ResponseFilter(input);
        var result = new System.Text.StringBuilder();

        for (var offset = 0; offset < answer.Length; offset += size)
        {
            var chunk = answer.Substring(offset, Math.Min(size, answer.Length - offset));
            result.Append(filter.Push(chunk));
        }

        result.Append(filter.Finish());
        return result.ToString();
    }
}
