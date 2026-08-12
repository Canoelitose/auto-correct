using AutoCorrect.App.Interop;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Tray;

/// <summary>
/// Loads the application icon as an HICON without taking a dependency on System.Drawing.
/// </summary>
internal static class IconLoader
{
    /// <param name="ownsHandle">True when the caller must call DestroyIcon.</param>
    public static IntPtr LoadApplicationIcon(out bool ownsHandle)
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON);
        if (width <= 0 || height <= 0)
        {
            width = 16;
            height = 16;
        }

        // 1. Icon resource embedded by <ApplicationIcon> in the executable.
        try
        {
            var module = NativeMethods.GetModuleHandle(null);
            var handle = NativeMethods.LoadImage(
                module,
                new IntPtr(NativeMethods.APPLICATION_ICON_RESOURCE_ID),
                NativeMethods.IMAGE_ICON,
                width,
                height,
                0);

            if (handle != IntPtr.Zero)
            {
                ownsHandle = true;
                return handle;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("LoadImage is not available.", ex);
        }

        // 2. First icon of the executable file on disk.
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) &&
                NativeMethods.ExtractIconEx(path, 0, out var large, out var small, 1) > 0)
            {
                if (small != IntPtr.Zero)
                {
                    if (large != IntPtr.Zero)
                    {
                        NativeMethods.DestroyIcon(large);
                    }

                    ownsHandle = true;
                    return small;
                }

                if (large != IntPtr.Zero)
                {
                    ownsHandle = true;
                    return large;
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warn("ExtractIconEx is not available.", ex);
        }

        // 3. Stock application icon, shared and therefore not destroyed by us.
        Log.Warn("Falling back to the stock application icon for the tray.");
        ownsHandle = false;
        return NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(NativeMethods.APPLICATION_ICON_RESOURCE_ID));
    }
}
