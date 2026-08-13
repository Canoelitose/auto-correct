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
using AutoCorrect.Core.Caching;
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
    private readonly ResultCache _cache = new();
    private readonly EngineRouter _engine;

    private PopupWindow _popup;

    private AppSettings _settings;
    private ContextMenu? _trayMenu;
    private MenuItem? _autoStartMenuItem;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private bool _busy;
    private bool _disposed;

    /// <summary>True when no settings file existed yet, so a welcome window is shown.</summary>
    public bool IsFirstStart { get; init; }

    public AppController(SettingsStore store, AppSettings settings)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // One HttpClient for the whole process: a client per request exhausts sockets.
        _http = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan, // cancellation is handled per request
        };

        _engine = EngineFactory.CreateRouter(_http, () => _settings, _cache);

        _messageWindow = new MessageWindow();
        _hotkeys = new HotkeyManager(_messageWindow);

        _tray = new TrayIcon(_messageWindow, $"{UiText.AppName} – {_settings.PrimaryHotkey}");
        _tray.Activated += (_, _) => ShowSettings();
        _tray.MenuRequested += (_, _) => ShowTrayMenu();

        UiText.Language = UiText.Resolve(_settings.InterfaceLanguage);

        _popup = CreatePopup();

        RegisterHotkeys(reportConflicts: true);

        // A first-run check: without a LanguageTool server nothing works, and finding that out
        // only after the first hotkey press is a poor first impression.
        _ = CheckEngineOnStartupAsync();

        // Keep the registry in sync with the setting, for example after the executable moved.
        if (_settings.StartWithWindows != AutoStartManager.IsEnabled())
        {
            AutoStartManager.SetEnabled(_settings.StartWithWindows);
        }
    }

    /// <summary>
    /// Checks once after start-up whether an engine answers, and warns if not.
    ///
    /// LanguageTool is usually started together with Windows and needs a good half minute to
    /// load its models. The check is therefore repeated a few times; a warning on a server that
    /// is merely still starting would be a false alarm.
    /// </summary>
    private async Task CheckEngineOnStartupAsync()
    {
        const int attempts = 4;
        const int pauseSeconds = 12;

        var available = false;

        try
        {
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                if (_disposed)
                {
                    return;
                }

                using var cts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(Core.Engines.LanguageTool.LanguageToolEngine.ProbeTimeoutSeconds + 2));

                if (await _engine.IsAvailableAsync(cts.Token).ConfigureAwait(true))
                {
                    Log.Info($"Engine reachable after {attempt} attempt(s).");
                    available = true;
                    break;
                }

                // On a first start there is no point waiting: the user is looking at the screen
                // right now and wants to know what is going on.
                if (IsFirstStart || attempt == attempts)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(pauseSeconds)).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("The start-up check failed.", ex);
        }

        if (_disposed)
        {
            return;
        }

        if (IsFirstStart)
        {
            ShowWelcome(available);
            return;
        }

        if (!available)
        {
            Log.Info("No engine reachable at start-up.");
            _tray.ShowNotification(
                UiText.AppName,
                UiText.LanguageToolMissingAtStartup,
                TrayNotificationLevel.Warning);
        }
    }

    /// <summary>
    /// The application has no main window, and Windows 11 hides new tray icons behind the
    /// chevron, so the very first start would otherwise look like nothing happened at all.
    /// </summary>
    private void ShowWelcome(bool engineAvailable)
    {
        try
        {
            var window = new WelcomeWindow(_settings, engineAvailable);
            window.ShowDialog();

            _tray.ShowNotification(
                UiText.AppName,
                UiText.TrayStarted(_settings.PrimaryHotkey));

            if (window.OpenSettingsRequested)
            {
                ShowSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("The welcome window could not be shown.", ex);
        }
    }

    private PopupWindow CreatePopup()
    {
        var popup = new PopupWindow(_engine, _injector, () => _engine.LastUsedName ?? _engine.Name);
        popup.Warmup();
        return popup;
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

        // A modal settings dialog disables the other windows of the process, so a popup opened
        // now would be visible but unusable.
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // Pressing the hotkey again while the popup is open would read the selection of our own
        // result box instead of the original application.
        if (_popup.IsSessionActive)
        {
            _popup.Activate();
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

        var uninstallItem = new MenuItem { Header = UiText.TrayUninstall };
        uninstallItem.Click += (_, _) => Uninstall();
        menu.Items.Add(uninstallItem);

        var exitItem = new MenuItem { Header = UiText.TrayExit };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Removes settings, logs and the autostart entry, then exits. There is no installer, so
    /// without this the user would have to know about two hidden folders and a registry value.
    /// </summary>
    private void Uninstall()
    {
        var confirmed = MessageBox.Show(
            UiText.UninstallConfirm,
            UiText.AppName,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var result = Uninstaller.RemoveUserData();

        MessageBox.Show(
            result.Failed.Count == 0 ? UiText.UninstallDone : UiText.UninstallPartial(result.Failed[0]),
            UiText.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        Uninstaller.ShowExecutableInExplorer();
        Application.Current.Shutdown();
    }

    private void ToggleAutoStart(MenuItem item)
    {
        var enabled = item.IsChecked;
        if (!AutoStartManager.SetEnabled(enabled))
        {
            item.IsChecked = !enabled;
            MessageBox.Show(
                UiText.AutostartFailed,
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

        var window = new SettingsWindow(_settings, EngineFactory.ProbeAsync, _cache)
        {
            Topmost = true,
        };

        _settingsWindow = window;
        try
        {
            var accepted = window.ShowDialog() == true;
            var chosenLanguage = accepted
                ? window.UpdatedSettings.InterfaceLanguage
                : _settings.InterfaceLanguage;

            // The dialog switches UiText.Language live for its preview, so it is reset here
            // to whatever was actually saved.
            UiText.Language = UiText.Resolve(chosenLanguage);

            if (accepted)
            {
                var updated = window.UpdatedSettings;
                var languageChanged = !string.Equals(
                    updated.InterfaceLanguage,
                    _settings.InterfaceLanguage,
                    StringComparison.OrdinalIgnoreCase);

                SaveSettings(updated);
                AutoStartManager.SetEnabled(updated.StartWithWindows);
                RegisterHotkeys(reportConflicts: true);

                if (languageChanged)
                {
                    // The popup is built once and reused, so it is rebuilt in the new language.
                    ReplacePopup();
                }
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

    private void ReplacePopup()
    {
        var old = _popup;
        _popup = CreatePopup();

        old.EndSession();
        old.Close();
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
                $"{UiText.SettingsNotSaved}\n\n{_store.FilePath}",
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
        _cache.Dispose();
    }
}
