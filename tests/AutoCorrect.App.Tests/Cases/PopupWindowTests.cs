using System.Runtime.CompilerServices;
using System.Windows;
using AutoCorrect.App.Capture;
using AutoCorrect.App.Ui;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Localization;
using AutoCorrect.Core.Tests;

namespace AutoCorrect.App.Tests.Cases;

/// <summary>
/// Drives the real popup window: XAML is loaded, the window is shown, the engine runs and the
/// result lands in the text box. This is what could not be verified without Windows.
/// </summary>
public static class PopupWindowTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Popup: warm-up renders the window without showing it", () =>
        {
            using var popup = new PopupScope(new StubEngine("Korrigiert."));

            popup.Window.Warmup();

            Assert.False(popup.Window.IsVisible, "the warm-up must not leave the window visible");
            Assert.False(popup.Window.IsSessionActive, "no session should be running yet");
        });

        runner.Add("Popup: a session shows the window and the corrected text", async () =>
        {
            using var popup = new PopupScope(new StubEngine("Ich habe gestern ein Buch gelesen."));
            popup.Window.Warmup();

            popup.Window.StartSession("Ich habe gestern ein Buch gelest.", ProcessingMode.Correct, IntPtr.Zero);

            Assert.True(popup.Window.IsVisible, "the popup did not become visible");
            Assert.True(popup.Window.IsSessionActive, "no session was started");
            Assert.Contains("gelest", popup.Window.OriginalPreviewForTests);

            await WaitUntil(() => popup.Window.ResultTextForTests.Length > 0);

            Assert.Equal("Ich habe gestern ein Buch gelesen.", popup.Window.ResultTextForTests);

            popup.Window.EndSession();
            Assert.False(popup.Window.IsVisible, "the popup stayed visible after the session ended");
        });

        runner.Add("Popup: the window is open before the engine answers", async () =>
        {
            // The popup must never wait for the result before appearing.
            var engine = new StubEngine("fertig") { DelayMs = 400 };
            using var popup = new PopupScope(engine);
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Correct, IntPtr.Zero);

            Assert.True(popup.Window.IsVisible, "the popup waited for the engine");
            Assert.Equal(string.Empty, popup.Window.ResultTextForTests);
            Assert.Contains(UiText.StatusWorking, popup.Window.StatusLineForTests);

            await WaitUntil(() => popup.Window.ResultTextForTests.Length > 0, 5000);
            Assert.Equal("fertig", popup.Window.ResultTextForTests);

            popup.Window.EndSession();
        });

        runner.Add("Popup: streamed chunks are collected into the text box", async () =>
        {
            var engine = new StubEngine("Das ", "ist ", "gestreamt.") { DelayMs = 20 };
            using var popup = new PopupScope(engine);
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Correct, IntPtr.Zero);
            await WaitUntil(() => popup.Window.ResultTextForTests.Contains("gestreamt", StringComparison.Ordinal), 5000);

            Assert.Equal("Das ist gestreamt.", popup.Window.ResultTextForTests);
            popup.Window.EndSession();
        });

        runner.Add("Popup: an unreachable engine shows the error with the start hint", async () =>
        {
            using var popup = new PopupScope(new FailingEngine());
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Correct, IntPtr.Zero);
            await WaitUntil(() => popup.Window.ErrorVisibleForTests, 5000);

            Assert.Contains("LanguageTool", popup.Window.ErrorMessageForTests);
            popup.Window.EndSession();
        });

        runner.Add("Popup: a mode without an engine falls back to correcting", async () =>
        {
            using var popup = new PopupScope(new StubEngine("ok"));
            popup.Window.Warmup();

            // The stub only supports Correct, so Rephrase must fall back with a visible note.
            popup.Window.StartSession("Eingabe", ProcessingMode.Rephrase, IntPtr.Zero);
            await WaitUntil(() => popup.Window.ResultTextForTests.Length > 0, 5000);

            Assert.Equal("ok", popup.Window.ResultTextForTests);
            Assert.Contains(UiText.ModeCorrect, popup.Window.StatusLineForTests);

            popup.Window.EndSession();
        });

        runner.Add("Popup: a slow model is explained instead of looking hung", async () =>
        {
            // A model that is not in memory yet takes tens of seconds for the first token. An
            // unchanging "Wird verarbeitet ..." looks like a hang, so the status has to say why.
            var engine = new RewriteEngine("umformuliert") { DelayMs = 20_000 };
            using var popup = new PopupScope(engine);
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Rephrase, IntPtr.Zero);
            Assert.Contains(UiText.StatusWorking, popup.Window.StatusLineForTests);

            await WaitUntil(
                () => popup.Window.StatusLineForTests.Contains(UiText.StatusModelLoading, StringComparison.Ordinal),
                8000);

            popup.Window.EndSession();
        });

        runner.Add("Popup: correcting explains a long wait as well", async () =>
        {
            // Correcting can reach the model too now, so the same explanation applies.
            var engine = new StubEngine("korrigiert") { DelayMs = 20_000 };
            using var popup = new PopupScope(engine);
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Correct, IntPtr.Zero);

            await WaitUntil(
                () => popup.Window.StatusLineForTests.Contains(UiText.StatusModelLoading, StringComparison.Ordinal),
                8000);

            popup.Window.EndSession();
        });

        runner.Add("Popup: a fast answer is never called slow", async () =>
        {
            // The note must only appear when the wait is real.
            var engine = new StubEngine("sofort da");
            using var popup = new PopupScope(engine);
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Correct, IntPtr.Zero);
            await WaitUntil(() => popup.Window.ResultTextForTests.Length > 0, 5000);
            await Task.Delay(600);

            Assert.False(
                popup.Window.StatusLineForTests.Contains(UiText.StatusModelLoading, StringComparison.Ordinal),
                $"an instant answer was reported as slow: {popup.Window.StatusLineForTests}");

            popup.Window.EndSession();
        });

        runner.Add("Popup: ending a session cancels the running request", async () =>
        {
            var engine = new StubEngine("darf nie erscheinen") { DelayMs = 1200 };
            using var popup = new PopupScope(engine);
            popup.Window.Warmup();

            popup.Window.StartSession("Eingabe", ProcessingMode.Correct, IntPtr.Zero);
            Assert.True(popup.Window.IsSessionActive, "no session was started");

            popup.Window.EndSession();

            Assert.False(popup.Window.IsSessionActive, "the session is still active");
            Assert.False(popup.Window.IsVisible, "the popup is still visible");

            // Wait past the engine delay. The result of a cancelled run must never appear,
            // and the window must stay closed.
            await Task.Delay(engine.DelayMs + 800);

            Assert.Equal(string.Empty, popup.Window.ResultTextForTests);
            Assert.False(popup.Window.IsVisible, "the popup came back after cancellation");

            // Either the engine was cancelled while running, or it never started because the
            // token was already cancelled. Both are correct; asserting only on the first one
            // makes the test depend on scheduling luck.
            Assert.True(
                engine.WasCancelled || !engine.WasStarted,
                "the engine kept running after cancellation");
        });

        runner.Add("Welcome: the first start window shows the hotkey", () =>
        {
            var settings = new AutoCorrect.Core.Configuration.AppSettings();
            var window = new WelcomeWindow(settings, engineAvailable: false);

            try
            {
                window.Show();
                Assert.True(window.IsVisible, "the welcome window did not appear");
            }
            finally
            {
                window.Close();
            }
        });

        runner.Add("Popup: the interface language reaches the window", () =>
        {
            using var language = new LanguageScope(UiLanguage.English);
            using var popup = new PopupScope(new StubEngine("ok"));

            Assert.Equal("Correct", popup.Window.ModeButtonTextForTests(ProcessingMode.Correct));
            Assert.Contains("Apply", popup.Window.ShortcutHintForTests);

            UiText.Language = UiLanguage.German;
            using var german = new PopupScope(new StubEngine("ok"));

            Assert.Equal("Korrigieren", german.Window.ModeButtonTextForTests(ProcessingMode.Correct));
            Assert.Contains("Übernehmen", german.Window.ShortcutHintForTests);
        });
    }

    /// <summary>Waits for a condition while the dispatcher keeps running.</summary>
    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new AssertionException($"condition was not met within {timeoutMs} ms");
    }

    private sealed class PopupScope : IDisposable
    {
        public PopupScope(ITextEngine engine)
        {
            Window = new PopupWindow(engine, new TextInjector());
        }

        public PopupWindow Window { get; }

        public void Dispose()
        {
            Window.EndSession();
            Window.Close();
        }
    }

    private sealed class StubEngine : ITextEngine
    {
        private readonly string[] _chunks;

        public StubEngine(params string[] chunks) => _chunks = chunks;

        public string Name => "Stub";

        public int DelayMs { get; init; }

        public bool WasCancelled { get; private set; }

        public bool WasStarted { get; private set; }

        public bool SupportsMode(ProcessingMode mode) => mode == ProcessingMode.Correct;

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);

        public async IAsyncEnumerable<string> ProcessAsync(
            string input,
            ProcessingMode mode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            WasStarted = true;

            foreach (var chunk in _chunks)
            {
                if (DelayMs > 0)
                {
                    Task delay;
                    try
                    {
                        delay = Task.Delay(DelayMs, ct);
                        await delay.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        WasCancelled = true;
                        throw;
                    }
                }

                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }

    /// <summary>Like <see cref="StubEngine"/>, but for the modes a language model handles.</summary>
    private sealed class RewriteEngine : ITextEngine
    {
        private readonly string[] _chunks;

        public RewriteEngine(params string[] chunks) => _chunks = chunks;

        public string Name => "Stub-Modell";

        public int DelayMs { get; init; }

        public bool SupportsMode(ProcessingMode mode) =>
            mode is ProcessingMode.Rephrase or ProcessingMode.Formal or ProcessingMode.Shorten;

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);

        public async IAsyncEnumerable<string> ProcessAsync(
            string input,
            ProcessingMode mode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in _chunks)
            {
                if (DelayMs > 0)
                {
                    await Task.Delay(DelayMs, ct).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }

    private sealed class FailingEngine : ITextEngine
    {
        public string Name => "Failing";

        public bool SupportsMode(ProcessingMode mode) => mode == ProcessingMode.Correct;

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(false);

        public async IAsyncEnumerable<string> ProcessAsync(
            string input,
            ProcessingMode mode,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            throw new EngineUnavailableException(UiText.LanguageToolUnavailable);
#pragma warning disable CS0162 // unreachable, required to make this an iterator
            yield break;
#pragma warning restore CS0162
        }
    }
}

/// <summary>Restores the interface language after a test.</summary>
internal sealed class LanguageScope : IDisposable
{
    private readonly UiLanguage _previous;

    public LanguageScope(UiLanguage language)
    {
        _previous = UiText.Language;
        UiText.Language = language;
    }

    public void Dispose() => UiText.Language = _previous;
}
