using System.Runtime.InteropServices;
using System.Windows.Threading;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Hotkeys;

/// <summary>
/// Catches key combinations that RegisterHotKey cannot claim because Windows uses them itself.
/// Win+Space is the relevant case: the shell owns it for the keyboard layout switcher.
///
/// A WH_KEYBOARD_LL hook sits in front of the shell, so the combination can be intercepted and
/// swallowed. Two rules matter:
///
///   * The callback must return within a few hundred milliseconds or Windows silently removes
///     the hook. The work is therefore only queued on the dispatcher, never done here.
///   * Swallowing the second key would let Windows see a lone Win press and open the start menu
///     on release, so a harmless key is injected to break that.
/// </summary>
internal sealed class LowLevelHotkeyHook : IDisposable
{
    private readonly List<(HotkeyDefinition Hotkey, Action Callback)> _hotkeys = new();
    private readonly Dispatcher _dispatcher;

    // The delegate has to be kept alive: Windows holds a raw function pointer to it.
    private readonly NativeMethods.LowLevelKeyboardProc _callback;

    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    public LowLevelHotkeyHook(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _callback = HookCallback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Registers a combination. Returns false when the hook could not be installed.</summary>
    public bool Add(HotkeyDefinition hotkey, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!EnsureInstalled())
        {
            return false;
        }

        _hotkeys.Add((hotkey, callback));
        Log.Info($"Hotkey handled by the keyboard hook: {hotkey}");
        return true;
    }

    public void RemoveAll() => _hotkeys.Clear();

    private bool EnsureInstalled()
    {
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        // A low level hook is global and needs no module handle of its own in managed code;
        // passing the module of the current process is what Windows expects here.
        var module = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, module, 0);

        if (_hook == IntPtr.Zero)
        {
            Log.Warn($"The keyboard hook could not be installed (Win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        return true;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _hotkeys.Count == 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = (int)wParam;
        if (message is not (NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN))
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

        // Ignore what we injected ourselves, otherwise the simulated Ctrl+C and Ctrl+V would
        // feed back into this hook.
        if ((data.Flags & NativeMethods.LLKHF_INJECTED) != 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var pressed = CurrentModifiers();

        foreach (var (hotkey, callback) in _hotkeys)
        {
            if (hotkey.VirtualKey != data.VkCode || hotkey.Modifiers != pressed)
            {
                continue;
            }

            // Queue and return immediately; the hook must not run any real work.
            _dispatcher.BeginInvoke(DispatcherPriority.Normal, callback);

            if (pressed.HasFlag(HotkeyModifiers.Windows))
            {
                InputSimulator.SendStartMenuBlocker();
            }

            // 1 means "handled": the key never reaches the shell or the target application.
            return new IntPtr(1);
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (IsDown(NativeMethods.VK_CONTROL))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(NativeMethods.VK_MENU))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(NativeMethods.VK_SHIFT))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(ushort virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeys.Clear();

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
