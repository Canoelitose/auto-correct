using System.Windows;
using System.Windows.Interop;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Ui;

/// <summary>
/// Places a window next to the mouse cursor, correcting for screen edges, multiple monitors and
/// DPI scaling.
///
/// The cursor position comes back in physical pixels while WPF works in device independent units.
/// Positioning therefore happens through SetWindowPos with physical coordinates instead of
/// Window.Left / Window.Top, which avoids the conversion mismatch on mixed DPI setups.
/// </summary>
internal static class ScreenPlacement
{
    private const int CursorOffsetX = 16;
    private const int CursorOffsetY = 20;
    private const int EdgeMargin = 8;

    public static void PositionAtCursor(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            if (handle == IntPtr.Zero || !NativeMethods.GetCursorPos(out var cursor))
            {
                return;
            }

            var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var workArea = GetWorkArea(monitor);
            var scale = GetScale(monitor);

            var size = GetWindowSize(handle, window, scale);
            var offsetX = (int)Math.Round(CursorOffsetX * scale);
            var offsetY = (int)Math.Round(CursorOffsetY * scale);
            var margin = (int)Math.Round(EdgeMargin * scale);

            var x = cursor.X + offsetX;
            var y = cursor.Y + offsetY;

            // Not enough room to the right or below: flip to the other side of the cursor.
            if (x + size.Width > workArea.Right - margin)
            {
                x = cursor.X - size.Width - offsetX;
            }

            if (y + size.Height > workArea.Bottom - margin)
            {
                y = cursor.Y - size.Height - offsetY;
            }

            x = Clamp(x, workArea.Left + margin, workArea.Right - margin - size.Width);
            y = Clamp(y, workArea.Top + margin, workArea.Bottom - margin - size.Height);

            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("Window could not be positioned at the cursor.", ex);
        }
    }

    private static (int Width, int Height) GetWindowSize(IntPtr handle, Window window, double scale)
    {
        // Before the first layout pass the window has no measured size yet, so the configured
        // size in DIPs multiplied by the monitor scale is used.
        if (NativeMethods.GetWindowRect(handle, out var rect) && rect.Width > 0 && rect.Height > 0)
        {
            return (rect.Width, rect.Height);
        }

        var width = double.IsNaN(window.Width) ? 560 : window.Width;
        var height = double.IsNaN(window.Height) ? 380 : window.Height;
        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    private static NativeMethods.RECT GetWorkArea(IntPtr monitor)
    {
        var info = new NativeMethods.MONITORINFO
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return info.WorkArea;
        }

        // Fallback: primary screen in physical pixels is unknown here, use a generous rectangle.
        return new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    /// <summary>Scale factor of the monitor, 1.0 at 96 DPI.</summary>
    private static double GetScale(IntPtr monitor)
    {
        try
        {
            if (monitor != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Shcore.dll exists from Windows 8.1 onwards; on older systems 96 DPI is assumed.
            Log.Debug("GetDpiForMonitor is not available: " + ex.GetType().Name);
        }

        return 1.0;
    }

    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);
}
