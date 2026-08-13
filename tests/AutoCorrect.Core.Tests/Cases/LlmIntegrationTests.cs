using System.Diagnostics;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Engines.Llm;

namespace AutoCorrect.Core.Tests.Cases;

/// <summary>
/// Runs against a real OpenAI compatible server (Ollama, llama.cpp, LM Studio) instead of the
/// fake one. Skipped unless AUTOCORRECT_LLM_ENDPOINT is set, so the normal test run stays
/// offline and fast.
///
///   ollama pull qwen2.5:3b-instruct-q4_K_M
///   AUTOCORRECT_LLM_ENDPOINT=http://localhost:11434/v1 dotnet run --project tests/AutoCorrect.Core.Tests
///
/// AUTOCORRECT_LLM_MODEL overrides the model name.
///
/// The assertions check what can be checked about generated text: that something came back,
/// that it is not the instruction echoed, that shortening shortens. Asserting on exact wording
/// would only produce a test that fails on the next model.
/// </summary>
public static class LlmIntegrationTests
{
    public const string EndpointVariable = "AUTOCORRECT_LLM_ENDPOINT";
    public const string ModelVariable = "AUTOCORRECT_LLM_MODEL";

    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EndpointVariable));

    public static void Register(TestRunner runner)
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointVariable)!;
        var model = Environment.GetEnvironmentVariable(ModelVariable);

        runner.Add("LLM integration: the server is reachable", async () =>
        {
            var engine = CreateEngine(endpoint, model);
            Assert.True(
                await engine.IsAvailableAsync(CancellationToken.None),
                $"no OpenAI compatible server at {endpoint}");
        });

        runner.Add("LLM integration: rephrasing returns different, non-empty text", async () =>
        {
            const string input = "Das Meeting ist am Montag und es ist wichtig dass alle da sind.";
            var result = await RunAsync(endpoint, model, input, ProcessingMode.Rephrase);

            Assert.True(result.Length > 10, $"answer too short: <{result}>");
            Assert.False(
                result.Contains("Formuliere den folgenden Text", StringComparison.Ordinal),
                $"the instruction was echoed: {result}");
            Assert.False(result.StartsWith('"'), $"the answer is wrapped in quotation marks: {result}");
            Assert.False(result.Contains("---", StringComparison.Ordinal), $"a fence was echoed: {result}");
        });

        runner.Add("LLM integration: shortening actually shortens", async () =>
        {
            const string input =
                "Ich wollte Ihnen an dieser Stelle noch kurz mitteilen, dass wir uns sehr darüber " +
                "gefreut haben, dass Sie sich die Zeit genommen haben, um an unserem Treffen am " +
                "vergangenen Montagnachmittag teilzunehmen.";

            var result = await RunAsync(endpoint, model, input, ProcessingMode.Shorten);

            Assert.True(result.Length > 0, "empty answer");
            Assert.True(result.Length < input.Length, $"not shorter: {input.Length} -> {result.Length}");
        });

        runner.Add("LLM integration: the formal mode answers in German", async () =>
        {
            var result = await RunAsync(endpoint, model, "Hey, schick mir mal die Unterlagen.", ProcessingMode.Formal);

            Assert.True(result.Length > 0, "empty answer");
            Assert.False(result.Contains('ß'), $"Swiss spelling violated: {result}");
        });

        runner.Add("LLM integration: English input stays English", async () =>
        {
            var result = await RunAsync(endpoint, model, "The meeting is on monday and its important.", ProcessingMode.Rephrase);

            Assert.True(result.Length > 0, "empty answer");
            Assert.Contains("meeting", result.ToLowerInvariant());
        });

        runner.Add("LLM integration: the answer arrives in pieces", async () =>
        {
            var engine = CreateEngine(endpoint, model);
            var chunks = new List<string>();

            await foreach (var chunk in engine.ProcessAsync(
                "Bitte formuliere diesen etwas längeren Satz für mich anders, damit er klarer wird.",
                ProcessingMode.Rephrase,
                CancellationToken.None))
            {
                chunks.Add(chunk);
            }

            // Streaming is the whole point: the popup must fill up while the model works.
            Assert.True(chunks.Count > 1, $"the answer arrived in one lump ({chunks.Count} chunk)");
        });

        runner.Add("LLM integration: the second identical request comes from the cache", async () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new Caching.ResultCache(temp.Path);

            var settings = Settings(endpoint, model);
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var engine = new LlmEngine(http, () => settings, cache);

            const string input = "Der Termin verschiebt sich leider um eine Woche nach hinten.";

            var first = Stopwatch.StartNew();
            var firstResult = await CollectAsync(engine, input, ProcessingMode.Formal);
            first.Stop();

            var second = Stopwatch.StartNew();
            var secondResult = await CollectAsync(engine, input, ProcessingMode.Formal);
            second.Stop();

            Assert.Equal(firstResult, secondResult);
            Assert.True(
                second.ElapsedMilliseconds < 100,
                $"the second request took {second.ElapsedMilliseconds} ms, so it was generated again");
        });

        runner.Add("LLM integration: a missing model is reported clearly", async () =>
        {
            var settings = Settings(endpoint, "kein-modell-mit-diesem-namen");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var engine = new LlmEngine(http, () => settings);

            var ex = await Assert.ThrowsAsync<EngineUnavailableException>(
                () => CollectAsync(engine, "Test", ProcessingMode.Rephrase));

            Assert.Contains("kein-modell-mit-diesem-namen", ex.Message);
        });
    }

    private static AppSettings Settings(string endpoint, string? model) => new()
    {
        LlmEndpoint = endpoint,
        LlmModel = string.IsNullOrWhiteSpace(model) ? LlmEngine.DefaultModel : model,
    };

    private static ITextEngine CreateEngine(string endpoint, string? model)
    {
        // No client timeout: the engine cancels per request, and a cold model needs a while.
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        return new LlmEngine(http, () => Settings(endpoint, model));
    }

    private static Task<string> RunAsync(string endpoint, string? model, string input, ProcessingMode mode) =>
        CollectAsync(CreateEngine(endpoint, model), input, mode);

    private static async Task<string> CollectAsync(ITextEngine engine, string input, ProcessingMode mode)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var chunk in engine.ProcessAsync(input, mode, CancellationToken.None))
        {
            text.Append(chunk);
        }

        return text.ToString();
    }
}
