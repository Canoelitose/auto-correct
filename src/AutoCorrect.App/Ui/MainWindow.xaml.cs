using System.Windows;
using System.Windows.Controls;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App.Ui;

/// <summary>
/// The window for working on text without a hotkey: paste or type, pick a mode, take the result.
///
/// The hotkey and the popup remain the fast path and are untouched. This window exists because a
/// program with no window at all is hard to trust and hard to find - and because text that is
/// not selected somewhere else has nowhere to go otherwise.
///
/// Closing it hides it; the application keeps running in the notification area. Exiting is the
/// tray menu's job, so a closed window never silently switches the hotkey off.
/// </summary>
public partial class MainWindow : Window
{
    private const int FlushIntervalMs = 30;

    private readonly ITextEngine _engine;
    private readonly Func<string> _engineName;
    private readonly Action _openSettings;
    private readonly Action _openAbout;

    private readonly Dictionary<ProcessingMode, Button> _modeButtons = new();
    private readonly System.Windows.Threading.DispatcherTimer _flushTimer;
    private readonly System.Text.StringBuilder _pending = new();
    private readonly object _pendingLock = new();

    private CancellationTokenSource? _cts;
    private int _runId;
    private ProcessingMode _mode = ProcessingMode.Correct;
    private bool _closingForReal;

    public MainWindow(
        ITextEngine engine,
        Func<string> engineName,
        Action openSettings,
        Action openAbout)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _engineName = engineName ?? throw new ArgumentNullException(nameof(engineName));
        _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));
        _openAbout = openAbout ?? throw new ArgumentNullException(nameof(openAbout));

        InitializeComponent();

        _flushTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render)
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
            button.Click += (_, _) => Run(captured);
        }

        SettingsButton.Click += (_, _) => _openSettings();
        AboutButton.Click += (_, _) => _openAbout();
        CopyButton.Click += (_, _) => CopyResult();
        UseResultButton.Click += (_, _) => MoveResultToInput();
        ClearButton.Click += (_, _) => ClearAll();

        InputBox.TextChanged += (_, _) => UpdateButtons();

        ApplyTexts();
        UpdateButtons();
    }

    /// <summary>Applies every label, so a language change takes effect without a restart.</summary>
    public void ApplyTexts()
    {
        Title = UiText.AppName;
        TitleText.Text = UiText.MainTitle;
        InputLabel.Text = UiText.MainInputLabel;
        ResultLabel.Text = UiText.MainResultLabel;
        SettingsButton.Content = UiText.TraySettings;
        AboutButton.Content = UiText.TrayAbout;
        CopyButton.Content = UiText.MainCopy;
        UseResultButton.Content = UiText.MainUseResult;
        ClearButton.Content = UiText.MainClear;

        foreach (var (mode, button) in _modeButtons)
        {
            button.Content = UiText.ModeLabel(mode);
        }

        UpdateHotkeyHint(_hotkey);
    }

    private string _hotkey = string.Empty;

    /// <summary>The window mentions the hotkey, so the fast path stays discoverable.</summary>
    public void UpdateHotkeyHint(string hotkey)
    {
        _hotkey = hotkey ?? string.Empty;
        HotkeyHintText.Text = string.IsNullOrEmpty(_hotkey)
            ? UiText.MainSubtitle
            : $"{UiText.MainSubtitle} {UiText.MainHotkeyHint(_hotkey)}";
    }

    /// <summary>Brings the window up and focuses the input, whatever state it was in.</summary>
    public void ShowAndFocus()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        InputBox.Focus();
        InputBox.SelectAll();
    }

    /// <summary>Ends the session for good; until then closing only hides the window.</summary>
    public void CloseForReal()
    {
        _closingForReal = true;
        CancelRunning();
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closingForReal)
        {
            // The application lives in the notification area. Closing the window there is a
            // "put it away", not a "quit" - quitting is the tray menu.
            e.Cancel = true;
            CancelRunning();
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void Run(ProcessingMode mode)
    {
        var input = InputBox.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        _mode = mode;
        UpdateButtons();
        _ = RunAsync(mode, input);
    }

    private async Task RunAsync(ProcessingMode mode, string input)
    {
        CancelRunning();

        var runId = ++_runId;
        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;

        lock (_pendingLock)
        {
            _pending.Clear();
        }

        ResultBox.Clear();
        HideError();
        SetBusy(true);
        StatusText.Text = $"{UiText.ModeLabel(mode)} · {UiText.StatusWorking}";

        try
        {
            // Off the UI thread; the chunks are buffered and flushed by the timer, exactly as
            // in the popup, so a fast model cannot flood the dispatcher.
            await Task.Run(
                async () =>
                {
                    await foreach (var chunk in _engine.ProcessAsync(input, mode, ct).ConfigureAwait(false))
                    {
                        if (chunk.Length == 0)
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

            if (runId != _runId)
            {
                return;
            }

            FlushPending();

            var unchanged = string.Equals(ResultBox.Text, input, StringComparison.Ordinal);
            StatusText.Text = $"{_engineName()} · {UiText.ModeLabel(mode)} · " +
                              (unchanged ? UiText.StatusNoChange : UiText.StatusDone);
        }
        catch (OperationCanceledException)
        {
            if (runId == _runId)
            {
                StatusText.Text = UiText.StatusCancelled;
            }
        }
        catch (EngineUnavailableException ex)
        {
            if (runId == _runId)
            {
                ShowError(ex.Message);
            }
        }
        catch (NotSupportedException ex)
        {
            Log.Warn("Engine rejected the requested mode.", ex);
            if (runId == _runId)
            {
                ShowError(UiText.ModeNotSupported);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Processing failed in the main window.", ex);
            if (runId == _runId)
            {
                ShowError(UiText.UnexpectedError);
            }
        }
        finally
        {
            if (runId == _runId)
            {
                SetBusy(false);
                UpdateButtons();
            }

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
        ResultBox.ScrollToEnd();
    }

    private void CancelRunning()
    {
        var running = _cts;
        _cts = null;

        if (running is null)
        {
            return;
        }

        try
        {
            running.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CopyResult()
    {
        if (string.IsNullOrEmpty(ResultBox.Text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(ResultBox.Text);
            StatusText.Text = UiText.StatusCopied;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Another process can hold the clipboard open; that is not worth an error dialog.
            Log.Warn("The clipboard could not be written.", ex);
            ShowError(UiText.ClipboardBusy);
        }
    }

    /// <summary>Moves the result up into the input, so several steps can be chained.</summary>
    private void MoveResultToInput()
    {
        if (string.IsNullOrEmpty(ResultBox.Text))
        {
            return;
        }

        InputBox.Text = ResultBox.Text;
        ResultBox.Clear();
        StatusText.Text = string.Empty;
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
        UpdateButtons();
    }

    private void ClearAll()
    {
        CancelRunning();
        InputBox.Clear();
        ResultBox.Clear();
        StatusText.Text = string.Empty;
        HideError();
        SetBusy(false);
        InputBox.Focus();
        UpdateButtons();
    }

    private void SetBusy(bool busy)
    {
        BusyBar.IsIndeterminate = busy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        foreach (var button in _modeButtons.Values)
        {
            button.IsEnabled = !busy;
        }

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

    private void UpdateButtons()
    {
        var hasInput = !string.IsNullOrWhiteSpace(InputBox.Text);
        var hasResult = !string.IsNullOrEmpty(ResultBox.Text);

        foreach (var (mode, button) in _modeButtons)
        {
            button.IsEnabled = hasInput && _engine.SupportsMode(mode);
            button.Tag = mode == _mode ? "active" : null;
        }

        CopyButton.IsEnabled = hasResult;
        UseResultButton.IsEnabled = hasResult;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        StatusText.Text = string.Empty;
    }

    private void HideError()
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    // ---------------------------------------------------------------- for the tests

    internal string InputTextForTests
    {
        get => InputBox.Text;
        set => InputBox.Text = value;
    }

    internal string ResultTextForTests => ResultBox.Text;

    internal string StatusTextForTests => StatusText.Text;

    internal bool ErrorVisibleForTests => ErrorPanel.Visibility == Visibility.Visible;

    internal bool IsModeEnabledForTests(ProcessingMode mode) => _modeButtons[mode].IsEnabled;

    internal void RunForTests(ProcessingMode mode) => Run(mode);

    internal string TitleTextForTests => TitleText.Text;

    internal string ErrorMessageForTests => ErrorText.Text;
}
