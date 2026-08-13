using System.Text;
using AutoCorrect.Core.Caching;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Engines.Llm;

namespace AutoCorrect.Core.Tests.Cases;

public static class LlmEngineTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("LLM: handles every mode, correcting included", () =>
        {
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => new AppSettings());

            // Correcting needs grammar in context: a spell checker passes "Halo dass ist ein
            // tEst." untouched because every word in it exists.
            Assert.True(engine.SupportsMode(ProcessingMode.Correct));
            Assert.True(engine.SupportsMode(ProcessingMode.Rephrase));
            Assert.True(engine.SupportsMode(ProcessingMode.Formal));
            Assert.True(engine.SupportsMode(ProcessingMode.Shorten));
        });

        runner.Add("LLM: correcting is told not to rewrite the text", async () =>
        {
            using var server = FakeLlmServer.Streaming("Hallo, das ist ein Test.");
            var settings = new AppSettings { LlmEndpoint = server.Endpoint };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            await DrainAsync(engine, "Halo dass ist ein tEst.", ProcessingMode.Correct);

            var body = server.LastRequestBody!;
            Assert.Contains("Korrigiere", body);
            // Without this the model returns a nicer sentence instead of the corrected one.
            Assert.Contains("keine andere Wortwahl", body);
        });

        runner.Add("LLM: streams the tokens one by one", async () =>
        {
            using var server = FakeLlmServer.Streaming("Der ", "Text ", "ist ", "besser.");

            var chunks = await CollectChunksAsync(server, "Der Text ist gut.", ProcessingMode.Rephrase);

            // Arriving in pieces is the point: the popup fills up while the model works. Where
            // exactly the pieces are cut is up to the filter, so only "more than one" is asserted.
            Assert.True(chunks.Count > 1, $"the answer arrived in one lump ({chunks.Count} chunk)");
            Assert.Equal("Der Text ist besser.", string.Concat(chunks));
        });

        runner.Add("LLM: an answer without [DONE] is still complete", async () =>
        {
            using var server = new FakeLlmServer(_ => new FakeLlmServer.ChatReply
            {
                Tokens = ["Fertig."],
                SendDone = false,
            });

            Assert.Equal("Fertig.", string.Concat(await CollectChunksAsync(server, "Test", ProcessingMode.Shorten)));
        });

        runner.Add("LLM: nothing after [DONE] is read", async () =>
        {
            using var server = new FakeLlmServer(_ => new FakeLlmServer.ChatReply
            {
                Tokens = ["Kurz."],
                RawLines = ["data: [DONE]", """data: {"choices":[{"delta":{"content":"zu spaet"}}]}"""],
                SendDone = false,
            });

            Assert.Equal("Kurz.", string.Concat(await CollectChunksAsync(server, "Test", ProcessingMode.Shorten)));
        });

        runner.Add("LLM: a malformed chunk does not abort the answer", async () =>
        {
            using var server = new FakeLlmServer(_ => new FakeLlmServer.ChatReply
            {
                Tokens = ["Anfang "],
                RawLines =
                [
                    "data: {kaputt",
                    ": keep-alive comment",
                    """data: {"choices":[{"delta":{"content":"und Ende."}}]}""",
                ],
            });

            Assert.Equal(
                "Anfang und Ende.",
                string.Concat(await CollectChunksAsync(server, "Test", ProcessingMode.Rephrase)));
        });

        runner.Add("LLM: the request carries model, streaming and both prompts", async () =>
        {
            using var server = FakeLlmServer.Streaming("ok");

            var settings = new AppSettings
            {
                LlmEndpoint = server.Endpoint,
                LlmModel = "test-model",
            };

            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);
            await DrainAsync(engine, "Bitte umformulieren.", ProcessingMode.Formal);

            Assert.NotNull(server.LastRequestBody);
            var body = server.LastRequestBody!;

            Assert.Contains("\"model\":\"test-model\"", body);
            Assert.Contains("\"stream\":true", body);
            Assert.Contains("\"role\":\"system\"", body);
            Assert.Contains("\"role\":\"user\"", body);
            // The formal instruction, not the rephrase one.
            Assert.Contains("f\\u00F6rmlicher", body);
            Assert.Contains("Bitte umformulieren.", body);
        });

        runner.Add("LLM: an empty model name falls back to the default", async () =>
        {
            using var server = FakeLlmServer.Streaming("ok");
            server.InstalledModels = [LlmEngine.DefaultModel];

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "  " };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);
            await DrainAsync(engine, "Test", ProcessingMode.Rephrase);

            Assert.Contains($"\"model\":\"{LlmEngine.DefaultModel}\"", server.LastRequestBody!);
        });

        runner.Add("LLM: with nothing installed the pull command is shown", async () =>
        {
            using var server = new FakeLlmServer(_ =>
                FakeLlmServer.ChatReply.Error(404, """{"error":"model not found"}"""))
            {
                InstalledModels = [],
            };

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "qwen2.5:3b" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            var ex = await Assert.ThrowsAsync<EngineUnavailableException>(
                () => DrainAsync(engine, "Test", ProcessingMode.Rephrase));

            Assert.Contains("ollama pull qwen2.5:3b", ex.Message);
            // The request must not even be attempted when nothing can answer it.
            Assert.Equal(0, server.ChatRequestCount);
        });

        // ------------------------------------------------------------ choosing a model

        runner.Add("LLM: an installed model is used even under a different name", async () =>
        {
            // The reported problem: a model is sitting right there, but the configured name is
            // a slightly different tag, and the answer was "model not loaded".
            using var server = FakeLlmServer.Streaming("Antwort");
            server.InstalledModels = ["llama3.2:3b"];

            var settings = new AppSettings
            {
                LlmEndpoint = server.Endpoint,
                LlmModel = "qwen2.5:3b-instruct-q4_K_M",
            };

            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);
            await DrainAsync(engine, "Test", ProcessingMode.Rephrase);

            Assert.Contains("\"model\":\"llama3.2:3b\"", server.LastRequestBody!);
        });

        runner.Add("LLM: the configured model wins when it is installed", async () =>
        {
            using var server = FakeLlmServer.Streaming("Antwort");
            server.InstalledModels = ["llama3.2:1b", "qwen2.5:7b", "qwen2.5:3b"];

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "qwen2.5:7b" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);
            await DrainAsync(engine, "Test", ProcessingMode.Rephrase);

            Assert.Contains("\"model\":\"qwen2.5:7b\"", server.LastRequestBody!);
        });

        runner.Add("LLM: a differently tagged variant of the configured model counts as a match", async () =>
        {
            using var server = FakeLlmServer.Streaming("Antwort");
            server.InstalledModels = ["qwen2.5:3b-instruct-q4_K_M"];

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "qwen2.5:3b" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);
            await DrainAsync(engine, "Test", ProcessingMode.Rephrase);

            Assert.Contains("\"model\":\"qwen2.5:3b-instruct-q4_K_M\"", server.LastRequestBody!);
        });

        runner.Add("LLM: the model list is read once, not before every request", async () =>
        {
            using var server = FakeLlmServer.Streaming("Antwort");
            server.InstalledModels = ["llama3.2:3b"];

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "nicht-da" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            await DrainAsync(engine, "Eins", ProcessingMode.Rephrase);
            await DrainAsync(engine, "Zwei", ProcessingMode.Rephrase);
            await DrainAsync(engine, "Drei", ProcessingMode.Rephrase);

            Assert.Equal(1, server.ModelListRequestCount);
            Assert.Equal(3, server.ChatRequestCount);
        });

        runner.Add("LLM: an unreachable endpoint explains how to install Ollama", async () =>
        {
            // Port 1 never answers.
            var settings = new AppSettings { LlmEndpoint = "http://127.0.0.1:1/v1" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            var ex = await Assert.ThrowsAsync<EngineUnavailableException>(
                () => DrainAsync(engine, "Test", ProcessingMode.Rephrase));

            Assert.Contains("ollama pull", ex.Message);
        });

        runner.Add("LLM: a refused connection is not retried straight away", async () =>
        {
            var settings = new AppSettings { LlmEndpoint = "http://127.0.0.1:1/v1" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            await Assert.ThrowsAsync<EngineUnavailableException>(
                () => DrainAsync(engine, "Test", ProcessingMode.Rephrase));

            var second = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<EngineUnavailableException>(
                () => DrainAsync(engine, "Test", ProcessingMode.Rephrase));
            second.Stop();

            Assert.True(
                second.ElapsedMilliseconds <= 5,
                $"the second attempt still went to the network ({second.ElapsedMilliseconds} ms)");
        });

        runner.Add("LLM: blank input is returned without a request", async () =>
        {
            using var server = FakeLlmServer.Streaming("nie");
            var settings = new AppSettings { LlmEndpoint = server.Endpoint };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            Assert.Equal("  ", string.Concat(await CollectChunksAsync(engine, "  ", ProcessingMode.Rephrase)));
            Assert.Equal(0, server.ChatRequestCount);
        });

        runner.Add("LLM: cancellation is honoured", async () =>
        {
            using var server = FakeLlmServer.Streaming("a", "b", "c");
            var settings = new AppSettings { LlmEndpoint = server.Endpoint };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in engine.ProcessAsync("Test", ProcessingMode.Rephrase, cts.Token))
                {
                }
            });
        });

        runner.Add("LLM: availability follows the endpoint", async () =>
        {
            using var server = FakeLlmServer.Streaming("ok");
            var settings = new AppSettings { LlmEndpoint = server.Endpoint };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings);

            Assert.True(await engine.IsAvailableAsync(CancellationToken.None), "the fake server should answer");

            server.ModelsAvailable = false;
            Assert.False(await engine.IsAvailableAsync(CancellationToken.None), "a 503 is not available");

            settings.LlmEndpoint = "http://127.0.0.1:1/v1";
            Assert.False(await engine.IsAvailableAsync(CancellationToken.None), "a closed port is not available");
        });

        runner.Add("LLM: URLs are derived from the base address", () =>
        {
            Assert.Equal(
                "http://localhost:11434/v1/chat/completions",
                LlmEngine.ChatUrl(new AppSettings { LlmEndpoint = "http://localhost:11434/v1" }));
            Assert.Equal(
                "http://localhost:11434/v1/chat/completions",
                LlmEngine.ChatUrl(new AppSettings { LlmEndpoint = "http://localhost:11434/v1/" }));
            Assert.Equal(
                "http://localhost:8080/v1/models",
                LlmEngine.ModelsUrl(new AppSettings { LlmEndpoint = "http://localhost:8080/v1" }));
            Assert.Equal(
                $"{LlmEngine.DefaultEndpoint}/models",
                LlmEngine.ModelsUrl(new AppSettings { LlmEndpoint = "" }));
        });

        // ------------------------------------------------------------ caching

        runner.Add("LLM: two names for the same model share a cache entry", async () =>
        {
            // A renamed tag must not throw the cache away: the answer came from the same model.
            using var server = FakeLlmServer.Streaming("Gleiche Antwort.");
            server.InstalledModels = ["qwen2.5:3b"];

            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "qwen2.5:3b" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings, cache);

            await DrainAsync(engine, "Ein Satz.", ProcessingMode.Shorten);

            settings.LlmModel = "qwen2.5:3b-instruct-q4_K_M";
            engine.ResetResolvedModel();
            await DrainAsync(engine, "Ein Satz.", ProcessingMode.Shorten);

            Assert.Equal(1, server.ChatRequestCount);
        });

        runner.Add("LLM: a repeated request is answered from the cache", async () =>
        {
            using var server = FakeLlmServer.Streaming("Kurz ", "und ", "knapp.");
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "test-model" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings, cache);

            var first = string.Concat(await CollectChunksAsync(engine, "Ein Satz.", ProcessingMode.Shorten));
            var second = string.Concat(await CollectChunksAsync(engine, "Ein Satz.", ProcessingMode.Shorten));

            Assert.Equal("Kurz und knapp.", first);
            Assert.Equal(first, second);
            Assert.Equal(1, server.ChatRequestCount);
        });

        runner.Add("LLM: mode and model are part of the cache key", async () =>
        {
            using var server = FakeLlmServer.Streaming("Antwort");
            // Both have to exist on the server: the key uses the model that really answered, so
            // two names resolving to the same model would share a cache entry - correctly.
            server.InstalledModels = ["model-a", "model-b"];

            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            var settings = new AppSettings { LlmEndpoint = server.Endpoint, LlmModel = "model-a" };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings, cache);

            await DrainAsync(engine, "Ein Satz.", ProcessingMode.Shorten);
            await DrainAsync(engine, "Ein Satz.", ProcessingMode.Formal);
            Assert.Equal(2, server.ChatRequestCount);

            settings.LlmModel = "model-b";
            await DrainAsync(engine, "Ein Satz.", ProcessingMode.Shorten);
            Assert.Equal(3, server.ChatRequestCount);
            Assert.Contains("\"model\":\"model-b\"", server.LastRequestBody!);
        });

        runner.Add("LLM: a cancelled answer is not cached", async () =>
        {
            using var server = FakeLlmServer.Streaming("erstes ", "zweites ", "drittes");
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            var settings = new AppSettings { LlmEndpoint = server.Endpoint };
            using var http = new HttpClient();
            var engine = new LlmEngine(http, () => settings, cache);

            using var cts = new CancellationTokenSource();

            try
            {
                await foreach (var chunk in engine.ProcessAsync("Ein Satz.", ProcessingMode.Rephrase, cts.Token))
                {
                    // Stop after the first token, the way the user closing the popup does.
                    await cts.CancelAsync();
                    break;
                }
            }
            catch (OperationCanceledException)
            {
            }

            // A fragment cached as if it were the whole answer would be handed back next time.
            Assert.Equal(0, cache.Count());
        });
    }

    private static async Task<List<string>> CollectChunksAsync(
        FakeLlmServer server,
        string input,
        ProcessingMode mode)
    {
        var settings = new AppSettings { LlmEndpoint = server.Endpoint };
        using var http = new HttpClient();
        return await CollectChunksAsync(new LlmEngine(http, () => settings), input, mode);
    }

    private static async Task<List<string>> CollectChunksAsync(
        ITextEngine engine,
        string input,
        ProcessingMode mode)
    {
        var chunks = new List<string>();
        await foreach (var chunk in engine.ProcessAsync(input, mode, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static async Task DrainAsync(ITextEngine engine, string input, ProcessingMode mode)
    {
        var sink = new StringBuilder();
        await foreach (var chunk in engine.ProcessAsync(input, mode, CancellationToken.None))
        {
            sink.Append(chunk);
        }
    }
}
