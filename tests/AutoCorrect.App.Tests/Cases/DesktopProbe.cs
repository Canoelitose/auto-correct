using System.Runtime.InteropServices;

namespace AutoCorrect.App.Tests.Cases;

/// <summary>
/// Detects whether the process has a usable interactive desktop. A build agent running as a
/// service has none, and then hotkeys and simulated input cannot work. Tests that need it are
/// reported as skipped instead of failing for the wrong reason.
/// </summary>
internal static class DesktopProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(
        IntPtr handle,
        int index,
        IntPtr info,
        int length,
        out int lengthNeeded);

    private const int UOI_FLAGS = 1;
    private const int WSF_VISIBLE = 0x0001;

    private static readonly Lazy<bool> Interactive = new(Probe);

    public static bool IsInteractive => Interactive.Value;

    private static bool Probe()
    {
        try
        {
            var station = GetProcessWindowStation();
            if (station == IntPtr.Zero)
            {
                return false;
            }

            // USEROBJECTFLAGS { BOOL fInherit; BOOL fReserved; DWORD dwFlags; }
            var size = sizeof(int) * 3;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetUserObjectInformation(station, UOI_FLAGS, buffer, size, out _))
                {
                    return false;
                }

                var flags = Marshal.ReadInt32(buffer, sizeof(int) * 2);
                return (flags & WSF_VISIBLE) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
