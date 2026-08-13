namespace AutoCorrect.Core.Localization;

/// <summary>An entry of a language drop-down: the stored code plus the text shown to the user.</summary>
public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// The languages offered in the settings dialog. Both the interface and the correction
/// language ship in a single executable and are switched at runtime.
/// </summary>
public static class LanguageOptions
{
    /// <summary>Correction language value that lets LanguageTool detect the language itself.</summary>
    public const string AutomaticDetection = "auto";

    /// <summary>
    /// Variants preferred when detection is automatic. Without them LanguageTool would pick
    /// de-DE and start proposing "ß" for Swiss text.
    /// </summary>
    public const string DefaultPreferredVariants = "de-CH,en-US";

    public static IReadOnlyList<LanguageOption> Interface =>
    [
        new(UiText.AutomaticSetting, UiText.Language == UiLanguage.German
            ? "Automatisch (Systemsprache)"
            : "Automatic (system language)"),
        new("de", "Deutsch"),
        new("en", "English"),
    ];

    public static IReadOnlyList<LanguageOption> Correction =>
    [
        new(AutomaticDetection, UiText.Language == UiLanguage.German
            ? "Automatisch (Deutsch/Englisch)"
            : "Automatic (German/English)"),
        new("de-CH", UiText.Language == UiLanguage.German ? "Deutsch (Schweiz)" : "German (Switzerland)"),
        new("de-DE", UiText.Language == UiLanguage.German ? "Deutsch (Deutschland)" : "German (Germany)"),
        new("de-AT", UiText.Language == UiLanguage.German ? "Deutsch (Österreich)" : "German (Austria)"),
        new("en-US", UiText.Language == UiLanguage.German ? "Englisch (USA)" : "English (US)"),
        new("en-GB", UiText.Language == UiLanguage.German ? "Englisch (UK)" : "English (UK)"),
    ];

    public static bool IsAutomatic(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode) ||
        languageCode.Trim().Equals(AutomaticDetection, StringComparison.OrdinalIgnoreCase);

    /// <summary>Display name for a stored code, falling back to the code itself.</summary>
    public static string DescribeCorrection(string? code)
    {
        var match = Correction.FirstOrDefault(o =>
            string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase));

        return match?.DisplayName ?? code ?? AutomaticDetection;
    }
}
