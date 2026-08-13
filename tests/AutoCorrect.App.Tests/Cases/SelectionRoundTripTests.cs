using System.Windows;
using System.Windows.Controls;
using AutoCorrect.App.Capture;
using AutoCorrect.App.Ui;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Tests;

namespace AutoCorrect.App.Tests.Cases;

/// <summary>
/// The end to end path that could never be checked without Windows: read the selection out of a
/// focused text box and write a result back into it.
///
/// A WPF window plays the role of the target application. It exposes TextPattern through UI
/// Automation exactly like Notepad or a browser text field does.
/// </summary>
public static class SelectionRoundTripTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Capture: reads the selection of the focused text box via UI Automation", async () =>
        {
            if (!DesktopProbe.IsInteractive)
            {
                Console.WriteLine("      (skipped: no interactive desktop)");
                return;
            }

            using var target = new TargetWindow("Ich habe gestern ein Buch gelest.");
            await target.FocusAndSelectAllAsync();

            var capture = new SelectionCapture();
            var settings = new AppSettings { PreferUiAutomation = true };

            var result = await capture.CaptureAsync(settings, CancellationToken.None);

            Assert.True(result.HasText, "no selection was read");
            Assert.Equal("Ich habe gestern ein Buch gelest.", result.Text!.Trim());
            Assert.Equal(SelectionSource.UiAutomation, result.Source);
        });

        runner.Add("Capture: the clipboard path reads the selection as well", async () =>
        {
            if (!DesktopProbe.IsInteractive)
            {
                Console.WriteLine("      (skipped: no interactive desktop)");
                return;
            }

            const string userClipboard = "Inhalt des Benutzers";
            await ClipboardSnapshot.SetTextAsync(userClipboard);

            using var target = new TargetWindow("Der Hund lauft schnell.");
            await target.FocusAndSelectAllAsync();

            var capture = new SelectionCapture();

            // UI Automation is switched off, so the simulated Ctrl+C has to do the work.
            var settings = new AppSettings { PreferUiAutomation = false, ClipboardWaitMilliseconds = 150 };
            var result = await capture.CaptureAsync(settings, CancellationToken.None);

            Assert.True(result.HasText, "the clipboard path read nothing");
            Assert.Equal("Der Hund lauft schnell.", result.Text!.Trim());
            Assert.Equal(SelectionSource.Clipboard, result.Source);

            // And the previous clipboard content must be back.
            Assert.Equal(userClipboard, await ClipboardSnapshot.ReadTextAsync());
        });

        runner.Add("Capture: an empty selection is reported as empty", async () =>
        {
            if (!DesktopProbe.IsInteractive)
            {
                Console.WriteLine("      (skipped: no interactive desktop)");
                return;
            }

            using var target = new TargetWindow(string.Empty);
            await target.FocusAsync();

            var capture = new SelectionCapture();
            var result = await capture.CaptureAsync(
                new AppSettings { ClipboardWaitMilliseconds = 150 },
                CancellationToken.None);

            Assert.False(result.HasText, $"unexpected text: \"{result.Text}\"");
        });

        runner.Add("Insert: the result replaces the selection in the target window", async () =>
        {
            if (!DesktopProbe.IsInteractive)
            {
                Console.WriteLine("      (skipped: no interactive desktop)");
                return;
            }

            const string userClipboard = "vorher in der Zwischenablage";
            await ClipboardSnapshot.SetTextAsync(userClipboard);

            using var target = new TargetWindow("Ich habe gestern ein Buch gelest.");
            await target.FocusAndSelectAllAsync();

            var injector = new TextInjector();
            await injector.InsertAsync("Ich habe gestern ein Buch gelesen.", target.Handle);

            await PopupWindowTests.WaitUntil(
                () => target.Text.Contains("gelesen", StringComparison.Ordinal),
                5000);

            Assert.Equal("Ich habe gestern ein Buch gelesen.", target.Text);

            // The clipboard has to be back to what the user had.
            await PopupWindowTests.WaitUntil(
                () => string.Equals(Clipboard.GetText(), userClipboard, StringComparison.Ordinal),
                3000);
        });

        runner.Add("Placement: the popup lands inside the visible work area", () =>
        {
            using var window = new PlacementProbeWindow();

            ScreenPlacement.PositionAtCursor(window);
            window.Show();
            ScreenPlacement.PositionAtCursor(window);

            var rect = window.GetPhysicalRect();
            var work = PlacementProbeWindow.GetWorkAreaAtCursor();

            Assert.True(rect.Left >= work.Left, $"window sticks out on the left: {rect.Left} < {work.Left}");
            Assert.True(rect.Top >= work.Top, $"window sticks out at the top: {rect.Top} < {work.Top}");
            Assert.True(rect.Right <= work.Right, $"window sticks out on the right: {rect.Right} > {work.Right}");
            Assert.True(rect.Bottom <= work.Bottom, $"window sticks out at the bottom: {rect.Bottom} > {work.Bottom}");

            window.Close();
        });
    }
}

/// <summary>Stands in for the application the user has focused.</summary>
internal sealed class TargetWindow : IDisposable
{
    private readonly Window _window;
    private readonly TextBox _textBox;

    public TargetWindow(string text)
    {
        _textBox = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            FontSize = 14,
        };

        _window = new Window
        {
            Title = "AutoCorrect Testziel",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = _textBox,
            ShowInTaskbar = false,
            Topmost = true,
        };

        _window.Show();
    }

    public string Text => _textBox.Text;

    public IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(_window).Handle;

    public async Task FocusAsync()
    {
        _window.Activate();
        _textBox.Focus();
        System.Windows.Input.Keyboard.Focus(_textBox);

        // Give Windows a moment to actually move the focus.
        await Task.Delay(250);
    }

    public async Task FocusAndSelectAllAsync()
    {
        await FocusAsync();
        _textBox.SelectAll();
        await Task.Delay(100);
    }

    public void Dispose() => _window.Close();
}

/// <summary>Minimal window used to verify the cursor placement maths.</summary>
internal sealed class PlacementProbeWindow : Window, IDisposable
{
    public PlacementProbeWindow()
    {
        Width = 400;
        Height = 300;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    public (int Left, int Top, int Right, int Bottom) GetPhysicalRect()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        NativeProbe.GetWindowRect(handle, out var rect);
        return (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    public static (int Left, int Top, int Right, int Bottom) GetWorkAreaAtCursor()
    {
        NativeProbe.GetCursorPos(out var point);
        var monitor = NativeProbe.MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);

        var info = new NativeProbe.MONITORINFO
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeProbe.MONITORINFO>(),
        };

        NativeProbe.GetMonitorInfo(monitor, ref info);
        return (info.WorkArea.Left, info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom);
    }

    void IDisposable.Dispose() => Close();
}

/// <summary>The few native calls the tests need themselves.</summary>
internal static class NativeProbe
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int CbSize;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO monitorInfo);
}

/// <summary>Placement is exercised through the probe window above.</summary>
public static class ScreenPlacementTests
{
    public static void Register(TestRunner runner)
    {
        // The real assertions live in SelectionRoundTripTests so the window handling stays in
        // one place; this keeps the registration list readable.
        _ = runner;
    }
}
