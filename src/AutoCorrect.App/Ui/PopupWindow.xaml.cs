using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AutoCorrect.App.Capture;
using AutoCorrect.App.Interop;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App.Ui;

/// <summary>
/// The popup shown at the cursor. It opens immediately in a loading state; processing starts
/// afterwards so the window is visible long before the first result arrives.
/// </summary>
public partial class PopupWindow : Window
{
    /// <summary>
    /// Tokens are collected for this long and written to the text box in one go. Writing every
    /// single token would cause 20+ redraws per second and cost more CPU than the inference.
    /// </summary>
    private const int FlushIntervalMs = 30;

    private const int OriginalPreviewLength = 400;

    private readonly ITextEngine _engine;
    private readonly TextInjector _injector;
    private readonly DispatcherTimer _flushTimer;
    private readonly StringBuilder _pending = new();
    private readonly object _pendingLock = new();
    private readonly Dictionary<ProcessingMode, Button> _modeButtons = new();

    private CancellationTokenSource? _cts;
    private string _original = string.Empty;
    private IntPtr _targetWindow;
    private ProcessingMode _mode = ProcessingMode.Correct;
    private bool _sessionActive;
    private bool _applying;

    public PopupWindow(ITextEngine engine, TextInjector injector)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));

        InitializeComponent();

        _flushTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FlushIntervalMs),
        };
        _flushTimer.Tick += (_, _) => FlushPending();

        _modeButtons[ProcessingMode.Correct] = ModeCorrectButton;
        _modeButtons[ProcessingMode.Rephrase] = ModeRephraseButton;
        _modeButtons[ProcessingMode.Formal] = ModeFormalButton;
        _modeButtons[ProcessingMode.Shorten] = ModeShortenButton;

        foreach (var (mode, button) in _modeButtons)
        {
            var captured = mode;
            button.Content = UiText.ModeLabel(mode);
            button.Click += (_, _) => StartProcessing(captured);
        }

        EngineLabel.Text = _engine.Name;

        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Renders the window once off screen so the first real invocation does not pay for the
    /// initial layout and render pass. Without this the first popup needs noticeably longer.
    /// </summary>
    public void Warmup()
    {
        try
        {
            Opacity = 0;
            ShowActivated = false;

            // Move the window off screen before it becomes visible, otherwise it flashes at the
            // default position for one frame during start-up.
            var handle = new WindowInteropHelper(this).EnsureHandle();
            NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                -32000,
                -32000,
                0,
                0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

            Show();
            UpdateLayout();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warn("Popup warm-up failed.", ex);
        }
        finally
        {
            Hide();
            ShowActivated = true;
            Opacity = 1;
        }
    }

    /// <summary>Opens the popup for a fresh selection.</summary>
    public void StartSession(string original, ProcessingMode requestedMode, IntPtr targetWindow)
    {
        _original = original ?? string.Empty;
        _targetWindow = targetWindow;
        _applying = false;
        _sessionActive = true;

        OriginalText.Text = Shorten(_original, OriginalPreviewLength);
        ResultBox.Clear();
        HideError();
        UpdateModeAvailability();

        var mode = requestedMode;
        string? note = null;
        if (!_engine.SupportsMode(mode))
        {
            // Phase 1 has no LLM engine: fall back instead of showing an error for a mode the
            // user did not explicitly pick.
            note = $"{UiText.ModeLabel(mode)} ist nicht verfügbar – {UiText.ModeCorrect} wird verwendet.";
            mode = ProcessingMode.Correct;
        }

        // Position, show, focus. Everything expensive happens after this point.
        ScreenPlacement.PositionAtCursor(this);
        Show();
        ScreenPlacement.PositionAtCursor(this);
        Activate();
        TextInjector.BringToForeground(new WindowInteropHelper(this).Handle);
        ResultBox.Focus();

        if (note is not null)
        {
            SetStatus(note);
        }

        StartProcessing(mode);
    }

    private void StartProcessing(ProcessingMode mode)
    {
        if (!_sessionActive || !_engine.SupportsMode(mode))
        {
            return;
        }

        _mode = mode;
        UpdateModeAvailability();
        _ = RunAsync(mode);
    }

    private async Task RunAsync(ProcessingMode mode)
    {
        CancelRunning();

        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        lock (_pendingLock)
        {
            _pending.Clear();
        }

        ResultBox.Clear();
        ResultBox.IsReadOnly = true;
        HideError();
        SetBusy(true);
        SetStatus($"{_engine.Name} · {UiText.ModeLabel(mode)} · {UiText.StatusWorking}");

        try
        {
            var input = _original;

            // The enumeration runs off the UI thread; chunks are buffered and flushed by the timer.
            await Task.Run(
                async () =>
                {
                    await foreach (var chunk in _engine.ProcessAsync(input, mode, ct).ConfigureAwait(false))
                    {
                        if (string.IsNullOrEmpty(chunk))
                        {
                            continue;
                        }

                        lock (_pendingLock)
                        {
                            _pending.Append(chunk);
                        }
                    }
                },
                ct).ConfigureAwait(true);

            FlushPending();

            var unchanged = string.Equals(ResultBox.Text, _original, StringComparison.Ordinal);
            SetStatus($"{_engine.Name} · {UiText.ModeLabel(mode)} · " +
                      (unchanged ? UiText.StatusNoChange : UiText.StatusDone));
        }
        catch (OperationCanceledException)
        {
            SetStatus(UiText.StatusCancelled);
        }
        catch (EngineUnavailableException ex)
        {
            ShowError(ex.Message);
        }
        catch (NotSupportedException ex)
        {
            Log.Warn("Engine rejected the requested mode.", ex);
            ShowError(UiText.ModeNotSupported);
        }
        catch (Exception ex)
        {
            Log.Error("Processing failed.", ex);
            ShowError(UiText.UnexpectedError);
        }
        finally
        {
            SetBusy(false);
            ResultBox.IsReadOnly = false;

            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }

            cts.Dispose();
        }
    }

    private void FlushPending()
    {
        string chunk;
        lock (_pendingLock)
        {
            if (_pending.Length == 0)
            {
                return;
            }

            chunk = _pending.ToString();
            _pending.Clear();
        }

        ResultBox.AppendText(chunk);
        ResultBox.CaretIndex = ResultBox.Text.Length;
        ResultBox.ScrollToEnd();
    }

    private void CancelRunning()
    {
        var cts = _cts;
        _cts = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                EndSession();
                break;

            case Key.Enter when (Keyboard.Modifiers & ModifierKeys.Shift) == 0:
                // Shift+Enter keeps its normal behaviour and inserts a line break.
                e.Handled = true;
                _ = ApplyAsync();
                break;

            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                // A manual selection keeps the standard copy behaviour; otherwise the whole
                // result is copied and the popup closes.
                if (ResultBox.SelectionLength == 0)
                {
                    e.Handled = true;
                    _ = CopyAsync();
                }

                break;
        }
    }

    private async Task ApplyAsync()
    {
        if (_applying || !_sessionActive)
        {
            return;
        }

        var text = ResultBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            EndSession();
            return;
        }

        _applying = true;
        CancelRunning();
        _sessionActive = false;
        _flushTimer.Stop();
        Hide();

        try
        {
            await _injector.InsertAsync(text, _targetWindow).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error("Result could not be inserted.", ex);
        }
        finally
        {
            _applying = false;
        }
    }

    private async Task CopyAsync()
    {
        var text = ResultBox.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _applying = true;
        try
        {
            await TextInjector.CopyAsync(text).ConfigureAwait(true);
            SetStatus(UiText.StatusCopied);
        }
        finally
        {
            _applying = false;
        }

        EndSession();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Losing focus closes the popup, except while the result is being inserted.
        if (_sessionActive && !_applying)
        {
            EndSession();
        }
    }

    /// <summary>Cancels any running work and hides the window without inserting anything.</summary>
    public void EndSession()
    {
        if (!_sessionActive)
        {
            return;
        }

        _sessionActive = false;
        CancelRunning();
        _flushTimer.Stop();
        SetBusy(false);
        Hide();
    }

    private void SetBusy(bool busy)
    {
        // Also stop the animation itself: an indeterminate bar keeps repainting even when
        // collapsed, which shows up as idle CPU load.
        BusyBar.IsIndeterminate = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            _flushTimer.Start();
        }
        else
        {
            _flushTimer.Stop();
            FlushPending();
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        SetStatus(string.Empty);
    }

    private void HideError()
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    /// <summary>Greys out modes without an engine and explains why in the tooltip.</summary>
    private void UpdateModeAvailability()
    {
        foreach (var (mode, button) in _modeButtons)
        {
            var supported = _engine.SupportsMode(mode);
            button.IsEnabled = supported;
            button.ToolTip = supported
                ? null
                : $"{UiText.ModeLabel(mode)} benötigt ein lokales Sprachmodell (ab Phase 2).";

            var active = supported && mode == _mode;
            button.Background = active
                ? (Brush)FindResource("AccentBackground")
                : (Brush)FindResource("ChipBackground");
            button.Foreground = active
                ? (Brush)FindResource("AccentForeground")
                : (Brush)FindResource("TextPrimary");
            button.BorderBrush = active
                ? (Brush)FindResource("AccentBackground")
                : (Brush)FindResource("ChipBorder");
        }
    }

    private static string Shorten(string text, int maxLength)
    {
        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + " …";
    }
}
