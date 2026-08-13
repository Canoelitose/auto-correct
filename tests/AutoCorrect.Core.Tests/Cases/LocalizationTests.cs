using System.Globalization;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Engines;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.Core.Tests.Cases;

public static class LocalizationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("UiText: both languages ship in the same build", () =>
        {
            using var _ = new LanguageScope(UiLanguage.German);
            Assert.Equal("Einstellungen", UiText.TraySettings);
            Assert.Equal("Korrigieren", UiText.ModeCorrect);

            UiText.Language = UiLanguage.English;
            Assert.Equal("Settings", UiText.TraySettings);
            Assert.Equal("Correct", UiText.ModeCorrect);
        });

        runner.Add("UiText: German uses Swiss spelling, never ß", () =>
        {
            using var _ = new LanguageScope(UiLanguage.German);

            var samples = new[]
            {
                UiText.AboutClose,
                UiText.NoSelection,
                UiText.NoSelectionHint,
                UiText.StatusWorking,
                UiText.SettingsCorrectionLanguageHint,
                UiText.LanguageToolUnavailable,
                UiText.UnexpectedError,
                UiText.HotkeyNeedsModifier,
                UiText.HotkeyWinSpaceNote,
                UiText.TextTooLong(6000, 5000),
                UiText.ModeFallbackNote(ProcessingMode.Rephrase),
            };

            foreach (var sample in samples)
            {
                Assert.False(sample.Contains('ß'), $"Swiss spelling violated: {sample}");
            }
        });

        runner.Add("UiText: no string is left untranslated in English", () =>
        {
            // Every text is compared in both languages; only deliberate exceptions may match.
            string[] intentionallyIdentical = ["AutoCorrect", "Original", "Engine"];

            foreach (var (name, german, english) in AllTexts())
            {
                Assert.True(german.Length > 0, $"{name} is empty in German");
                Assert.True(english.Length > 0, $"{name} is empty in English");

                if (german == english && !intentionallyIdentical.Contains(german))
                {
                    // Command lines and addresses are the same in both languages, so only
                    // plain prose is required to differ.
                    var isTechnical = german.Contains("http", StringComparison.Ordinal) ||
                                      german.Contains("java", StringComparison.Ordinal) ||
                                      german.Contains("AutoCorrect", StringComparison.Ordinal);

                    Assert.True(isTechnical, $"{name} was not translated: \"{german}\"");
                }
            }
        });

        runner.Add("UiText: mode labels differ per language", () =>
        {
            using var _ = new LanguageScope(UiLanguage.German);
            Assert.Equal("Umformulieren", UiText.ModeLabel(ProcessingMode.Rephrase));
            Assert.Equal("Kürzer", UiText.ModeLabel(ProcessingMode.Shorten));

            UiText.Language = UiLanguage.English;
            Assert.Equal("Rephrase", UiText.ModeLabel(ProcessingMode.Rephrase));
            Assert.Equal("Shorter", UiText.ModeLabel(ProcessingMode.Shorten));
        });

        runner.Add("UiText: setting value resolves to a language", () =>
        {
            Assert.Equal(UiLanguage.German, UiText.Resolve("de"));
            Assert.Equal(UiLanguage.German, UiText.Resolve("DE"));
            Assert.Equal(UiLanguage.English, UiText.Resolve("en"));
            Assert.Equal(UiLanguage.English, UiText.Resolve("EN-GB"));
        });

        runner.Add("UiText: automatic follows the Windows display language", () =>
        {
            Assert.Equal(UiLanguage.German, UiText.FromCulture(new CultureInfo("de-CH")));
            Assert.Equal(UiLanguage.German, UiText.FromCulture(new CultureInfo("de-DE")));
            Assert.Equal(UiLanguage.English, UiText.FromCulture(new CultureInfo("en-US")));
            Assert.Equal(UiLanguage.English, UiText.FromCulture(new CultureInfo("fr-FR")));
        });

        runner.Add("Language options: both interface languages are offered", () =>
        {
            using var _ = new LanguageScope(UiLanguage.German);
            var codes = LanguageOptions.Interface.Select(o => o.Code).ToArray();

            Assert.True(codes.Contains("auto"), "automatic is missing");
            Assert.True(codes.Contains("de"), "German is missing");
            Assert.True(codes.Contains("en"), "English is missing");
        });

        runner.Add("Language options: German and English variants are selectable", () =>
        {
            using var _ = new LanguageScope(UiLanguage.German);
            var codes = LanguageOptions.Correction.Select(o => o.Code).ToArray();

            Assert.True(codes.Contains("auto"), "automatic detection is missing");
            Assert.True(codes.Contains("de-CH"), "de-CH is missing");
            Assert.True(codes.Contains("de-DE"), "de-DE is missing");
            Assert.True(codes.Contains("en-US"), "en-US is missing");
            Assert.True(codes.Contains("en-GB"), "en-GB is missing");
        });

        runner.Add("Language options: option names follow the interface language", () =>
        {
            using var _ = new LanguageScope(UiLanguage.German);
            Assert.Contains("Automatisch", LanguageOptions.Correction[0].DisplayName);

            UiText.Language = UiLanguage.English;
            Assert.Contains("Automatic", LanguageOptions.Correction[0].DisplayName);
        });

        runner.Add("Language options: automatic detection is the default", () =>
        {
            var settings = new AppSettings();
            Assert.Equal("auto", settings.Language);
            Assert.Equal("de-CH,en-US", settings.PreferredVariants);
            Assert.Equal("auto", settings.InterfaceLanguage);
            Assert.True(LanguageOptions.IsAutomatic(settings.Language));
        });

        runner.Add("Language options: an explicit language is not automatic", () =>
        {
            Assert.False(LanguageOptions.IsAutomatic("de-CH"));
            Assert.False(LanguageOptions.IsAutomatic("en-US"));
            Assert.True(LanguageOptions.IsAutomatic(null));
            Assert.True(LanguageOptions.IsAutomatic(" AUTO "));
        });

        runner.Add("Settings: language choices survive a round trip", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "settings.json");

            new SettingsStore(path).Save(new AppSettings
            {
                InterfaceLanguage = "en",
                Language = "en-GB",
            });

            var read = new SettingsStore(path).Load();
            Assert.Equal("en", read.InterfaceLanguage);
            Assert.Equal("en-GB", read.Language);
        });
    }

    private static IEnumerable<(string Name, string German, string English)> AllTexts()
    {
        var properties = typeof(UiText)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead);

        foreach (var property in properties)
        {
            UiText.Language = UiLanguage.German;
            var german = (string?)property.GetValue(null) ?? "";

            UiText.Language = UiLanguage.English;
            var english = (string?)property.GetValue(null) ?? "";

            yield return (property.Name, german, english);
        }

        UiText.Language = UiLanguage.German;
    }
}

/// <summary>Restores the previous interface language when the test ends.</summary>
internal sealed class LanguageScope : IDisposable
{
    private readonly UiLanguage _previous;

    public LanguageScope(UiLanguage language)
    {
        _previous = UiText.Language;
        UiText.Language = language;
    }

    public void Dispose() => UiText.Language = _previous;
}
