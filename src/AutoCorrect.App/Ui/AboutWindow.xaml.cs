using System.Reflection;
using System.Text;
using System.Windows;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Ui;

public partial class AboutWindow : Window
{
    public AboutWindow(AppSettings settings, string engineName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version}";

        var details = new StringBuilder()
            .AppendLine($"Engine: {engineName}")
            .AppendLine($"Hotkey Korrigieren: {settings.PrimaryHotkey}")
            .AppendLine($"Hotkey Umformulieren: {(string.IsNullOrWhiteSpace(settings.RephraseHotkey) ? "nicht belegt" : settings.RephraseHotkey)}")
            .AppendLine($"LanguageTool: {settings.LanguageToolEndpoint}")
            .AppendLine($"Sprache: {settings.Language}")
            .AppendLine()
            .AppendLine($"Einstellungen: {SettingsStore.DefaultFilePath}")
            .Append($"Protokoll: {Log.DefaultFilePath}");

        DetailsText.Text = details.ToString();

        CloseButton.Click += (_, _) => Close();
    }
}
