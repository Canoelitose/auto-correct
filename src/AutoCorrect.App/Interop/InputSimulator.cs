using System.Runtime.InteropServices;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Interop;

/// <summary>
/// Keyboard simulation through SendInput.
/// </summary>
internal static class InputSimulator
{
    /// <summary>
    /// Sends key up for every modifier the user still holds down.
    ///
    /// The hotkey is Ctrl+Alt+Space, so at the moment it fires Alt is usually still pressed.
    /// Without this the simulated Ctrl+C would arrive as Ctrl+Alt+C in the target application.
    /// </summary>
    public static void ReleaseHeldModifiers()
    {
        var inputs = new List<NativeMethods.INPUT>(8);

        AddIfDown(inputs, NativeMethods.VK_MENU, NativeMethods.VK_LMENU, NativeMethods.VK_RMENU);
        AddIfDown(inputs, NativeMethods.VK_CONTROL, NativeMethods.VK_LCONTROL, NativeMethods.VK_RCONTROL);
        AddIfDown(inputs, NativeMethods.VK_SHIFT, NativeMethods.VK_LSHIFT, NativeMethods.VK_RSHIFT);

        if (IsDown(NativeMethods.VK_LWIN))
        {
            inputs.Add(KeyUp(NativeMethods.VK_LWIN));
        }

        if (IsDown(NativeMethods.VK_RWIN))
        {
            inputs.Add(KeyUp(NativeMethods.VK_RWIN));
        }

        if (inputs.Count > 0)
        {
            Send(inputs.ToArray());
        }
    }

    /// <summary>
    /// Sends a harmless key so Windows does not treat a held Windows key as a lone tap.
    ///
    /// When the keyboard hook swallows the second key of Win+Space, the shell never sees it and
    /// would open the start menu once the Windows key is released. A Control tap in between
    /// prevents that.
    /// </summary>
    public static void SendStartMenuBlocker()
    {
        Send(new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyUp(NativeMethods.VK_CONTROL),
        });
    }

    /// <summary>Sends Ctrl + <paramref name="virtualKey"/> as a complete key sequence.</summary>
    public static void SendCtrlKey(ushort virtualKey)
    {
        Send(new[]
        {
            KeyDown(NativeMethods.VK_CONTROL),
            KeyDown(virtualKey),
            KeyUp(virtualKey),
            KeyUp(NativeMethods.VK_CONTROL),
        });
    }

    private static void AddIfDown(List<NativeMethods.INPUT> inputs, ushort generic, ushort left, ushort right)
    {
        if (!IsDown(generic))
        {
            return;
        }

        // Release both sides: which one the user pressed cannot be told from the generic key.
        inputs.Add(KeyUp(left));
        inputs.Add(KeyUp(right));
        inputs.Add(KeyUp(generic));
    }

    private static bool IsDown(ushort virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static NativeMethods.INPUT KeyDown(ushort virtualKey) => Create(virtualKey, 0);

    private static NativeMethods.INPUT KeyUp(ushort virtualKey) => Create(virtualKey, NativeMethods.KEYEVENTF_KEYUP);

    private static NativeMethods.INPUT Create(ushort virtualKey, uint flags) => new()
    {
        Type = NativeMethods.INPUT_KEYBOARD,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KEYBDINPUT
            {
                Vk = virtualKey,
                Scan = 0,
                Flags = flags,
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            // Happens when the foreground window belongs to a process with higher integrity level (UIPI).
            Log.Warn($"SendInput delivered {sent} of {inputs.Length} events (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }
}
