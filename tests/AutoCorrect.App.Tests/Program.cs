using System.Windows;
using AutoCorrect.App.Tests.Cases;
using AutoCorrect.Core.Tests;

namespace AutoCorrect.App.Tests;

/// <summary>
/// Runs the Windows specific tests. Everything here needs a real Windows session:
/// clipboard, hotkey registration, tray icon, UI Automation and window placement.
///
/// The process is STA and owns a WPF Application, because that is the environment the
/// production code runs in.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Attach to the parent console when started from one, so the output is visible in CI.
        ConsoleAttach.TryAttach();

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;

        application.Startup += (_, _) => _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                var runner = new TestRunner();

                EnvironmentTests.Register(runner);
                AutoStartTests.Register(runner);
                MessageWindowTests.Register(runner);
                HotkeyTests.Register(runner);
                TrayIconTests.Register(runner);
                ClipboardTests.Register(runner);
                ScreenPlacementTests.Register(runner);
                PopupWindowTests.Register(runner);
                SelectionRoundTripTests.Register(runner);

                Console.WriteLine("AutoCorrect.App tests (Windows only)");
                Console.WriteLine($"Interactive desktop: {DesktopProbe.IsInteractive}");
                Console.WriteLine();

                exitCode = await runner.RunAsync(args.Length > 0 ? args[0] : null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Test host failed: " + ex);
                exitCode = 1;
            }
            finally
            {
                application.Shutdown();
            }
        }

        application.Run();
        return exitCode;
    }
}
