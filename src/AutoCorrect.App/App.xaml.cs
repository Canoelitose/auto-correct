using System.IO;
using System.Windows;
using System.Windows.Threading;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\AutoCorrect.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private FileLogger? _logger;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // A second instance would fight over the same hotkeys. Exiting silently made the
            // application look broken: a second double click simply did nothing.
            var store2 = new SettingsStore();
            UiText.Language = UiText.Resolve(store2.Load().InterfaceLanguage);

            MessageBox.Show(
                UiText.AlreadyRunning,
                UiText.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        var store = new SettingsStore();

        // No settings file yet means this is the first start on this machine.
        var isFirstStart = !File.Exists(store.FilePath);
        var settings = store.Load();

        if (isFirstStart)
        {
            // Write the defaults straight away. Without the file the next start would look like
            // a first start again and show the welcome window over and over.
            try
            {
                store.Save(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not fatal: the application runs on defaults, only the welcome window repeats.
            }
        }

        _logger = new FileLogger(Log.DefaultFilePath, settings.LogLevel);
        Log.Initialize(_logger);
        Log.Info($"{UiText.AppName} starting.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception on a background thread.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        try
        {
            _controller = new AppController(store, settings) { IsFirstStart = isFirstStart };
        }
        catch (Exception ex)
        {
            Log.Error("Start-up failed.", ex);
            MessageBox.Show(
                $"{UiText.AppName} konnte nicht gestartet werden.\n\n{ex.Message}",
                UiText.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception on the UI thread.", e.Exception);

        MessageBox.Show(
            $"{UiText.UnexpectedError}\n\n{e.Exception.Message}",
            UiText.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Keep the tray application alive: a single failed action must not end the session.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info($"{UiText.AppName} exiting.");

        _controller?.Dispose();
        _logger?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned, nothing to release.
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
