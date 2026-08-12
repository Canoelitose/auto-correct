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
            // A second instance would fight over the same hotkeys, so it exits silently.
            Shutdown();
            return;
        }

        var store = new SettingsStore();
        var settings = store.Load();

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
            _controller = new AppController(store, settings);
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
