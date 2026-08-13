using System.Reflection;
using System.Text;
using System.Windows;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App.Ui;

public partial class AboutWindow : Window
{
    public AboutWindow(AppSettings settings, string engineName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        Title = UiText.AboutTitle;
        VersionText.Text = $"Version {version}";
        TaglineText.Text = UiText.AppTagline;
        NoteHeader.Text = UiText.AboutNoteHeader;
        NoteText.Text = UiText.AboutNoteText;
        CloseButton.Content = UiText.AboutClose;

        var rephrase = string.IsNullOrWhiteSpace(settings.RephraseHotkey)
            ? UiText.AboutNotAssigned
            : settings.RephraseHotkey;

        var details = new StringBuilder()
            .AppendLine($"{UiText.AboutEngine}: {engineName}")
            .AppendLine($"{UiText.AboutHotkeyCorrect}: {settings.PrimaryHotkey}")
            .AppendLine($"{UiText.AboutHotkeyRephrase}: {rephrase}")
            .AppendLine($"LanguageTool: {settings.LanguageToolEndpoint}")
            .AppendLine($"{UiText.AboutLanguage}: {LanguageOptions.DescribeCorrection(settings.Language)}")
            .AppendLine()
            .AppendLine($"{UiText.AboutSettingsPath}: {SettingsStore.DefaultFilePath}")
            .Append($"{UiText.AboutLogPath}: {Log.DefaultFilePath}");

        DetailsText.Text = details.ToString();

        CloseButton.Click += (_, _) => Close();
    }
}
