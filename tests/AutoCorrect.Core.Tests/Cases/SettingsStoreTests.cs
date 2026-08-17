using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines.Llm;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.Core.Tests.Cases;

public static class SettingsStoreTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Settings: defaults match the specification", () =>
        {
            var settings = new AppSettings();
            Assert.Equal("Win+Space", settings.PrimaryHotkey);
            Assert.Equal("Ctrl+Alt+R", settings.RephraseHotkey);
            // Automatic detection by default: German and English both work without switching,
            // and preferredVariants keeps Swiss spelling.
            Assert.Equal("auto", settings.Language);
            Assert.Equal("de-CH,en-US", settings.PreferredVariants);
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
            Assert.Equal("Win+Space", settings.PrimaryHotkey);
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
            Assert.Equal("Win+Space", settings.PrimaryHotkey);
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
            File.WriteAllText(path, """{ "primaryHotkey": "Ctrl+Alt" }""");

            var settings = new SettingsStore(path).Load();
            Assert.Equal("Win+Space", settings.PrimaryHotkey);
            Assert.Equal(HotkeyDefinition.DefaultPrimary, settings.PrimaryHotkeyDefinition);
        });

        runner.Add("Settings: empty rephrase hotkey disables the second hotkey", () =>
        {
            var settings = new AppSettings { RephraseHotkey = "" };
            Assert.True(settings.RephraseHotkeyDefinition is null);
        });

        runner.Add("Settings: clone is independent", () =>
        {
            var original = new AppSettings { PrimaryHotkey = "Win+Space" };
            var clone = original.Clone();
            clone.PrimaryHotkey = "Ctrl+Alt+R";
            Assert.Equal("Win+Space", original.PrimaryHotkey);
        });

        runner.Add("Settings: the protected words of a clone are a copy, not the same list", () =>
        {
            // The settings dialog works on a clone. A shared list would let a cancelled dialog
            // change what the running application protects.
            var original = new AppSettings { LlmProtectedTerms = ["Zwiebelturm"] };
            var clone = original.Clone();

            clone.LlmProtectedTerms.Add("Nordwind");

            Assert.Equal(1, original.LlmProtectedTerms.Count);
            Assert.Equal(2, clone.LlmProtectedTerms.Count);
        });

        runner.Add("Settings: the model and privacy settings survive a round trip", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "settings.json");

            new SettingsStore(path).Save(new AppSettings
            {
                LlmEndpoint = "https://integrate.api.nvidia.com/v1",
                LlmModel = "meta/llama-3.1-8b-instruct",
                LlmApiKey = "nvapi-abc",
                LlmMaskNames = AppSettings.MaskNamesAlways,
                LlmProtectedTerms = ["Zwiebelturm", "Nordwind"],
            });

            var read = new SettingsStore(path).Load();

            Assert.Equal("https://integrate.api.nvidia.com/v1", read.LlmEndpoint);
            Assert.Equal("meta/llama-3.1-8b-instruct", read.LlmModel);
            Assert.Equal("nvapi-abc", read.LlmApiKey);
            Assert.Equal(AppSettings.MaskNamesAlways, read.LlmMaskNames);
            Assert.Equal(2, read.LlmProtectedTerms.Count);
            Assert.Contains("Zwiebelturm", string.Join(",", read.LlmProtectedTerms));
        });

        runner.Add("Settings: an NVIDIA key sets the address by itself", () =>
        {
            // Pasting the key is the whole setup: the key says where it belongs, so making the
            // user find and type the address as well would be busywork.
            var settings = new AppSettings { LlmApiKey = "nvapi-abcdef" };
            settings.Normalize();

            Assert.Equal(LlmEngine.NvidiaEndpoint, settings.LlmEndpoint);

            // The local model name means nothing there; the catalogue picks from what is offered.
            Assert.Equal("", settings.LlmModel);
        });

        runner.Add("Settings: an address the user chose is never overwritten by a key", () =>
        {
            var settings = new AppSettings
            {
                LlmApiKey = "nvapi-abcdef",
                LlmEndpoint = "http://192.168.1.20:11434/v1",
            };
            settings.Normalize();

            Assert.Equal("http://192.168.1.20:11434/v1", settings.LlmEndpoint);
        });

        runner.Add("Settings: a chosen model survives the key too", () =>
        {
            var settings = new AppSettings
            {
                LlmApiKey = "nvapi-abcdef",
                LlmModel = "meta/llama-3.1-8b-instruct",
            };
            settings.Normalize();

            Assert.Equal(LlmEngine.NvidiaEndpoint, settings.LlmEndpoint);
            Assert.Equal("meta/llama-3.1-8b-instruct", settings.LlmModel);
        });

        runner.Add("Settings: without a key the local defaults stay in place", () =>
        {
            var settings = new AppSettings();
            settings.Normalize();

            Assert.Equal(LlmEngine.DefaultEndpoint, settings.LlmEndpoint);
            Assert.Equal(LlmEngine.DefaultModel, settings.LlmModel);
        });

        runner.Add("Settings: blank and duplicate protected words are dropped", () =>
        {
            var settings = new AppSettings { LlmProtectedTerms = ["  Nordwind ", "", "   ", "nordwind"] };
            settings.Normalize();

            Assert.Equal(1, settings.LlmProtectedTerms.Count);
            Assert.Equal("Nordwind", settings.LlmProtectedTerms[0]);
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
