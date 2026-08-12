using AutoCorrect.App.Interop;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Tray;

public enum TrayNotificationLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Tray icon on top of Shell_NotifyIcon. Implemented directly against the shell API so the
/// application does not have to pull in Windows Forms just for a notification area icon.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int IconId = 1;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _added;
    private bool _disposed;
    private string _tooltip;

    public TrayIcon(MessageWindow window, string tooltip)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _tooltip = Truncate(tooltip, 127);

        // Broadcast sent by Explorer when it restarts; the icon has to be added again.
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _icon = IconLoader.LoadApplicationIcon(out _ownsIcon);
        _window.AddHandler(HandleMessage);
        Add();
    }

    /// <summary>Raised on left click or keyboard selection of the icon.</summary>
    public event EventHandler? Activated;

    /// <summary>Raised on right click; the handler shows the context menu.</summary>
    public event EventHandler? MenuRequested;

    private bool HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _added = false;
            Add();
            return true;
        }

        if (msg != CallbackMessage)
        {
            return false;
        }

        // NOTIFYICON_VERSION_4: the notification event is the low word of lParam.
        var notification = (int)((long)lParam & 0xFFFF);

        switch (notification)
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
            case NativeMethods.NIN_SELECT:
            case NativeMethods.NIN_KEYSELECT:
                Activated?.Invoke(this, EventArgs.Empty);
                return true;

            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_CONTEXTMENU:
                MenuRequested?.Invoke(this, EventArgs.Empty);
                return true;

            default:
                return false;
        }
    }

    private void Add()
    {
        if (_added || _disposed)
        {
            return;
        }

        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            Log.Warn("Tray icon could not be added.");
            return;
        }

        _added = true;

        var version = CreateData(0);
        version.VersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref version);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = Truncate(tooltip, 127);
        if (!_added)
        {
            return;
        }

        var data = CreateData(NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>
    /// Short hint in the notification area, used instead of a popup when there is nothing to show
    /// (empty selection, text too long).
    /// </summary>
    public void ShowNotification(string title, string message, TrayNotificationLevel level = TrayNotificationLevel.Info)
    {
        if (!_added)
        {
            return;
        }

        var data = CreateData(NativeMethods.NIF_INFO);
        data.InfoTitle = Truncate(title, 63);
        data.Info = Truncate(message, 255);
        data.InfoFlags = level switch
        {
            TrayNotificationLevel.Warning => NativeMethods.NIIF_WARNING,
            TrayNotificationLevel.Error => NativeMethods.NIIF_ERROR,
            _ => NativeMethods.NIIF_INFO,
        };

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private NativeMethods.NOTIFYICONDATA CreateData(int flags) => new()
    {
        CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        Wnd = _window.Handle,
        Id = IconId,
        Flags = flags,
        CallbackMessage = CallbackMessage,
        Icon = _icon,
        Tip = _tooltip,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
        _ownsIcon = false;
    }
}
