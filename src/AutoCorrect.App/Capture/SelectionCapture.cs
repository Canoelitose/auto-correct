using System.Diagnostics;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Capture;

public enum SelectionSource
{
    None,
    UiAutomation,
    Clipboard,
}

public sealed record SelectionResult(string? Text, SelectionSource Source)
{
    public static readonly SelectionResult Empty = new(null, SelectionSource.None);

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Grabs the selected text from whatever application currently has focus.
/// Strategy 1 is UI Automation, strategy 2 the clipboard.
/// </summary>
public sealed class SelectionCapture
{
    /// <summary>
    /// Budget for the UI Automation attempt. Applications that answer slowly must not delay the
    /// popup, the clipboard path is used instead.
    /// </summary>
    private const int UiAutomationBudgetMs = 120;

    /// <summary>Must be called on the STA UI thread (the clipboard path requires it).</summary>
    public async Task<SelectionResult> CaptureAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var stopwatch = Stopwatch.StartNew();

        if (settings.PreferUiAutomation)
        {
            var text = await TryUiAutomationAsync(ct).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(text))
            {
                Log.Debug($"Selection read via UI Automation in {stopwatch.ElapsedMilliseconds} ms.");
                return new SelectionResult(text, SelectionSource.UiAutomation);
            }
        }

        var fallback = await TryClipboardAsync(settings, ct).ConfigureAwait(true);
        Log.Debug($"Selection capture finished in {stopwatch.ElapsedMilliseconds} ms via clipboard.");

        return string.IsNullOrWhiteSpace(fallback)
            ? SelectionResult.Empty
            : new SelectionResult(fallback, SelectionSource.Clipboard);
    }

    private static async Task<string?> TryUiAutomationAsync(CancellationToken ct)
    {
        // UI Automation runs cross process; on a thread pool thread it cannot block the UI.
        var read = Task.Run(UiAutomationSelectionReader.TryReadSelection, ct);
        var finished = await Task.WhenAny(read, Task.Delay(UiAutomationBudgetMs, ct)).ConfigureAwait(true);

        if (finished != read)
        {
            Log.Debug("UI Automation exceeded its time budget, falling back to the clipboard.");
            return null;
        }

        try
        {
            return await read.ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn("UI Automation failed unexpectedly.", ex);
            return null;
        }
    }

    private static async Task<string?> TryClipboardAsync(AppSettings settings, CancellationToken ct)
    {
        var snapshot = await ClipboardSnapshot.CaptureAsync().ConfigureAwait(true);

        try
        {
            // Clearing first makes "nothing was copied" distinguishable from "the old content is
            // still there", which a plain comparison with the previous text cannot tell apart.
            await ClipboardSnapshot.ClearAsync().ConfigureAwait(true);

            InputSimulator.ReleaseHeldModifiers();
            InputSimulator.SendCtrlKey(NativeMethods.VK_C);

            // The target application needs time to serve the copy request. Reading immediately
            // returns the previous clipboard content instead of the selection.
            await Task.Delay(settings.ClipboardWaitMilliseconds, ct).ConfigureAwait(true);

            return await ClipboardSnapshot.ReadTextAsync().ConfigureAwait(true);
        }
        finally
        {
            await snapshot.RestoreAsync().ConfigureAwait(true);
        }
    }
}
