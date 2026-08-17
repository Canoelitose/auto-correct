using System.Runtime.CompilerServices;
using System.Windows;
using AutoCorrect.App.Ui;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Localization;
using AutoCorrect.Core.Tests;

namespace AutoCorrect.App.Tests.Cases;

/// <summary>
/// Drives the real application window: the XAML loads, text goes in, the engine runs and the
/// result lands in the box. Everything a user does without touching the hotkey.
/// </summary>
public static class MainWindowTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Window: opens with the input focused and nothing running", () =>
        {
            using var scope = new WindowScope(new EchoEngine("egal"));

            scope.Window.ShowAndFocus();

            Assert.True(scope.Window.IsVisible, "the window did not become visible");
            Assert.Equal(string.Empty, scope.Window.ResultTextForTests);
        });

        runner.Add("Window: text goes in, the result comes out", async () =>
        {
            using var scope = new WindowScope(new EchoEngine("Ich habe ein Buch gelesen."));
            scope.Window.ShowAndFocus();

            scope.Window.InputTextForTests = "Ich habe ein Buch gelest.";
            scope.Window.RunForTests(ProcessingMode.Correct);

            await PopupWindowTests.WaitUntil(() => scope.Window.ResultTextForTests.Length > 0, 5000);
            Assert.Equal("Ich habe ein Buch gelesen.", scope.Window.ResultTextForTests);
        });

        runner.Add("Window: the streamed answer arrives piece by piece", async () =>
        {
            using var scope = new WindowScope(new EchoEngine("Das ", "ist ", "gestreamt.") { DelayMs = 15 });
            scope.Window.ShowAndFocus();

            scope.Window.InputTextForTests = "Eingabe";
            scope.Window.RunForTests(ProcessingMode.Correct);

            await PopupWindowTests.WaitUntil(
                () => scope.Window.ResultTextForTests.Contains("gestreamt", StringComparison.Ordinal),
                5000);

            Assert.Equal("Das ist gestreamt.", scope.Window.ResultTextForTests);
        });

        runner.Add("Window: an empty input starts nothing", () =>
        {
            var engine = new EchoEngine("darf nie laufen");
            using var scope = new WindowScope(engine);
            scope.Window.ShowAndFocus();

            scope.Window.InputTextForTests = "   ";
            scope.Window.RunForTests(ProcessingMode.Correct);

            Assert.False(engine.WasStarted, "the engine ran on an empty input");
            Assert.False(scope.Window.IsModeEnabledForTests(ProcessingMode.Correct), "buttons should be disabled");
        });

        runner.Add("Window: a mode without an engine is not offered", () =>
        {
            using var scope = new WindowScope(new EchoEngine("ok"));
            scope.Window.ShowAndFocus();
            scope.Window.InputTextForTests = "Ein Satz.";

            // The stub only corrects, so the other three have to be greyed out.
            Assert.True(scope.Window.IsModeEnabledForTests(ProcessingMode.Correct));
            Assert.False(scope.Window.IsModeEnabledForTests(ProcessingMode.Rephrase));
        });

        runner.Add("Window: an unavailable engine shows its message", async () =>
        {
            using var scope = new WindowScope(new BrokenEngine());
            scope.Window.ShowAndFocus();

            scope.Window.InputTextForTests = "Eingabe";
            scope.Window.RunForTests(ProcessingMode.Correct);

            await PopupWindowTests.WaitUntil(() => scope.Window.ErrorVisibleForTests, 5000);
            Assert.Contains("LanguageTool", scope.Window.StatusTextForTests + scope.Window.ResultTextForTests +
                                            ErrorOf(scope.Window));
        });

        runner.Add("Window: closing hides it and leaves the application running", () =>
        {
            using var scope = new WindowScope(new EchoEngine("ok"));
            scope.Window.ShowAndFocus();

            scope.Window.Close();

            // Closing must not end the session: the hotkey has to keep working afterwards.
            Assert.False(scope.Window.IsVisible, "the window stayed visible");
            Assert.True(Application.Current is not null, "the application was shut down by a window close");

            scope.Window.ShowAndFocus();
            Assert.True(scope.Window.IsVisible, "the window did not come back");
        });

        runner.Add("Window: the interface language reaches every label", () =>
        {
            using var english = new LanguageScope(UiLanguage.English);
            using var scope = new WindowScope(new EchoEngine("ok"));
            scope.Window.ApplyTexts();

            Assert.Equal("Work on text", scope.Window.TitleTextForTests);

            UiText.Language = UiLanguage.German;
            scope.Window.ApplyTexts();
            Assert.Equal("Text bearbeiten", scope.Window.TitleTextForTests);
        });
    }

    private static string ErrorOf(MainWindow window) => window.ErrorMessageForTests;

    private sealed class WindowScope : IDisposable
    {
        public WindowScope(ITextEngine engine)
        {
            Window = new MainWindow(engine, () => engine.Name, () => { }, () => { });
        }

        public MainWindow Window { get; }

        public void Dispose() => Window.CloseForReal();
    }

    /// <summary>Returns the chunks it was built with, whatever goes in.</summary>
    private sealed class EchoEngine : ITextEngine
    {
        private readonly string[] _chunks;

        public EchoEngine(params string[] chunks) => _chunks = chunks;

        public string Name => "Stub";

        public int DelayMs { get; init; }

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
                    await Task.Delay(DelayMs, ct).ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }

    private sealed class BrokenEngine : ITextEngine
    {
        public string Name => "Broken";

        public bool SupportsMode(ProcessingMode mode) => true;

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
