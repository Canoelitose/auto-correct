using System.Windows.Interop;

namespace AutoCorrect.App.Interop;

/// <summary>
/// Invisible top level window that owns the hotkey registrations and receives the tray callbacks.
///
/// It is deliberately not a message-only window (HWND_MESSAGE): those do not receive the
/// broadcast "TaskbarCreated" message, which is needed to restore the tray icon after Explorer
/// restarts.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _handlers = new();
    private bool _disposed;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("AutoCorrect.MessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0, // no WS_VISIBLE, so the window never appears anywhere
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>
    /// Registers a message handler. It returns true when the message was handled.
    /// </summary>
    public void AddHandler(Func<int, IntPtr, IntPtr, bool> handler) => _handlers.Add(handler);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var handler in _handlers)
        {
            if (handler(msg, wParam, lParam))
            {
                handled = true;
                break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
