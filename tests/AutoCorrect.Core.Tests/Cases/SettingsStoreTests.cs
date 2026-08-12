using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.Core.Tests.Cases;

public static class SettingsStoreTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Settings: defaults match the specification", () =>
        {
            var settings = new AppSettings();
            Assert.Equal("Ctrl+Alt+Space", settings.PrimaryHotkey);
            Assert.Equal("Ctrl+Alt+R", settings.RephraseHotkey);
            Assert.Equal("de-CH", settings.Language);
            Assert.Equal("http://localhost:8081/v2/check", settings.LanguageToolEndpoint);
            Assert.Equal("http://localhost:11434/v1", settings.LlmEndpoint);
            Assert.Equal(5000, settings.MaxInputLength);
            Assert.Equal(LogLevel.Warning, settings.LogLevel);
            Assert.False(settings.StartWithWindows);
            Assert.True(settings.ClipboardWaitMilliseconds is >= 80 and <= 120);
        });

        runner.Add("Settings: missing file yields defaults", () =>
        {
            using var temp = new TempDirectory();
            var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
            var settings = store.Load();
            Assert.Equal("Ctrl+Alt+Space", settings.PrimaryHotkey);
        });

        runner.Add("Settings: save and load round trip", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "sub", "settings.json");
            var store = new SettingsStore(path);

            var written = new AppSettings
            {
                PrimaryHotkey = "Ctrl+Shift+F9",
                RephraseHotkey = "",
                StartWithWindows = true,
                LanguageToolEndpoint = "http://127.0.0.1:9999/v2/check",
                Language = "de-DE",
                LogLevel = LogLevel.Debug,
                MaxInputLength = 1234,
            };
            store.Save(written);

            Assert.True(File.Exists(path), "settings file was not created");

            var read = new SettingsStore(path).Load();
            Assert.Equal("Ctrl+Shift+F9", read.PrimaryHotkey);
            Assert.Equal("", read.RephraseHotkey);
            Assert.True(read.StartWithWindows);
            Assert.Equal("http://127.0.0.1:9999/v2/check", read.LanguageToolEndpoint);
            Assert.Equal("de-DE", read.Language);
            Assert.Equal(LogLevel.Debug, read.LogLevel);
            Assert.Equal(1234, read.MaxInputLength);
        });

        runner.Add("Settings: file is human readable JSON with camelCase keys", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "settings.json");
            new SettingsStore(path).Save(new AppSettings());

            var json = File.ReadAllText(path);
            Assert.Contains("\"primaryHotkey\"", json);
            Assert.Contains("\"languageToolEndpoint\"", json);
            // Enums as text, so the file can be edited by hand or by a deployment script.
            Assert.Contains("\"logLevel\": \"Warning\"", json);
        });

        runner.Add("Settings: damaged file falls back to defaults and is kept aside", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, "{ this is not json");

            var settings = new SettingsStore(path).Load();
            Assert.Equal("Ctrl+Alt+Space", settings.PrimaryHotkey);
            Assert.True(File.Exists(path + ".invalid"), "damaged file was not preserved");
        });

        runner.Add("Settings: out of range values are clamped on load", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(
                path,
                """
                {
                  "primaryHotkey": "Ctrl+Alt+Space",
                  "maxInputLength": 99999999,
                  "clipboardWaitMilliseconds": 1,
                  "requestTimeoutSeconds": 0
                }
                """);

            var settings = new SettingsStore(path).Load();
            Assert.Equal(50_000, settings.MaxInputLength);
            Assert.Equal(80, settings.ClipboardWaitMilliseconds);
            Assert.Equal(2, settings.RequestTimeoutSeconds);
        });

        runner.Add("Settings: unusable hotkey falls back to the default", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, """{ "primaryHotkey": "Win+Space" }""");

            var settings = new SettingsStore(path).Load();
            Assert.Equal("Ctrl+Alt+Space", settings.PrimaryHotkey);
            Assert.Equal(HotkeyDefinition.DefaultPrimary, settings.PrimaryHotkeyDefinition);
        });

        runner.Add("Settings: empty rephrase hotkey disables the second hotkey", () =>
        {
            var settings = new AppSettings { RephraseHotkey = "" };
            Assert.True(settings.RephraseHotkeyDefinition is null);
        });

        runner.Add("Settings: clone is independent", () =>
        {
            var original = new AppSettings { PrimaryHotkey = "Ctrl+Alt+Space" };
            var clone = original.Clone();
            clone.PrimaryHotkey = "Ctrl+Alt+R";
            Assert.Equal("Ctrl+Alt+Space", original.PrimaryHotkey);
        });
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "autocorrect-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Nothing we can do in a test teardown.
        }
    }
}
