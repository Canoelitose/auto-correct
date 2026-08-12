using AutoCorrect.App.Interop;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Capture;

/// <summary>
/// Writes the result back into the application the text came from.
/// </summary>
public sealed class TextInjector
{
    /// <summary>Time the target window needs to become foreground again before Ctrl+V arrives.</summary>
    private const int FocusSettleMs = 60;

    /// <summary>Delay before the previous clipboard content is restored.</summary>
    private const int ClipboardRestoreDelayMs = 500;

    /// <summary>Must be called on the STA UI thread.</summary>
    public async Task InsertAsync(string text, IntPtr targetWindow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var snapshot = await ClipboardSnapshot.CaptureAsync().ConfigureAwait(true);

        if (!await ClipboardSnapshot.SetTextAsync(text).ConfigureAwait(true))
        {
            Log.Warn("Result could not be placed on the clipboard, insertion aborted.");
            return;
        }

        BringToForeground(targetWindow);
        await Task.Delay(FocusSettleMs, ct).ConfigureAwait(true);

        InputSimulator.ReleaseHeldModifiers();
        InputSimulator.SendCtrlKey(NativeMethods.VK_V);

        // Restoring too early would take the text away before the target has read it.
        await Task.Delay(ClipboardRestoreDelayMs, ct).ConfigureAwait(true);
        await snapshot.RestoreAsync().ConfigureAwait(true);
    }

    /// <summary>Copies the result without inserting it; the clipboard keeps the new content.</summary>
    public static Task<bool> CopyAsync(string text) => ClipboardSnapshot.SetTextAsync(text);

    /// <summary>
    /// Brings the original window back to the front. AttachThreadInput is needed because
    /// SetForegroundWindow alone is refused once another thread owns the foreground.
    /// </summary>
    public static void BringToForeground(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(targetWindow))
        {
            return;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == targetWindow)
        {
            return;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero
            ? currentThread
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);

        if (foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true))
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            NativeMethods.BringWindowToTop(targetWindow);
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
        }
        else
        {
            NativeMethods.SetForegroundWindow(targetWindow);
        }
    }
}
