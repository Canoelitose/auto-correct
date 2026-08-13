using System.Runtime.CompilerServices;
using AutoCorrect.Core.Engines;

namespace AutoCorrect.Core.Tests.Cases;

public static class EngineRouterTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Router: forwards each mode to the engine that supports it", async () =>
        {
            var corrector = new StubEngine("Corrector", ProcessingMode.Correct);
            var llm = new StubEngine("LLM", ProcessingMode.Rephrase, ProcessingMode.Formal, ProcessingMode.Shorten);
            var router = new EngineRouter(corrector, llm);

            Assert.Equal("Corrector", router.Resolve(ProcessingMode.Correct)?.Name);
            Assert.Equal("LLM", router.Resolve(ProcessingMode.Formal)?.Name);

            await Collect(router, ProcessingMode.Shorten);
            Assert.Equal(1, llm.Calls);
            Assert.Equal(0, corrector.Calls);
        });

        runner.Add("Router: reports the union of all supported modes", () =>
        {
            var router = new EngineRouter(new StubEngine("Corrector", ProcessingMode.Correct));

            Assert.True(router.SupportsMode(ProcessingMode.Correct));
            Assert.False(router.SupportsMode(ProcessingMode.Rephrase));
        });

        runner.Add("Router: unsupported mode fails with a German message", async () =>
        {
            var router = new EngineRouter(new StubEngine("Corrector", ProcessingMode.Correct));

            var ex = await Assert.ThrowsAsync<EngineUnavailableException>(async () =>
            {
                await foreach (var _ in router.ProcessAsync("x", ProcessingMode.Formal, CancellationToken.None))
                {
                }
            });

            Assert.Contains("keine Engine", ex.Message);
        });

        runner.Add("Router: the first registered engine wins", () =>
        {
            var first = new StubEngine("First", ProcessingMode.Correct);
            var second = new StubEngine("Second", ProcessingMode.Correct);
            var router = new EngineRouter(first, second);

            Assert.Equal("First", router.Resolve(ProcessingMode.Correct)?.Name);
        });

        runner.Add("Router: availability is true when any engine answers", async () =>
        {
            var offline = new StubEngine("Offline", ProcessingMode.Correct) { Available = false };
            var online = new StubEngine("Online", ProcessingMode.Rephrase) { Available = true };

            Assert.False(await new EngineRouter(offline).IsAvailableAsync(CancellationToken.None));
            Assert.True(await new EngineRouter(offline, online).IsAvailableAsync(CancellationToken.None));
        });

        runner.Add("Router: falls back to the next engine when the first is unavailable", async () =>
        {
            var offline = new FailingEngine(ProcessingMode.Correct);
            var backup = new StubEngine("Backup", ProcessingMode.Correct) { Chunks = ["Ersatz"] };
            var router = new EngineRouter(offline, backup);

            Assert.Equal("Ersatz", await Collect(router, ProcessingMode.Correct));
            Assert.Equal("Backup", router.LastUsedName);
        });

        runner.Add("Router: the last engine's failure is reported", async () =>
        {
            var router = new EngineRouter(
                new FailingEngine(ProcessingMode.Correct),
                new FailingEngine(ProcessingMode.Correct));

            await Assert.ThrowsAsync<EngineUnavailableException>(async () =>
            {
                await foreach (var _ in router.ProcessAsync("x", ProcessingMode.Correct, CancellationToken.None))
                {
                }
            });
        });

        runner.Add("Router: no fallback once output has been produced", async () =>
        {
            // Switching engines mid result would glue two different answers together.
            var halfway = new HalfwayFailingEngine();
            var backup = new StubEngine("Backup", ProcessingMode.Correct);
            var router = new EngineRouter(halfway, backup);

            await Assert.ThrowsAsync<EngineUnavailableException>(async () =>
            {
                await foreach (var _ in router.ProcessAsync("x", ProcessingMode.Correct, CancellationToken.None))
                {
                }
            });
        });

        runner.Add("Router: reports which engine answered", async () =>
        {
            var router = new EngineRouter(new StubEngine("Backup", ProcessingMode.Correct));
            Assert.True(router.LastUsedName is null, "nothing ran yet");

            await Collect(router, ProcessingMode.Correct);
            Assert.Equal("Backup", router.LastUsedName);
        });

        runner.Add("Router: streamed chunks are passed through in order", async () =>
        {
            var engine = new StubEngine("Streamer", ProcessingMode.Rephrase) { Chunks = ["Das ", "ist ", "ein Test."] };
            var router = new EngineRouter(engine);

            Assert.Equal("Das ist ein Test.", await Collect(router, ProcessingMode.Rephrase));
        });
    }

    private static async Task<string> Collect(ITextEngine engine, ProcessingMode mode)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var chunk in engine.ProcessAsync("input", mode, CancellationToken.None))
        {
            text.Append(chunk);
        }

        return text.ToString();
    }

    /// <summary>Always reports its service as unreachable.</summary>
    private sealed class FailingEngine : ITextEngine
    {
        private readonly HashSet<ProcessingMode> _modes;

        public FailingEngine(params ProcessingMode[] modes) => _modes = new HashSet<ProcessingMode>(modes);

        public string Name => "Offline";

        public bool SupportsMode(ProcessingMode mode) => _modes.Contains(mode);

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(false);

        public async IAsyncEnumerable<string> ProcessAsync(
            string input,
            ProcessingMode mode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            throw new EngineUnavailableException("not reachable");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    /// <summary>Yields one chunk and only then fails.</summary>
    private sealed class HalfwayFailingEngine : ITextEngine
    {
        public string Name => "Halfway";

        public bool SupportsMode(ProcessingMode mode) => mode == ProcessingMode.Correct;

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);

        public async IAsyncEnumerable<string> ProcessAsync(
            string input,
            ProcessingMode mode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return "Anfang ";
            throw new EngineUnavailableException("died halfway");
        }
    }

    private sealed class StubEngine : ITextEngine
    {
        private readonly HashSet<ProcessingMode> _modes;

        public StubEngine(string name, params ProcessingMode[] modes)
        {
            Name = name;
            _modes = new HashSet<ProcessingMode>(modes);
        }

        public string Name { get; }

        public bool Available { get; init; } = true;

        public string[] Chunks { get; init; } = ["output"];

        public int Calls { get; private set; }

        public bool SupportsMode(ProcessingMode mode) => _modes.Contains(mode);

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(Available);

        public async IAsyncEnumerable<string> ProcessAsync(
            string input,
            ProcessingMode mode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            Calls++;
            foreach (var chunk in Chunks)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }
}
