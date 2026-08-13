using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Engines.LanguageTool;

namespace AutoCorrect.Core.Tests.Cases;

public static class LanguageToolEngineTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("LanguageTool: supports only the correction mode", () =>
        {
            using var http = new HttpClient();
            var engine = new LanguageToolEngine(http, () => new AppSettings());

            Assert.True(engine.SupportsMode(ProcessingMode.Correct));
            Assert.False(engine.SupportsMode(ProcessingMode.Rephrase));
            Assert.False(engine.SupportsMode(ProcessingMode.Formal));
            Assert.False(engine.SupportsMode(ProcessingMode.Shorten));
        });

        runner.Add("LanguageTool: applies the first suggestion of every match", async () =>
        {
            const string input = "Ich habe gestern ein Buch gelest und dan geschlafen.";
            var gelest = input.IndexOf("gelest", StringComparison.Ordinal);
            var dan = input.IndexOf("dan", StringComparison.Ordinal);

            using var server = new FakeLanguageToolServer(_ => (200, $$"""
                {"matches":[
                  {"offset":{{gelest}},"length":6,"replacements":[{"value":"gelesen"},{"value":"geloest"}]},
                  {"offset":{{dan}},"length":3,"replacements":[{"value":"dann"}]}
                ]}
                """));

            var result = await CollectAsync(server, input);
            Assert.Equal("Ich habe gestern ein Buch gelesen und dann geschlafen.", result);
        });

        runner.Add("LanguageTool: asks for automatic detection with Swiss and US variants", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));
            await CollectAsync(server, "Ein Test.");

            Assert.NotNull(server.LastRequestBody);
            var decoded = Uri.UnescapeDataString(server.LastRequestBody!.Replace("+", " ", StringComparison.Ordinal));

            // Detection covers German and English in one session; the preferred variants stop
            // LanguageTool from treating Swiss German as de-DE and proposing "ß".
            Assert.Contains("language=auto", decoded);
            Assert.Contains("preferredVariants=de-CH,en-US", decoded);
            Assert.Contains("text=Ein Test.", decoded);
        });

        runner.Add("LanguageTool: an explicit language is sent without preferredVariants", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));

            var settings = new AppSettings
            {
                LanguageToolEndpoint = server.CheckEndpoint,
                Language = "en-GB",
            };

            using var http = new HttpClient();
            var engine = new LanguageToolEngine(http, () => settings);
            await foreach (var _ in engine.ProcessAsync("A test.", ProcessingMode.Correct, CancellationToken.None))
            {
            }

            var decoded = Uri.UnescapeDataString(server.LastRequestBody!.Replace("+", " ", StringComparison.Ordinal));
            Assert.Contains("language=en-GB", decoded);
            Assert.False(decoded.Contains("preferredVariants", StringComparison.Ordinal),
                "preferredVariants must only be sent for automatic detection");
        });

        runner.Add("LanguageTool: yields exactly one element", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """
                {"matches":[{"offset":0,"length":3,"replacements":[{"value":"Der"}]}]}
                """));

            var settings = new AppSettings { LanguageToolEndpoint = server.CheckEndpoint };
            using var http = new HttpClient();
            var engine = new LanguageToolEngine(http, () => settings);

            var chunks = new List<string>();
            await foreach (var chunk in engine.ProcessAsync("Das Haus", ProcessingMode.Correct, CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            Assert.Equal(1, chunks.Count);
            Assert.Equal("Der Haus", chunks[0]);
        });

        runner.Add("LanguageTool: text without findings comes back unchanged", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));
            Assert.Equal("Alles korrekt.", await CollectAsync(server, "Alles korrekt."));
        });

        runner.Add("LanguageTool: matches without a suggestion are ignored", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """
                {"matches":[{"offset":0,"length":5,"replacements":[],"rule":{"id":"HINT_ONLY"}}]}
                """));

            Assert.Equal("Hallo Welt", await CollectAsync(server, "Hallo Welt"));
        });

        runner.Add("LanguageTool: blank input is returned without a request", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));
            Assert.Equal("   ", await CollectAsync(server, "   "));
            Assert.Equal(0, server.RequestCount);
        });

        runner.Add("LanguageTool: unreachable server reports how to start it", async () =>
        {
            var settings = new AppSettings
            {
                // Port 1 is never a LanguageTool server.
                LanguageToolEndpoint = "http://127.0.0.1:1/v2/check",
                RequestTimeoutSeconds = 5,
            };

            using var http = new HttpClient();
            var engine = new LanguageToolEngine(http, () => settings);

            var ex = await Assert.ThrowsAsync<EngineUnavailableException>(async () =>
            {
                await foreach (var _ in engine.ProcessAsync("Test", ProcessingMode.Correct, CancellationToken.None))
                {
                }
            });

            Assert.Contains("LanguageTool ist nicht erreichbar", ex.Message);
            Assert.Contains("org.languagetool.server.HTTPServer", ex.Message);
        });

        runner.Add("LanguageTool: server error is reported as unavailable", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (500, "boom"));
            await Assert.ThrowsAsync<EngineUnavailableException>(() => CollectAsync(server, "Test"));
        });

        runner.Add("LanguageTool: unparseable response is reported as unavailable", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, "<html>not json</html>"));
            await Assert.ThrowsAsync<EngineUnavailableException>(() => CollectAsync(server, "Test"));
        });

        runner.Add("LanguageTool: unsupported mode throws", async () =>
        {
            using var http = new HttpClient();
            var engine = new LanguageToolEngine(http, () => new AppSettings());

            await Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                await foreach (var _ in engine.ProcessAsync("Test", ProcessingMode.Rephrase, CancellationToken.None))
                {
                }
            });
        });

        runner.Add("LanguageTool: cancellation is honoured", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));
            var settings = new AppSettings { LanguageToolEndpoint = server.CheckEndpoint };
            using var http = new HttpClient();
            var engine = new LanguageToolEngine(http, () => settings);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in engine.ProcessAsync("Test", ProcessingMode.Correct, cts.Token))
                {
                }
            });
        });

        runner.Add("LanguageTool: availability probe reflects the server state", async () =>
        {
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));
            var settings = new AppSettings { LanguageToolEndpoint = server.CheckEndpoint };
            using var http = new HttpClient();

            var engine = new LanguageToolEngine(http, () => settings);
            Assert.True(await engine.IsAvailableAsync(CancellationToken.None), "server should be reported as available");

            settings.LanguageToolEndpoint = "http://127.0.0.1:1/v2/check";
            Assert.False(await engine.IsAvailableAsync(CancellationToken.None), "closed port should not be available");
        });

        runner.Add("LanguageTool: languages probe URL is derived from the check URL", () =>
        {
            Assert.Equal(
                "http://localhost:8081/v2/languages",
                LanguageToolEngine.BuildLanguagesUrl("http://localhost:8081/v2/check"));
            Assert.Equal(
                "http://localhost:8081/v2/languages",
                LanguageToolEngine.BuildLanguagesUrl("http://localhost:8081/v2/check/"));
            Assert.Equal(
                "http://localhost:8081/v2/languages",
                LanguageToolEngine.BuildLanguagesUrl("http://localhost:8081/v2"));
        });

        runner.Add("LanguageTool: long text is transported correctly", async () =>
        {
            var input = string.Join(" ", Enumerable.Repeat("Dies ist ein etwas laengerer Satz.", 120));
            using var server = new FakeLanguageToolServer(_ => (200, """{"matches":[]}"""));
            Assert.Equal(input, await CollectAsync(server, input));
        });
    }

    private static async Task<string> CollectAsync(FakeLanguageToolServer server, string input)
    {
        var settings = new AppSettings { LanguageToolEndpoint = server.CheckEndpoint };
        using var http = new HttpClient();
        var engine = new LanguageToolEngine(http, () => settings);

        var result = new System.Text.StringBuilder();
        await foreach (var chunk in engine.ProcessAsync(input, ProcessingMode.Correct, CancellationToken.None))
        {
            result.Append(chunk);
        }

        return result.ToString();
    }
}
