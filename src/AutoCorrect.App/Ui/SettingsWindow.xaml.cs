using System.Windows;
using System.Windows.Input;
using AutoCorrect.Core.Caching;
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

    /// <summary>Shared with the running engines, so clearing takes effect immediately.</summary>
    private readonly ResultCache? _cache;

    private HotkeyDefinition _primaryHotkey;
    private HotkeyDefinition? _rephraseHotkey;

    public SettingsWindow(
        AppSettings settings,
        Func<AppSettings, CancellationToken, Task<bool>> probe,
        ResultCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _cache = cache;

        InitializeComponent();

        UpdatedSettings = settings.Clone();
        _primaryHotkey = UpdatedSettings.PrimaryHotkeyDefinition;
        _rephraseHotkey = UpdatedSettings.RephraseHotkeyDefinition;

        ApplyTexts();

        PrimaryHotkeyBox.Text = _primaryHotkey.ToString();
        RephraseHotkeyBox.Text = _rephraseHotkey?.ToString() ?? string.Empty;
        EndpointBox.Text = UpdatedSettings.LanguageToolEndpoint;
        LlmEndpointBox.Text = UpdatedSettings.LlmEndpoint;
        LlmModelBox.Text = UpdatedSettings.LlmModel;
        LlmApiKeyBox.Password = UpdatedSettings.LlmApiKey;
        ProtectedTermsBox.Text = string.Join(Environment.NewLine, UpdatedSettings.LlmProtectedTerms);
        FillMaskBox();
        AutoStartCheck.IsChecked = UpdatedSettings.StartWithWindows;
        UiAutomationCheck.IsChecked = UpdatedSettings.PreferUiAutomation;

        FillLanguageBoxes();

        PrimaryHotkeyBox.PreviewKeyDown += (_, e) => CaptureHotkey(e, isPrimary: true);
        RephraseHotkeyBox.PreviewKeyDown += (_, e) => CaptureHotkey(e, isPrimary: false);
        ClearRephraseButton.Click += (_, _) =>
        {
            _rephraseHotkey = null;
            RephraseHotkeyBox.Text = string.Empty;
            HideError();
        };

        // Switching the interface language updates the dialog immediately, so the effect of
        // the setting is visible before saving.
        InterfaceLanguageBox.SelectionChanged += (_, _) => OnInterfaceLanguageChanged();

        // The first control takes focus when the window opens, and the scroll viewer then
        // brings it into view - which pushes the heading above it out of sight.
        Loaded += (_, _) => FormScroll.ScrollToTop();

        ClearCacheButton.Click += (_, _) =>
        {
            _cache?.Clear();
            CacheHint.Text = UiText.SettingsCacheCleared;
        };

        TestButton.Click += async (_, _) => await TestConnectionAsync();
        SaveButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => DialogResult = false;
    }

    /// <summary>The edited copy. Only meaningful when ShowDialog returned true.</summary>
    public AppSettings UpdatedSettings { get; }

    private void ApplyTexts()
    {
        Title = UiText.SettingsTitle;
        InterfaceLanguageLabel.Text = UiText.SettingsInterfaceLanguage;
        CorrectionLanguageLabel.Text = UiText.SettingsCorrectionLanguage;
        CorrectionLanguageHint.Text = UiText.SettingsCorrectionLanguageHint;
        PrimaryHotkeyLabel.Text = UiText.SettingsHotkeyPrimary;
        HotkeyHint.Text = UiText.SettingsHotkeyHint;
        RephraseHotkeyLabel.Text = UiText.SettingsHotkeyRephrase;
        ClearRephraseButton.Content = UiText.SettingsClear;
        EndpointLabel.Text = UiText.SettingsLanguageTool;
        EndpointHint.Text = UiText.SettingsLanguageToolHint;
        LlmEndpointLabel.Text = UiText.SettingsLlmEndpoint;
        LlmEndpointHint.Text = UiText.SettingsLlmEndpointHint;
        LlmModelLabel.Text = UiText.SettingsLlmModel;
        LlmModelHint.Text = UiText.SettingsLlmModelHint;
        LlmApiKeyLabel.Text = UiText.SettingsLlmApiKey;
        LlmApiKeyHint.Text = UiText.SettingsLlmApiKeyHint;
        MaskNamesLabel.Text = UiText.SettingsMaskNames;
        MaskNamesHint.Text = UiText.SettingsMaskHint;
        ProtectedTermsLabel.Text = UiText.SettingsProtectedTerms;
        ProtectedTermsHint.Text = UiText.SettingsProtectedTermsHint;
        FillMaskBox();
        ClearCacheButton.Content = UiText.SettingsClearCache;
        CacheHint.Text = UiText.SettingsCacheHint(_cache?.Count() ?? 0);
        AutoStartCheck.Content = UiText.TrayStartWithWindows;
        UiAutomationCheck.Content = UiText.SettingsUiAutomation;
        UiAutomationHint.Text = UiText.SettingsUiAutomationHint;
        TestButton.Content = UiText.SettingsTestConnection;
        SaveButton.Content = UiText.SettingsSave;
        CancelButton.Content = UiText.SettingsCancel;
    }

    private void FillLanguageBoxes()
    {
        Select(InterfaceLanguageBox, LanguageOptions.Interface, UpdatedSettings.InterfaceLanguage);
        Select(CorrectionLanguageBox, LanguageOptions.Correction, UpdatedSettings.Language);

        static void Select(
            System.Windows.Controls.ComboBox box,
            IReadOnlyList<LanguageOption> options,
            string? current)
        {
            box.ItemsSource = options;
            box.SelectedItem =
                options.FirstOrDefault(o => string.Equals(o.Code, current, StringComparison.OrdinalIgnoreCase))
                ?? options[0];
        }
    }

    private void OnInterfaceLanguageChanged()
    {
        if (InterfaceLanguageBox.SelectedItem is not LanguageOption option)
        {
            return;
        }

        if (string.Equals(option.Code, UpdatedSettings.InterfaceLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        UpdatedSettings.InterfaceLanguage = option.Code;
        UiText.Language = UiText.Resolve(option.Code);

        // The drop-down entries carry translated names as well, so both lists are rebuilt.
        var correction = (CorrectionLanguageBox.SelectedItem as LanguageOption)?.Code;
        ApplyTexts();
        UpdatedSettings.Language = correction ?? UpdatedSettings.Language;
        FillLanguageBoxes();
    }

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

        if (candidate.IsReservedByWindows)
        {
            // Not an error: the keyboard hook takes over, but the user should know the side effect.
            TestResultText.Text = UiText.HotkeyWinSpaceNote;
        }

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
        TestResultText.Text = UiText.TestRunning;

        try
        {
            // Probe against the address currently in the text box, not the saved one.
            var probeSettings = UpdatedSettings.Clone();
            probeSettings.LanguageToolEndpoint = EndpointBox.Text.Trim();
            probeSettings.Normalize();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var available = await _probe(probeSettings, cts.Token).ConfigureAwait(true);

            TestResultText.Text = available ? UiText.TestOk : UiText.TestFailed;
        }
        catch (OperationCanceledException)
        {
            TestResultText.Text = UiText.TestFailed;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Save()
    {
        var endpoint = EndpointBox.Text.Trim();
        if (!IsHttpAddress(endpoint))
        {
            ShowError(UiText.EndpointInvalid);
            return;
        }

        var llmEndpoint = LlmEndpointBox.Text.Trim();
        if (!IsHttpAddress(llmEndpoint))
        {
            ShowError(UiText.LlmEndpointInvalid);
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
            ShowError(UiText.HotkeysIdentical);
            return;
        }

        UpdatedSettings.PrimaryHotkey = _primaryHotkey.ToString();
        UpdatedSettings.RephraseHotkey = _rephraseHotkey?.ToString() ?? string.Empty;
        UpdatedSettings.LanguageToolEndpoint = endpoint;
        UpdatedSettings.LlmEndpoint = llmEndpoint;
        UpdatedSettings.LlmModel = LlmModelBox.Text.Trim();
        UpdatedSettings.LlmApiKey = LlmApiKeyBox.Password.Trim();
        UpdatedSettings.LlmMaskNames = SelectedMaskSetting();
        UpdatedSettings.LlmProtectedTerms = ProtectedTermsBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        UpdatedSettings.StartWithWindows = AutoStartCheck.IsChecked == true;
        UpdatedSettings.PreferUiAutomation = UiAutomationCheck.IsChecked == true;

        if (InterfaceLanguageBox.SelectedItem is LanguageOption interfaceLanguage)
        {
            UpdatedSettings.InterfaceLanguage = interfaceLanguage.Code;
        }

        if (CorrectionLanguageBox.SelectedItem is LanguageOption correctionLanguage)
        {
            UpdatedSettings.Language = correctionLanguage.Code;
        }

        UpdatedSettings.Normalize();

        DialogResult = true;
    }

    /// <summary>
    /// The three masking choices, with their setting value carried alongside the label so the
    /// interface language can change without losing the selection.
    /// </summary>
    private sealed record MaskOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private void FillMaskBox()
    {
        var current = SelectedMaskSetting();

        var options = new[]
        {
            new MaskOption(AppSettings.MaskNamesAuto, UiText.SettingsMaskAuto),
            new MaskOption(AppSettings.MaskNamesAlways, UiText.SettingsMaskAlways),
            new MaskOption(AppSettings.MaskNamesNever, UiText.SettingsMaskNever),
        };

        MaskNamesBox.ItemsSource = options;
        MaskNamesBox.SelectedItem =
            options.FirstOrDefault(o => string.Equals(o.Value, current, StringComparison.OrdinalIgnoreCase))
            ?? options[0];
    }

    private string SelectedMaskSetting() =>
        MaskNamesBox.SelectedItem is MaskOption option ? option.Value : UpdatedSettings.LlmMaskNames;

    private static bool IsHttpAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

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
