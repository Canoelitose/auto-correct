using System.Windows;
using AutoCorrect.App.Hotkeys;
using AutoCorrect.App.Interop;
using AutoCorrect.App.Startup;
using AutoCorrect.App.Tray;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Tests;

namespace AutoCorrect.App.Tests.Cases;

public static class EnvironmentTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Environment: runs on Windows with a WPF application and an STA thread", () =>
        {
            Assert.True(OperatingSystem.IsWindows(), "these tests only make sense on Windows");
            Assert.NotNull(Application.Current);
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        });

        runner.Add("Environment: the application icon can be loaded as an HICON", () =>
        {
            var icon = IconLoader.LoadApplicationIcon(out var ownsHandle);
            try
            {
                Assert.True(icon != IntPtr.Zero, "no icon handle was returned");
            }
            finally
            {
                if (ownsHandle && icon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(icon);
                }
            }
        });
    }
}

public static class AutoStartTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Autostart: writes and removes the Run entry", () =>
        {
            var wasEnabled = AutoStartManager.IsEnabled();

            try
            {
                Assert.True(AutoStartManager.SetEnabled(true), "the entry could not be written");
                Assert.True(AutoStartManager.IsEnabled(), "the entry was not reported as set");

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run");

                var value = key?.GetValue("AutoCorrect") as string;
                Assert.NotNull(value);
                Assert.Contains(".exe", value!);

                Assert.True(AutoStartManager.SetEnabled(false), "the entry could not be removed");
                Assert.False(AutoStartManager.IsEnabled(), "the entry survived removal");
            }
            finally
            {
                AutoStartManager.SetEnabled(wasEnabled);
            }
        });
    }
}

public static class MessageWindowTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("MessageWindow: creates a real window handle and disposes cleanly", () =>
        {
            using var window = new MessageWindow();

            Assert.True(window.Handle != IntPtr.Zero, "no window handle");
            Assert.True(NativeMethods.IsWindow(window.Handle), "the handle is not a window");
        });

        runner.Add("MessageWindow: the window is never visible", () =>
        {
            using var window = new MessageWindow();

            // A hidden helper window must not show up anywhere for the user.
            Assert.False(NativeMethods.IsWindowVisible(window.Handle), "the helper window is visible");
        });
    }
}

public static class HotkeyTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Hotkey: registers the default combination with Windows", () =>
        {
            using var window = new MessageWindow();
            using var manager = new HotkeyManager(window);

            var result = manager.Register(HotkeyDefinition.DefaultPrimary, () => { });

            Assert.True(result.Success, $"registration failed: {result.Error}");
            Assert.True(result.Error is null, "a successful registration must not report an error");
        });

        runner.Add("Hotkey: a conflict is reported instead of failing silently", () =>
        {
            using var windowA = new MessageWindow();
            using var managerA = new HotkeyManager(windowA);
            using var windowB = new MessageWindow();
            using var managerB = new HotkeyManager(windowB);

            // Ctrl+Alt+F9 is unlikely to be taken by the machine itself.
            var hotkey = new HotkeyDefinition(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKeys.TryGetCode("F9", out var f9) ? f9 : 0x78);

            var first = managerA.Register(hotkey, () => { });
            Assert.True(first.Success, $"the first registration should succeed: {first.Error}");

            var second = managerB.Register(hotkey, () => { });
            Assert.False(second.Success, "the second registration must fail");
            Assert.NotNull(second.Error);
            Assert.Contains(hotkey.ToString(), second.Error!);
        });

        runner.Add("Hotkey: unregistering frees the combination again", () =>
        {
            var hotkey = new HotkeyDefinition(
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
                VirtualKeys.TryGetCode("F8", out var f8) ? f8 : 0x77);

            using var window = new MessageWindow();
            using var manager = new HotkeyManager(window);

            Assert.True(manager.Register(hotkey, () => { }).Success, "first registration failed");
            manager.UnregisterAll();

            // If unregistering did not work, this second attempt would fail.
            Assert.True(manager.Register(hotkey, () => { }).Success, "the hotkey was not released");
        });

        runner.Add("Hotkey: an invalid combination is rejected before touching Windows", () =>
        {
            using var window = new MessageWindow();
            using var manager = new HotkeyManager(window);

            var result = manager.Register(new HotkeyDefinition(HotkeyModifiers.None, VirtualKeys.Space), () => { });

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        });
    }
}

public static class TrayIconTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Tray: icon is added to the notification area and removed again", () =>
        {
            using var window = new MessageWindow();
            using var tray = new TrayIcon(window, "AutoCorrect test");

            // Shell_NotifyIcon has no query API, so the check is that the whole life cycle
            // runs through without an exception and the tooltip can be changed.
            tray.SetTooltip("AutoCorrect test 2");
            tray.ShowNotification("Test", "Ein Hinweis", TrayNotificationLevel.Info);
        });

        runner.Add("Tray: a notification with all levels does not throw", () =>
        {
            using var window = new MessageWindow();
            using var tray = new TrayIcon(window, "AutoCorrect test");

            tray.ShowNotification("Info", "text", TrayNotificationLevel.Info);
            tray.ShowNotification("Warning", "text", TrayNotificationLevel.Warning);
            tray.ShowNotification("Error", "text", TrayNotificationLevel.Error);
        });

        runner.Add("Tray: disposing twice is harmless", () =>
        {
            using var window = new MessageWindow();
            var tray = new TrayIcon(window, "AutoCorrect test");

            tray.Dispose();
            tray.Dispose();
        });
    }
}
