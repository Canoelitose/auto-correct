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

public static class SpellCheckTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Spell check: the Windows spell checker is available", async () =>
        {
            var engine = new AutoCorrect.App.Engines.WindowsSpellCheckEngine(() => new AppSettings());

            // If this fails the COM interfaces are wrong, or Windows has no dictionary at all.
            Assert.True(
                await engine.IsAvailableAsync(CancellationToken.None),
                "no spell checking language available on this machine");
        });

        runner.Add("Spell check: corrects an English typo without any server", async () =>
        {
            var settings = new AppSettings { Language = "en-US" };
            var engine = new AutoCorrect.App.Engines.WindowsSpellCheckEngine(() => settings);

            var result = await Collect(engine, "This sentense has a mistke in it.");

            Assert.False(result.Contains("sentense", StringComparison.Ordinal), $"not corrected: {result}");
            Assert.False(result.Contains("mistke", StringComparison.Ordinal), $"not corrected: {result}");
        });

        runner.Add("Spell check: correct text is returned unchanged", async () =>
        {
            var settings = new AppSettings { Language = "en-US" };
            var engine = new AutoCorrect.App.Engines.WindowsSpellCheckEngine(() => settings);

            const string input = "This sentence is perfectly fine.";
            Assert.Equal(input, await Collect(engine, input));
        });

        runner.Add("Spell check: only the correction mode is supported", () =>
        {
            var engine = new AutoCorrect.App.Engines.WindowsSpellCheckEngine(() => new AppSettings());

            Assert.True(engine.SupportsMode(AutoCorrect.Core.Engines.ProcessingMode.Correct));
            Assert.False(engine.SupportsMode(AutoCorrect.Core.Engines.ProcessingMode.Rephrase));
        });

        runner.Add("Spell check: the fallback chain uses it when LanguageTool is missing", async () =>
        {
            // The situation a fresh download lands in: no server anywhere. Both addresses point
            // at a dead port on purpose, so the test keeps its meaning on a machine that happens
            // to have Ollama running.
            var settings = new AppSettings
            {
                LanguageToolEndpoint = "http://127.0.0.1:1/v2/check",
                LlmEndpoint = "http://127.0.0.1:1/v1",
                Language = "en-US",
                RequestTimeoutSeconds = 3,
            };

            using var http = new System.Net.Http.HttpClient();
            var router = AutoCorrect.App.Engines.EngineFactory.CreateRouter(http, () => settings);

            var result = await Collect(router, "This sentense has a mistke.");

            Assert.False(result.Contains("sentense", StringComparison.Ordinal), $"not corrected: {result}");
            Assert.Equal("Windows", router.LastUsedName);
        });
    }

    private static async Task<string> Collect(AutoCorrect.Core.Engines.ITextEngine engine, string input)
    {
        var text = new System.Text.StringBuilder();
        await foreach (var chunk in engine.ProcessAsync(
            input, AutoCorrect.Core.Engines.ProcessingMode.Correct, CancellationToken.None))
        {
            text.Append(chunk);
        }

        return text.ToString();
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

public static class UninstallTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Uninstall: removes the autostart entry", () =>
        {
            var wasEnabled = AutoStartManager.IsEnabled();

            try
            {
                AutoStartManager.SetEnabled(true);
                Assert.True(AutoStartManager.IsEnabled(), "precondition: autostart is set");

                var result = Uninstaller.RemoveUserData();

                Assert.False(AutoStartManager.IsEnabled(), "the autostart entry survived");
                Assert.True(
                    result.Removed.Any(r => r.Contains("Run", StringComparison.Ordinal)),
                    "the removed autostart entry was not reported");
            }
            finally
            {
                AutoStartManager.SetEnabled(wasEnabled);
            }
        });

        runner.Add("Uninstall: reports the folders it works on and never throws", () =>
        {
            // Runs even when nothing is there; a removal must not fail on a clean machine.
            var result = Uninstaller.RemoveUserData();

            Assert.NotNull(result.Removed);
            Assert.NotNull(result.Failed);
            Assert.Contains("AutoCorrect", Uninstaller.SettingsDirectory);
            Assert.Contains("AutoCorrect", Uninstaller.LogDirectory);
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

            // The keyboard hook must not be used to take a combination away from whoever holds
            // it; a conflict stays a conflict and is reported.
            var second = managerB.Register(hotkey, () => { });
            Assert.False(second.Success, "the second registration must fail");
            Assert.NotNull(second.Error);
            Assert.Contains(hotkey.ToString(), second.Error!);
            Assert.Equal(HotkeyMethod.None, second.Method);
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

        runner.Add("Hotkey: Win+Space is delivered through the keyboard hook", () =>
        {
            // RegisterHotKey cannot claim Win+Space because the shell owns it for the layout
            // switcher, so the manager has to fall back to the low level keyboard hook.
            using var window = new MessageWindow();
            using var manager = new HotkeyManager(window);

            var result = manager.Register(HotkeyDefinition.DefaultPrimary, () => { });

            Assert.True(result.Success, $"Win+Space could not be registered: {result.Error}");
            Assert.Equal(HotkeyMethod.KeyboardHook, result.Method);
        });

        runner.Add("Hotkey: an ordinary combination uses the normal system hotkey", () =>
        {
            using var window = new MessageWindow();
            using var manager = new HotkeyManager(window);

            var hotkey = new HotkeyDefinition(
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
                VirtualKeys.TryGetCode("F7", out var f7) ? f7 : 0x76);

            var result = manager.Register(hotkey, () => { });

            Assert.True(result.Success, $"registration failed: {result.Error}");
            Assert.Equal(HotkeyMethod.SystemHotkey, result.Method);
        });

        runner.Add("Hotkey: the keyboard hook is removed again on dispose", () =>
        {
            var window = new MessageWindow();
            var manager = new HotkeyManager(window);

            Assert.True(manager.Register(HotkeyDefinition.DefaultPrimary, () => { }).Success);

            // A hook left behind would keep swallowing Win+Space for the whole session.
            manager.Dispose();
            window.Dispose();

            using var second = new MessageWindow();
            using var again = new HotkeyManager(second);
            Assert.True(again.Register(HotkeyDefinition.DefaultPrimary, () => { }).Success,
                "the hook could not be installed a second time");
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
