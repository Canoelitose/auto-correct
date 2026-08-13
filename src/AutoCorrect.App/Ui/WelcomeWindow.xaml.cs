using System.Windows;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App.Ui;

/// <summary>
/// Shown once, on the very first start.
///
/// Without it the application looks broken: it deliberately has no main window, and Windows 11
/// hides new notification area icons behind the chevron, so a first time user sees nothing at
/// all after double clicking the executable.
/// </summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow(AppSettings settings, bool engineAvailable)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();

        Title = UiText.WelcomeTitle;
        HeadlineText.Text = UiText.WelcomeTitle;
        IntroText.Text = UiText.WelcomeIntro;
        HotkeyCaption.Text = UiText.WelcomeHotkeyCaption;
        HotkeyText.Text = settings.PrimaryHotkeyDefinition.ToString();
        TrayHintText.Text = UiText.WelcomeTrayHint;
        SettingsButton.Content = UiText.TraySettings;
        CloseButton.Content = UiText.WelcomeClose;

        // The engine warning is the other reason a first run disappoints: without a running
        // LanguageTool nothing gets corrected.
        EnginePanel.Visibility = engineAvailable ? Visibility.Collapsed : Visibility.Visible;
        EngineText.Text = UiText.LanguageToolMissingAtStartup;

        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Set when the user asked for the settings dialog instead of just closing.</summary>
    public bool OpenSettingsRequested { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        SettingsButton.Click += (_, _) =>
        {
            OpenSettingsRequested = true;
            Close();
        };
    }
}
