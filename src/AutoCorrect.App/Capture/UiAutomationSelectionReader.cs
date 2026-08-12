using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Capture;

/// <summary>
/// Reads the selection through UI Automation. This is the clean path: it has no side effects
/// on the clipboard and needs no simulated keystrokes.
///
/// Not every application implements TextPattern (many Win32 and Electron controls do not),
/// so the caller falls back to the clipboard when this returns null.
/// </summary>
internal static class UiAutomationSelectionReader
{
    /// <summary>
    /// Must be called from a background thread: UI Automation calls cross process and can block.
    /// Returns null when the focused element does not expose a text selection.
    /// </summary>
    public static string? TryReadSelection()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return null;
            }

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern textPattern)
            {
                return null;
            }

            var ranges = textPattern.GetSelection();
            if (ranges is not { Length: > 0 })
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var range in ranges)
            {
                // -1 means "no length limit".
                builder.Append(range.GetText(-1));
            }

            var text = builder.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex) when (
            ex is ElementNotAvailableException
                or ElementNotEnabledException
                or InvalidOperationException
                or NotSupportedException
                or ArgumentException
                or COMException
                or TimeoutException
                or UnauthorizedAccessException)
        {
            // A window running elevated is invisible to a normal process (UIPI). This is expected
            // and documented, the clipboard fallback takes over.
            Log.Debug($"UI Automation could not read the selection: {ex.GetType().Name}");
            return null;
        }
    }
}
