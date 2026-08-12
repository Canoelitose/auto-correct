using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using AutoCorrect.App.Capture;
using AutoCorrect.App.Engines;
using AutoCorrect.App.Hotkeys;
using AutoCorrect.App.Interop;
using AutoCorrect.App.Startup;
using AutoCorrect.App.Tray;
using AutoCorrect.App.Ui;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App;

/// <summary>
/// Wires tray, hotkeys, capture and popup together and owns the application state.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly HttpClient _http;
    private readonly MessageWindow _messageWindow;
    private readonly HotkeyManager _hotkeys;
    private readonly TrayIcon _tray;
    private readonly SelectionCapture _capture = new();
    private readonly TextInjector _injector = new();
    private readonly ITextEngine _engine;
    private readonly PopupWindow _popup;

    private AppSettings _settings;
    private ContextMenu? _trayMenu;
    private MenuItem? _autoStartMenuItem;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private bool _busy;
    private bool _disposed;

    public AppController(SettingsStore store, AppSettings settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // One HttpClient for the whole process: a client per request exhausts sockets.
        _http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan, // cancellation is handled per request
        };

        _engine = EngineFactory.CreateRouter(_http, () => _settings);

        _messageWindow = new MessageWindow();
        _hotkeys = new HotkeyManager(_messageWindow);

        _tray = new TrayIcon(_messageWindow, $"{UiText.AppName} – {_settings.PrimaryHotkey}");
        _tray.Activated += (_, _) => ShowSettings();
        _tray.MenuRequested += (_, _) => ShowTrayMenu();

        _popup = new PopupWindow(_engine, _injector);
        _popup.Warmup();

        RegisterHotkeys(reportConflicts: true);

        // Keep the registry in sync with the setting, for example after the executable moved.
        if (_settings.StartWithWindows != AutoStartManager.IsEnabled())
        {
            AutoStartManager.SetEnabled(_settings.StartWithWindows);
        }
    }

    // ---------------------------------------------------------------- hotkeys

    private void RegisterHotkeys(bool reportConflicts)
    {
        _hotkeys.UnregisterAll();

        var failures = new List<string>();

        var primary = _hotkeys.Register(
            _settings.PrimaryHotkeyDefinition,
            () => RunAsync(ProcessingMode.Correct));

        if (!primary.Success && primary.Error is not null)
        {
            failures.Add(primary.Error);
        }

        if (_settings.RephraseHotkeyDefinition is { } rephrase)
        {
            var second = _hotkeys.Register(rephrase, () => RunAsync(ProcessingMode.Rephrase));
            if (!second.Success && second.Error is not null)
            {
                failures.Add(second.Error);
            }
        }

        _tray.SetTooltip($"{UiText.AppName} – {_settings.PrimaryHotkey}");

        if (reportConflicts && failures.Count > 0)
        {
            // A hotkey conflict is never swallowed: without a working hotkey the application is
            // unusable and the user has to be able to pick a different combination.
            MessageBox.Show(
                string.Join(Environment.NewLine + Environment.NewLine, failures),
                UiText.HotkeyRegistrationFailedTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            ShowSettings();
        }
    }

    // ---------------------------------------------------------------- main flow

    private async void RunAsync(ProcessingMode mode)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            // Remember the window the text came from: the result is pasted back into it.
            var target = NativeMethods.GetForegroundWindow();

            var selection = await _capture.CaptureAsync(_settings, CancellationToken.None).ConfigureAwait(true);

            if (!selection.HasText)
            {
                _tray.ShowNotification(UiText.NoSelection, UiText.NoSelectionHint);
                return;
            }

            var text = selection.Text!;
            if (text.Length > _settings.MaxInputLength)
            {
                _tray.ShowNotification(
                    UiText.AppName,
                    UiText.TextTooLong(text.Length, _settings.MaxInputLength),
                    TrayNotificationLevel.Warning);
                return;
            }

            _popup.StartSession(text, mode, target);
        }
        catch (Exception ex)
        {
            Log.Error("Hotkey handling failed.", ex);
            _tray.ShowNotification(UiText.AppName, UiText.UnexpectedError, TrayNotificationLevel.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------------------------------------------------------------- tray menu

    private void ShowTrayMenu()
    {
        _trayMenu ??= BuildTrayMenu();

        if (_autoStartMenuItem is not null)
        {
            _autoStartMenuItem.IsChecked = AutoStartManager.IsEnabled();
        }

        // The menu closes on click outside only when our window owns the foreground.
        NativeMethods.SetForegroundWindow(_messageWindow.Handle);

        _trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _trayMenu.IsOpen = true;
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();

        var settingsItem = new MenuItem { Header = UiText.TraySettings, FontWeight = FontWeights.SemiBold };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = UiText.TrayAbout };
        aboutItem.Click += (_, _) => ShowAbout();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

        var autoStartItem = new MenuItem
        {
            Header = UiText.TrayStartWithWindows,
            IsCheckable = true,
            IsChecked = AutoStartManager.IsEnabled(),
        };
        autoStartItem.Click += (_, _) => ToggleAutoStart(autoStartItem);
        menu.Items.Add(autoStartItem);
        _autoStartMenuItem = autoStartItem;

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = UiText.TrayExit };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ToggleAutoStart(MenuItem item)
    {
        var enabled = item.IsChecked;
        if (!AutoStartManager.SetEnabled(enabled))
        {
            item.IsChecked = !enabled;
            MessageBox.Show(
                "Der Autostart-Eintrag konnte nicht geschrieben werden.",
                UiText.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _settings.StartWithWindows = enabled;
        SaveSettings(_settings);
    }

    // ---------------------------------------------------------------- windows

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _popup.EndSession();

        var window = new SettingsWindow(_settings, EngineFactory.ProbeAsync)
        {
            Topmost = true,
        };

        _settingsWindow = window;
        try
        {
            if (window.ShowDialog() == true)
            {
                var updated = window.UpdatedSettings;
                SaveSettings(updated);
                AutoStartManager.SetEnabled(updated.StartWithWindows);
                RegisterHotkeys(reportConflicts: true);
            }
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ShowAbout()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(_settings, _engine.Name) { Topmost = true };
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
    }

    private void SaveSettings(AppSettings settings)
    {
        _settings = settings;

        try
        {
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Settings could not be saved.", ex);
            MessageBox.Show(
                $"Die Einstellungen konnten nicht gespeichert werden.\n\n{_store.FilePath}",
                UiText.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (Log.Current is { } logger)
        {
            logger.MinimumLevel = settings.LogLevel;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _popup.EndSession();
        _popup.Close();
        _tray.Dispose();
        _hotkeys.Dispose();
        _messageWindow.Dispose();
        _http.Dispose();
    }
}
