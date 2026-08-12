using System.Globalization;
using AutoCorrect.Core.Engines;

namespace AutoCorrect.Core.Localization;

/// <summary>
/// Every user facing string of the application, in German with Swiss spelling
/// ("ss" instead of "ß"). Kept in one place so wording stays consistent and can be
/// reviewed without walking through the UI code.
/// </summary>
public static class UiText
{
    public const string AppName = "AutoCorrect";
    public const string AppTagline = "Lokale Textkorrektur";

    // Tray
    public const string TraySettings = "Einstellungen";
    public const string TrayAbout = "Über";
    public const string TrayExit = "Beenden";
    public const string TrayStartWithWindows = "Mit Windows starten";

    // Capture
    public const string NoSelection = "Kein markierter Text gefunden.";
    public const string NoSelectionHint =
        "Text markieren und den Hotkey erneut drücken.";
    public const string ClipboardBlocked =
        "Die Zwischenablage ist von einer anderen Anwendung blockiert. Bitte erneut versuchen.";
    public const string ElevatedWindowHint =
        "Läuft das Zielprogramm als Administrator, kann AutoCorrect dessen Text nicht lesen.";

    // Popup
    public const string ModeCorrect = "Korrigieren";
    public const string ModeRephrase = "Umformulieren";
    public const string ModeFormal = "Förmlicher";
    public const string ModeShorten = "Kürzer";

    public const string OriginalLabel = "Original";
    public const string ResultLabel = "Ergebnis";
    public const string StatusWorking = "Wird verarbeitet …";
    public const string StatusDone = "Fertig";
    public const string StatusCancelled = "Abgebrochen";
    public const string StatusCopied = "In die Zwischenablage kopiert";
    public const string StatusNoChange = "Keine Korrekturen gefunden";
    public const string ActionApply = "Enter: Übernehmen";
    public const string ActionCancel = "Esc: Abbrechen";
    public const string ActionCopy = "Ctrl+C: Kopieren";

    // Errors
    public const string LanguageToolUnavailable =
        "LanguageTool ist nicht erreichbar.\n\n" +
        "Server starten mit:\n" +
        "java -cp \"languagetool-server.jar:libs/*\" org.languagetool.server.HTTPServer --port 8081\n\n" +
        "Adresse in den Einstellungen prüfen.";

    public const string ModeNotSupported =
        "Für diesen Modus ist keine Engine verfügbar.";

    public const string UnexpectedError =
        "Unerwarteter Fehler. Details stehen im Protokoll unter %LOCALAPPDATA%\\AutoCorrect\\logs.";

    // Settings
    public const string SettingsTitle = "AutoCorrect – Einstellungen";
    public const string SettingsHotkeyPrimary = "Hotkey Korrigieren";
    public const string SettingsHotkeyRephrase = "Hotkey Umformulieren";
    public const string SettingsLanguageTool = "LanguageTool-Adresse";
    public const string SettingsLanguage = "Sprache";
    public const string SettingsSave = "Speichern";
    public const string SettingsCancel = "Abbrechen";
    public const string SettingsHotkeyHint =
        "Feld anklicken und die gewünschte Tastenkombination drücken.";

    public const string HotkeyRegistrationFailedTitle = "Hotkey konnte nicht registriert werden";

    public static string HotkeyRegistrationFailed(string hotkey) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Der Hotkey \"{0}\" ist bereits von einer anderen Anwendung belegt.\n\n" +
            "Bitte in den Einstellungen eine andere Kombination wählen.",
            hotkey);

    public static string TextTooLong(int length, int max) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Der markierte Text ist mit {0} Zeichen zu lang (Maximum {1}).",
            length,
            max);

    // Hotkey validation
    public const string HotkeyEmpty = "Es wurde keine Tastenkombination angegeben.";
    public const string HotkeyNeedsModifier =
        "Die Tastenkombination braucht mindestens eine Zusatztaste (Ctrl, Alt, Shift oder Win).";
    public const string HotkeyNeedsKey = "Der Tastenkombination fehlt eine Haupttaste.";
    public const string HotkeyWinSpaceReserved =
        "Win+Space ist von Windows für den Tastaturlayout-Wechsel belegt und kann nicht verwendet werden.";

    public static string HotkeyUnknownKey(string token) =>
        string.Format(CultureInfo.InvariantCulture, "Unbekannte Taste: \"{0}\".", token);

    /// <summary>German button caption for a processing mode.</summary>
    public static string ModeLabel(ProcessingMode mode) => mode switch
    {
        ProcessingMode.Correct => ModeCorrect,
        ProcessingMode.Rephrase => ModeRephrase,
        ProcessingMode.Formal => ModeFormal,
        ProcessingMode.Shorten => ModeShorten,
        _ => mode.ToString(),
    };
}
