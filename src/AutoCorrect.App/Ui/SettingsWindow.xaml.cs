using System.Windows;
using System.Windows.Input;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.App.Ui;

/// <summary>
/// Settings dialog. Works on a copy of the settings; the caller persists
/// <see cref="UpdatedSettings"/> when the dialog returns true.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// Availability probe supplied by the composition root. The dialog must not know which
    /// concrete engine is behind it.
    /// </summary>
    private readonly Func<AppSettings, CancellationToken, Task<bool>> _probe;

    private HotkeyDefinition _primaryHotkey;
    private HotkeyDefinition? _rephraseHotkey;

    public SettingsWindow(AppSettings settings, Func<AppSettings, CancellationToken, Task<bool>> probe)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));

        InitializeComponent();

        UpdatedSettings = settings.Clone();
        _primaryHotkey = UpdatedSettings.PrimaryHotkeyDefinition;
        _rephraseHotkey = UpdatedSettings.RephraseHotkeyDefinition;

        PrimaryHotkeyBox.Text = _primaryHotkey.ToString();
        RephraseHotkeyBox.Text = _rephraseHotkey?.ToString() ?? string.Empty;
        EndpointBox.Text = UpdatedSettings.LanguageToolEndpoint;
        LanguageBox.Text = UpdatedSettings.Language;
        AutoStartCheck.IsChecked = UpdatedSettings.StartWithWindows;
        UiAutomationCheck.IsChecked = UpdatedSettings.PreferUiAutomation;

        PrimaryHotkeyBox.PreviewKeyDown += (_, e) => CaptureHotkey(e, isPrimary: true);
        RephraseHotkeyBox.PreviewKeyDown += (_, e) => CaptureHotkey(e, isPrimary: false);
        ClearRephraseButton.Click += (_, _) =>
        {
            _rephraseHotkey = null;
            RephraseHotkeyBox.Text = string.Empty;
            HideError();
        };

        TestButton.Click += async (_, _) => await TestConnectionAsync();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => DialogResult = false;
    }

    /// <summary>The edited copy. Only meaningful when ShowDialog returned true.</summary>
    public AppSettings UpdatedSettings { get; }

    private void CaptureHotkey(KeyEventArgs e, bool isPrimary)
    {
        e.Handled = true;

        // With Alt held down WPF reports Key.System and puts the real key into SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierKey(key))
        {
            return;
        }

        if (key == Key.Escape)
        {
            HideError();
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0)
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        var candidate = new HotkeyDefinition(modifiers, virtualKey);
        var error = candidate.Validate();
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        if (!VirtualKeys.IsKnown(virtualKey))
        {
            ShowError(UiText.HotkeyUnknownKey(key.ToString()));
            return;
        }

        HideError();

        if (isPrimary)
        {
            _primaryHotkey = candidate;
            PrimaryHotkeyBox.Text = candidate.ToString();
        }
        else
        {
            _rephraseHotkey = candidate;
            RephraseHotkeyBox.Text = candidate.ToString();
        }
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System or Key.None;

    private async Task TestConnectionAsync()
    {
        TestButton.IsEnabled = false;
        TestResultText.Text = "Verbindung wird geprüft …";

        try
        {
            // Probe against the address currently in the text box, not the saved one.
            var probeSettings = UpdatedSettings.Clone();
            probeSettings.LanguageToolEndpoint = EndpointBox.Text.Trim();
            probeSettings.Normalize();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var available = await _probe(probeSettings, cts.Token).ConfigureAwait(true);

            TestResultText.Text = available
                ? "LanguageTool ist erreichbar."
                : "LanguageTool antwortet nicht. Läuft der Server auf dieser Adresse?";
        }
        catch (OperationCanceledException)
        {
            TestResultText.Text = "LanguageTool antwortet nicht.";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Save()
    {
        var endpoint = EndpointBox.Text.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowError("Die LanguageTool-Adresse muss eine vollständige http- oder https-Adresse sein.");
            return;
        }

        var primaryError = _primaryHotkey.Validate();
        if (primaryError is not null)
        {
            ShowError(primaryError);
            return;
        }

        if (_rephraseHotkey is { } rephrase && rephrase == _primaryHotkey)
        {
            ShowError("Die beiden Hotkeys dürfen nicht identisch sein.");
            return;
        }

        UpdatedSettings.PrimaryHotkey = _primaryHotkey.ToString();
        UpdatedSettings.RephraseHotkey = _rephraseHotkey?.ToString() ?? string.Empty;
        UpdatedSettings.LanguageToolEndpoint = endpoint;
        UpdatedSettings.Language = LanguageBox.Text.Trim();
        UpdatedSettings.StartWithWindows = AutoStartCheck.IsChecked == true;
        UpdatedSettings.PreferUiAutomation = UiAutomationCheck.IsChecked == true;
        UpdatedSettings.Normalize();

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }
}
