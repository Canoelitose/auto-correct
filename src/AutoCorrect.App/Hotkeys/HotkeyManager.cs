using System.Runtime.InteropServices;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Hotkeys;

/// <summary>How a hotkey ended up being delivered.</summary>
public enum HotkeyMethod
{
    None,

    /// <summary>The normal path: RegisterHotKey.</summary>
    SystemHotkey,

    /// <summary>Fallback for combinations Windows claims for itself, such as Win+Space.</summary>
    KeyboardHook,
}

/// <summary>Result of a registration attempt. A conflict is reported, never swallowed.</summary>
public sealed record HotkeyRegistrationResult(
    HotkeyDefinition Hotkey,
    bool Success,
    string? Error,
    HotkeyMethod Method = HotkeyMethod.None);

/// <summary>
/// Registers global hotkeys through RegisterHotKey and dispatches WM_HOTKEY to callbacks.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly LowLevelHotkeyHook _hook;
    private int _nextId = 1;
    private bool _disposed;

    public HotkeyManager(MessageWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.AddHandler(HandleMessage);
        _hook = new LowLevelHotkeyHook(System.Windows.Threading.Dispatcher.CurrentDispatcher);
    }

    private bool HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_HOTKEY)
        {
            return false;
        }

        if (_callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            callback();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers a hotkey. Returns a result with a German error message on failure,
    /// which the caller shows to the user.
    /// </summary>
    public HotkeyRegistrationResult Register(HotkeyDefinition hotkey, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var validationError = hotkey.Validate();
        if (validationError is not null)
        {
            return new HotkeyRegistrationResult(hotkey, false, validationError);
        }

        // Combinations Windows owns itself cannot be claimed through RegisterHotKey, so they go
        // straight to the keyboard hook instead of failing first.
        if (hotkey.IsReservedByWindows)
        {
            return RegisterThroughHook(hotkey, callback);
        }

        var id = _nextId++;

        // MOD_NOREPEAT stops the hotkey from firing repeatedly while the keys stay pressed.
        var modifiers = (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT;

        if (!NativeMethods.RegisterHotKey(_window.Handle, id, modifiers, hotkey.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            Log.Warn($"RegisterHotKey failed for id {id} with Win32 error {error}.");

            // Another application may simply hold the combination; the hook can still deliver it.
            var viaHook = RegisterThroughHook(hotkey, callback);
            if (viaHook.Success)
            {
                return viaHook;
            }

            return new HotkeyRegistrationResult(
                hotkey,
                false,
                Core.Localization.UiText.HotkeyRegistrationFailed(hotkey.ToString()));
        }

        _callbacks[id] = callback;
        Log.Info($"Hotkey registered: {hotkey}");
        return new HotkeyRegistrationResult(hotkey, true, null, HotkeyMethod.SystemHotkey);
    }

    private HotkeyRegistrationResult RegisterThroughHook(HotkeyDefinition hotkey, Action callback)
    {
        if (_hook.Add(hotkey, callback))
        {
            return new HotkeyRegistrationResult(hotkey, true, null, HotkeyMethod.KeyboardHook);
        }

        return new HotkeyRegistrationResult(
            hotkey,
            false,
            Core.Localization.UiText.HotkeyRegistrationFailed(hotkey.ToString()));
    }

    /// <summary>Removes every registration, for example before applying new settings.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }

        _callbacks.Clear();
        _nextId = 1;
        _hook.RemoveAll();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _hook.Dispose();
    }
}
