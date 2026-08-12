using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Engines.LanguageTool;

namespace AutoCorrect.Core.Tests.Cases;

/// <summary>
/// Runs against a real languagetool-standalone instead of the fake server. Skipped unless
/// AUTOCORRECT_LT_ENDPOINT is set, so the normal test run stays offline and fast.
///
///   AUTOCORRECT_LT_ENDPOINT=http://localhost:8081/v2/check dotnet run --project tests/AutoCorrect.Core.Tests
/// </summary>
public static class LanguageToolIntegrationTests
{
    public const string EndpointVariable = "AUTOCORRECT_LT_ENDPOINT";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointVariable));

    public static void Register(TestRunner runner)
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable)!;

        runner.Add("Integration: server is reachable", async () =>
        {
            Assert.True(await CreateEngine(endpoint).IsAvailableAsync(CancellationToken.None),
                $"no LanguageTool server at {endpoint}");
        });

        runner.Add("Integration: corrects a German sentence with several errors", async () =>
        {
            var result = await CorrectAsync(endpoint, "Ich habe gestern ein Buch gelest und dan geschlafen.");

            // Assert on the specific errors rather than the whole sentence: the exact wording of
            // LanguageTool suggestions changes between releases.
            Assert.False(result.Contains("gelest", StringComparison.Ordinal), $"'gelest' was not corrected: {result}");
            Assert.False(result.Contains(" dan ", StringComparison.Ordinal), $"'dan' was not corrected: {result}");
        });

        runner.Add("Integration: multiple corrections keep the sentence intact", async () =>
        {
            var result = await CorrectAsync(endpoint, "Der Hund lauft schnell und die Katze schlaft tief.");

            Assert.Contains("Hund", result);
            Assert.Contains("Katze", result);
            Assert.True(result.EndsWith('.'), $"punctuation was lost: {result}");
        });

        runner.Add("Integration: correct text is returned unchanged", async () =>
        {
            const string input = "Das ist ein vollständig korrekter Satz.";
            Assert.Equal(input, await CorrectAsync(endpoint, input));
        });

        runner.Add("Integration: de-CH does not introduce ß", async () =>
        {
            var result = await CorrectAsync(endpoint, "Die Strasse ist sehr gross und weiss gestrichen.");
            Assert.False(result.Contains('ß'), $"Swiss spelling violated: {result}");
        });

        runner.Add("Integration: offsets stay aligned in a long text", async () =>
        {
            // A long input is where wrong offset handling shows up: the tail of the text gets
            // shifted and the sentence turns into garbage.
            var sentences = new[]
            {
                "Ich habe gestern ein Buch gelest.",
                "Der Hund lauft ueber die Strasse.",
                "Wir haben das Fenster geoffnet.",
                "Sie hat das Essen gekochtt.",
                "Am Montag gehen wir ins Kino.",
            };

            var input = string.Join(" ", Enumerable.Repeat(string.Join(" ", sentences), 6));
            var result = await CorrectAsync(endpoint, input);

            Assert.Contains("Am Montag gehen wir ins Kino.", result);
            Assert.True(
                Math.Abs(result.Length - input.Length) < input.Length / 4,
                $"length changed implausibly: {input.Length} -> {result.Length}");
        });

        runner.Add("Integration: text with 5000 characters is processed", async () =>
        {
            var input = string.Join(" ", Enumerable.Repeat("Dies ist ein normaler Satz ohne Fehler.", 128));
            input = input[..Math.Min(5000, input.Length)];

            var result = await CorrectAsync(endpoint, input);
            Assert.True(result.Length > 0, "empty result for a long text");
        });
    }

    private static ITextEngine CreateEngine(string endpoint)
    {
        var settings = new AppSettings { LanguageToolEndpoint = endpoint, RequestTimeoutSeconds = 60 };
        return new LanguageToolEngine(new HttpClient(), () => settings);
    }

    private static async Task<string> CorrectAsync(string endpoint, string input)
    {
        var engine = CreateEngine(endpoint);
        var text = new System.Text.StringBuilder();

        await foreach (var chunk in engine.ProcessAsync(input, ProcessingMode.Correct, CancellationToken.None))
        {
            text.Append(chunk);
        }

        return text.ToString();
    }
}
