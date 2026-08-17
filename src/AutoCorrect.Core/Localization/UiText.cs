using System.Globalization;
using AutoCorrect.Core.Engines;

namespace AutoCorrect.Core.Localization;

/// <summary>Languages the user interface is available in.</summary>
public enum UiLanguage
{
    German,
    English,
}

/// <summary>
/// Every user facing string of the application, in German and English. One place, so wording
/// stays consistent and both languages can be reviewed side by side.
///
/// German uses Swiss spelling ("ss" instead of "ß").
///
/// Both languages ship in the same executable; <see cref="Language"/> switches at runtime.
/// </summary>
public static class UiText
{
    /// <summary>Setting value that follows the Windows display language.</summary>
    public const string AutomaticSetting = "auto";

    private static UiLanguage _language = UiLanguage.German;

    /// <summary>Current interface language. Changing it affects every string read afterwards.</summary>
    public static UiLanguage Language
    {
        get => _language;
        set => _language = value;
    }

    /// <summary>Picks German or English from a settings value ("auto", "de", "en").</summary>
    public static UiLanguage Resolve(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "de" or "de-ch" or "de-de" or "de-at" or "german" or "deutsch" => UiLanguage.German,
        "en" or "en-us" or "en-gb" or "english" or "englisch" => UiLanguage.English,
        _ => FromCulture(CultureInfo.CurrentUICulture),
    };

    /// <summary>German for German speaking systems, English everywhere else.</summary>
    public static UiLanguage FromCulture(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.German
            : UiLanguage.English;

    private static string T(string german, string english) =>
        _language == UiLanguage.German ? german : english;

    // ---------------------------------------------------------------- application

    public static string AppName => "AutoCorrect";

    public static string AppTagline => T(
        "Lokale Textkorrektur – die Verarbeitung verlässt das Gerät nicht.",
        "Local text correction – nothing leaves this device.");

    // ---------------------------------------------------------------- tray

    public static string TraySettings => T("Einstellungen", "Settings");

    public static string TrayAbout => T("Über", "About");

    public static string TrayExit => T("Beenden", "Exit");

    public static string TrayUninstall => T("Deinstallieren …", "Uninstall …");

    public static string UninstallConfirm => T(
        "AutoCorrect entfernen?\n\n" +
        "Entfernt werden:\n" +
        "• Einstellungen (%APPDATA%\\AutoCorrect)\n" +
        "• Protokolldateien (%LOCALAPPDATA%\\AutoCorrect)\n" +
        "• der Autostart-Eintrag, falls gesetzt\n\n" +
        "Die Programmdatei selbst müssen Sie danach löschen – AutoCorrect zeigt Ihnen, wo sie " +
        "liegt. Windows und Ihre Texte bleiben unverändert.",
        "Remove AutoCorrect?\n\n" +
        "This removes:\n" +
        "• the settings (%APPDATA%\\AutoCorrect)\n" +
        "• the log files (%LOCALAPPDATA%\\AutoCorrect)\n" +
        "• the autostart entry, if it was set\n\n" +
        "You have to delete the program file yourself afterwards – AutoCorrect will show you " +
        "where it is. Windows and your texts stay untouched.");

    public static string UninstallDone => T(
        "Fertig. AutoCorrect wird jetzt beendet und der Ordner mit der Programmdatei geöffnet – " +
        "löschen Sie dort AutoCorrect.exe.",
        "Done. AutoCorrect will now exit and open the folder holding the program file – delete " +
        "AutoCorrect.exe there.");

    public static string UninstallPartial(string leftover) => T(
        $"Fast fertig. Dieser Ordner liess sich nicht entfernen, weil er noch benutzt wird:\n\n" +
        $"{leftover}\n\n" +
        "Löschen Sie ihn zusammen mit AutoCorrect.exe von Hand, nachdem das Programm beendet ist.",
        $"Almost done. This folder could not be removed because it is still in use:\n\n" +
        $"{leftover}\n\n" +
        "Delete it together with AutoCorrect.exe by hand once the program has exited.");

    public static string TrayStartWithWindows => T("Mit Windows starten", "Start with Windows");

    // ---------------------------------------------------------------- first start

    public static string WelcomeTitle => T(
        "AutoCorrect läuft jetzt",
        "AutoCorrect is running");

    public static string WelcomeIntro => T(
        "AutoCorrect arbeitet auf zwei Arten: in jedem anderen Programm markieren Sie Text und " +
        "drücken den Hotkey – oder Sie öffnen das Fenster von AutoCorrect und arbeiten direkt " +
        "darin. Doppelklick auf das Symbol im Infobereich öffnet es.",
        "AutoCorrect works in two ways: in any other program, select text and press the hotkey – " +
        "or open the AutoCorrect window and work in it directly. Double-click the icon in the " +
        "notification area to open it.");

    public static string WelcomeHotkeyCaption => T(
        "Text markieren, dann drücken:",
        "Select text, then press:");

    public static string WelcomeTrayHint => T(
        "Das Symbol liegt im Infobereich der Taskleiste, rechts unten neben der Uhr. " +
        "Windows 11 versteckt neue Symbole hinter dem Pfeil ^ – klicken Sie darauf und ziehen " +
        "Sie das Symbol nach unten auf die Taskleiste, damit es dauerhaft sichtbar bleibt. " +
        "Doppelklick öffnet das Fenster, Rechtsklick das Menü mit Einstellungen und Beenden.",
        "The icon sits in the notification area next to the clock. Windows 11 hides new icons " +
        "behind the ^ arrow – click it and drag the icon down onto the taskbar to keep it " +
        "visible. Double-click opens the window, right-click the menu with settings and exit.");

    public static string WelcomeClose => T("Alles klar", "Got it");

    public static string AlreadyRunning => T(
        "AutoCorrect läuft bereits.\n\n" +
        "Das Symbol liegt im Infobereich der Taskleiste – Doppelklick öffnet das Fenster. " +
        "Unter Windows 11 steckt es möglicherweise hinter dem Pfeil ^ links neben der Uhr.",
        "AutoCorrect is already running.\n\n" +
        "The icon sits in the notification area – double-click it to open the window. On " +
        "Windows 11 it may be hidden behind the ^ arrow next to the clock.");

    public static string TrayStarted(string hotkey) => T(
        $"Läuft. Text markieren und {hotkey} drücken.",
        $"Running. Select text and press {hotkey}.");

    // ---------------------------------------------------------------- main window

    public static string MainTitle => T("Text bearbeiten", "Work on text");

    public static string MainSubtitle => T(
        "Text einfügen oder tippen, dann einen Modus wählen.",
        "Paste or type text, then pick a mode.");

    public static string MainHotkeyHint(string hotkey) => T(
        $"In anderen Programmen geht es schneller: markieren und {hotkey} drücken.",
        $"In other programs it is quicker: select the text and press {hotkey}.");

    public static string MainInputLabel => T("Ihr Text", "Your text");

    public static string MainResultLabel => T("Ergebnis", "Result");

    public static string MainCopy => T("Kopieren", "Copy");

    public static string MainUseResult => T("Weiterbearbeiten", "Keep working on it");

    public static string MainClear => T("Leeren", "Clear");

    public static string TrayOpenWindow => T("Fenster öffnen", "Open window");

    public static string ClipboardBusy => T(
        "Die Zwischenablage ist gerade von einem anderen Programm belegt. Bitte nochmals versuchen.",
        "Another program is holding the clipboard right now. Please try again.");

    // ---------------------------------------------------------------- capture

    public static string NoSelection => T(
        "Kein markierter Text gefunden.",
        "No selected text found.");

    public static string NoSelectionHint => T(
        "Text markieren und den Hotkey erneut drücken.",
        "Select some text and press the hotkey again.");

    public static string ElevatedWindowHint => T(
        "Läuft das Zielprogramm als Administrator, kann AutoCorrect dessen Text nicht lesen.",
        "AutoCorrect cannot read text from a program that runs as administrator.");

    // ---------------------------------------------------------------- popup

    public static string ModeCorrect => T("Korrigieren", "Correct");

    public static string ModeRephrase => T("Umformulieren", "Rephrase");

    public static string ModeFormal => T("Förmlicher", "More formal");

    public static string ModeShorten => T("Kürzer", "Shorter");

    public static string OriginalLabel => T("Original", "Original");

    public static string StatusWorking => T("Wird verarbeitet …", "Processing …");

    /// <summary>
    /// Shown when nothing has arrived for a few seconds. Worded as a likely cause, not as a
    /// fact: which engine is answering is only known once it does.
    /// </summary>
    public static string StatusModelLoading => T(
        "Dauert länger als üblich – beim ersten Aufruf wird das Sprachmodell geladen",
        "Taking longer than usual – the language model is loaded on the first call");

    public static string StatusDone => T("Fertig", "Done");

    public static string StatusCancelled => T("Abgebrochen", "Cancelled");

    public static string StatusCopied => T("In die Zwischenablage kopiert", "Copied to the clipboard");

    public static string StatusNoChange => T("Keine Korrekturen gefunden", "No corrections found");

    public static string ShortcutHint => T(
        "Enter: Übernehmen · Esc: Abbrechen · Ctrl+C: Kopieren",
        "Enter: Apply · Esc: Cancel · Ctrl+C: Copy");

    // ---------------------------------------------------------------- errors

    public static string LanguageToolUnavailable => T(
        "LanguageTool ist nicht erreichbar.\n\n" +
        "Server starten mit:\n" +
        "java -cp \"languagetool-server.jar;libs/*\" org.languagetool.server.HTTPServer --port 8081\n\n" +
        "Adresse in den Einstellungen prüfen.",
        "LanguageTool is not reachable.\n\n" +
        "Start the server with:\n" +
        "java -cp \"languagetool-server.jar;libs/*\" org.languagetool.server.HTTPServer --port 8081\n\n" +
        "Check the address in the settings.");

    public static string LanguageToolMissingAtStartup => T(
        "LanguageTool läuft nicht. Ohne den lokalen Server kann kein Text korrigiert werden – " +
        "siehe docs/SETUP-LANGUAGETOOL.md.",
        "LanguageTool is not running. Without the local server no text can be corrected – " +
        "see docs/SETUP-LANGUAGETOOL.md.");

    public static string NoSpellCheckLanguage => T(
        "Windows hat für diese Sprache keine Rechtschreibprüfung installiert. " +
        "Unter Einstellungen → Zeit und Sprache → Sprache lässt sie sich nachinstallieren, " +
        "oder Sie richten LanguageTool ein.",
        "Windows has no spell checking installed for this language. It can be added under " +
        "Settings → Time & language → Language, or you can set up LanguageTool.");

    public static string LlmUnavailable => T(
        "Das lokale Sprachmodell ist nicht erreichbar.\n\n" +
        "Ollama von ollama.com installieren, dann einmalig:\n" +
        "ollama pull qwen2.5:3b\n\n" +
        "Ollama läuft danach im Hintergrund. Adresse in den Einstellungen prüfen.",
        "The local language model is not reachable.\n\n" +
        "Install Ollama from ollama.com, then once:\n" +
        "ollama pull qwen2.5:3b\n\n" +
        "Ollama then runs in the background. Check the address in the settings.");

    public static string LlmTimeout => T(
        "Das Sprachmodell hat nicht rechtzeitig geantwortet. Beim ersten Aufruf lädt das Modell, " +
        "das kann eine Weile dauern – bitte nochmals versuchen.",
        "The language model did not answer in time. The first call loads the model, which can " +
        "take a while – please try again.");

    public static string LlmModelMissing(string model) => T(
        $"Es ist kein Sprachmodell installiert.\n\nMit diesem Befehl holen (ca. 2 GB, einmalig):\n" +
        $"ollama pull {model}\n\n" +
        "Ein bereits installiertes Modell wird automatisch verwendet, der Name muss nicht stimmen.",
        $"No language model is installed.\n\nGet one with this command (about 2 GB, once):\n" +
        $"ollama pull {model}\n\n" +
        "A model that is already installed is used automatically, the name does not have to match.");

    /// <summary>Shown in the status line next to "Windows" and "LanguageTool".</summary>
    public static string EngineLlmName => T("Sprachmodell", "Language model");

    public static string LlmNotAuthorised => T(
        "Der Endpunkt hat den Zugang abgelehnt.\n\n" +
        "Bei einem Dienst, der einen Schlüssel verlangt: API-Schlüssel in den Einstellungen " +
        "prüfen. Ollama auf dem eigenen Rechner braucht keinen.",
        "The endpoint refused access.\n\n" +
        "For a service that requires a key: check the API key in the settings. Ollama on your " +
        "own machine needs none.");

    /// <summary>Shown in the popup status line when details were replaced before sending.</summary>
    public static string EngineNamesMasked => T("Namen ersetzt", "names replaced");

    public static string ModeNotSupported => T(
        "Für diesen Modus ist keine Engine verfügbar.",
        "No engine is available for this mode.");

    public static string UnexpectedError => T(
        "Unerwarteter Fehler. Details stehen im Protokoll unter %LOCALAPPDATA%\\AutoCorrect\\logs.",
        "Unexpected error. Details are in the log under %LOCALAPPDATA%\\AutoCorrect\\logs.");

    public static string AutostartFailed => T(
        "Der Autostart-Eintrag konnte nicht geschrieben werden.",
        "The autostart entry could not be written.");

    public static string SettingsNotSaved => T(
        "Die Einstellungen konnten nicht gespeichert werden.",
        "The settings could not be saved.");

    // ---------------------------------------------------------------- settings dialog

    public static string SettingsTitle => T("AutoCorrect – Einstellungen", "AutoCorrect – Settings");

    public static string SettingsHotkeyPrimary => T("Hotkey Korrigieren", "Hotkey for correcting");

    public static string SettingsHotkeyRephrase => T("Hotkey Umformulieren", "Hotkey for rephrasing");

    public static string SettingsHotkeyHint => T(
        "Feld anklicken und die gewünschte Tastenkombination drücken. Standard ist Win+Leertaste.",
        "Click the field and press the key combination you want. The default is Win+Space.");

    public static string SettingsClear => T("Löschen", "Clear");

    public static string SettingsInterfaceLanguage => T("Sprache der Oberfläche", "Interface language");

    public static string SettingsCorrectionLanguage => T("Sprache des Textes", "Language of the text");

    public static string SettingsCorrectionLanguageHint => T(
        "Automatisch erkennt Deutsch und Englisch selbstständig und behält die Schweizer Schreibung bei.",
        "Automatic detects German and English on its own and keeps Swiss spelling.");

    public static string SettingsLanguageTool => T("LanguageTool-Adresse", "LanguageTool address");

    public static string SettingsLanguageToolHint => T(
        "Standard: http://localhost:8081/v2/check",
        "Default: http://localhost:8081/v2/check");

    public static string SettingsLlmEndpoint => T("Adresse des Sprachmodells", "Language model address");

    public static string SettingsLlmEndpointHint => T(
        "Standard: http://localhost:11434/v1 (Ollama). Nötig für Umformulieren, Förmlicher und " +
        "Kürzer; beim Korrigieren verbessert es das Ergebnis, wenn LanguageTool nicht läuft.",
        "Default: http://localhost:11434/v1 (Ollama). Needed for rephrasing, formal wording and " +
        "shortening; for correcting it improves the result when LanguageTool is not running.");

    public static string SettingsLlmModel => T("Modell", "Model");

    public static string SettingsLlmModelHint => T(
        "Empfohlen: qwen2.5:3b (einmalig holen mit: ollama pull qwen2.5:3b). Ist dieses Modell " +
        "nicht da, wird ein anderes installiertes automatisch benutzt.",
        "Recommended: qwen2.5:3b (fetch it once with: ollama pull qwen2.5:3b). If that model is " +
        "missing, another installed one is used automatically.");

    public static string SettingsLlmApiKey => T("API-Schlüssel (optional)", "API key (optional)");

    public static string SettingsLlmApiKeyHint => T(
        "Leer lassen für Ollama auf diesem Rechner. Nötig nur für Endpunkte, die einen Schlüssel " +
        "verlangen. Achtung: ein Dienst im Internet bekommt den markierten Text zu sehen.",
        "Leave empty for Ollama on this machine. Only needed for endpoints that require a key. " +
        "Note: a service on the internet gets to see the selected text.");

    public static string SettingsMaskNames => T("Namen vor dem Senden ersetzen", "Replace names before sending");

    public static string SettingsMaskAuto => T(
        "Automatisch – nur bei Diensten ausserhalb des eigenen Netzes",
        "Automatic – only for services outside your own network");

    public static string SettingsMaskAlways => T("Immer", "Always");

    public static string SettingsMaskNever => T("Nie", "Never");

    public static string SettingsMaskHint => T(
        "Namen, E-Mail-Adressen, Telefonnummern und IBAN werden durch Platzhalter ersetzt und im " +
        "Ergebnis wieder eingesetzt. Erkannt wird nach Mustern, nicht mit Verstand: ein " +
        "unbekannter Nachname ohne Anrede kann durchrutschen. Was gar nicht hinausgehen darf, " +
        "gehört auf ein lokales Modell.",
        "Names, e-mail addresses, phone numbers and IBANs are replaced by stand-ins and put back " +
        "into the result. Detection is by pattern, not by understanding: an unknown surname " +
        "without a salutation can slip through. Anything that must not leave the device belongs " +
        "on a local model.");

    public static string SettingsProtectedTerms => T(
        "Immer ersetzen (ein Wort pro Zeile)",
        "Always replace (one word per line)");

    public static string SettingsProtectedTermsHint => T(
        "Eigener Name, Firma, Projektnamen, Kundennamen – alles, was ein Dienst nie sehen soll.",
        "Your own name, company, project or client names – anything a service should never see.");

    public static string SettingsClearCache => T("Zwischenspeicher leeren", "Clear cache");

    public static string SettingsCacheHint(long entries) => T(
        $"Bereits umformulierte Texte werden lokal gespeichert ({entries} Einträge), damit die " +
        "gleiche Anfrage sofort beantwortet wird.",
        $"Texts that were already rephrased are stored locally ({entries} entries) so the same " +
        "request is answered instantly.");

    public static string SettingsCacheCleared => T("Zwischenspeicher geleert.", "Cache cleared.");

    public static string SettingsUiAutomation => T(
        "Text zuerst über UI Automation lesen (empfohlen)",
        "Read text through UI Automation first (recommended)");

    public static string SettingsUiAutomationHint => T(
        "Ohne UI Automation wird immer über die Zwischenablage gelesen.",
        "Without UI Automation the clipboard is always used.");

    public static string SettingsTestConnection => T("Verbindung testen", "Test connection");

    public static string SettingsSave => T("Speichern", "Save");

    public static string SettingsCancel => T("Abbrechen", "Cancel");

    public static string TestRunning => T("Verbindung wird geprüft …", "Checking the connection …");

    public static string TestOk => T("LanguageTool ist erreichbar.", "LanguageTool is reachable.");

    public static string TestFailed => T(
        "LanguageTool antwortet nicht. Läuft der Server auf dieser Adresse?",
        "LanguageTool does not answer. Is the server running at this address?");

    public static string EndpointInvalid => T(
        "Die LanguageTool-Adresse muss eine vollständige http- oder https-Adresse sein.",
        "The LanguageTool address must be a complete http or https address.");

    public static string LlmEndpointInvalid => T(
        "Die Adresse des Sprachmodells muss eine vollständige http- oder https-Adresse sein.",
        "The language model address must be a complete http or https address.");

    public static string HotkeysIdentical => T(
        "Die beiden Hotkeys dürfen nicht identisch sein.",
        "The two hotkeys must not be the same.");

    // ---------------------------------------------------------------- about dialog

    public static string AboutTitle => T("Über AutoCorrect", "About AutoCorrect");

    public static string AboutClose => T("Schliessen", "Close");

    public static string AboutNoteHeader => T("Hinweis", "Note");

    public static string AboutNoteText => T(
        "Anwendungen, die als Administrator laufen, sind für AutoCorrect nicht lesbar " +
        "(Windows-Sicherheitsgrenze UIPI). Dort bleibt die Auswahl leer.",
        "Applications running as administrator cannot be read by AutoCorrect " +
        "(the Windows UIPI security boundary). The selection stays empty there.");

    public static string AboutEngine => T("Engine", "Engine");

    public static string AboutHotkeyCorrect => T("Hotkey Korrigieren", "Hotkey for correcting");

    public static string AboutHotkeyRephrase => T("Hotkey Umformulieren", "Hotkey for rephrasing");

    public static string AboutNotAssigned => T("nicht belegt", "not assigned");

    public static string AboutLanguage => T("Sprache", "Language");

    public static string AboutSettingsPath => T("Einstellungen", "Settings");

    public static string AboutLogPath => T("Protokoll", "Log");

    // ---------------------------------------------------------------- hotkey validation

    public static string HotkeyRegistrationFailedTitle => T(
        "Hotkey konnte nicht registriert werden",
        "The hotkey could not be registered");

    public static string HotkeyEmpty => T(
        "Es wurde keine Tastenkombination angegeben.",
        "No key combination was given.");

    public static string HotkeyNeedsModifier => T(
        "Die Tastenkombination braucht mindestens eine Zusatztaste (Ctrl, Alt, Shift oder Win).",
        "The key combination needs at least one modifier (Ctrl, Alt, Shift or Win).");

    public static string HotkeyNeedsKey => T(
        "Der Tastenkombination fehlt eine Haupttaste.",
        "The key combination is missing a main key.");

    public static string HotkeyWinSpaceNote => T(
        "Win+Space ist eigentlich für den Windows-Layout-Wechsel belegt. AutoCorrect fängt die " +
        "Kombination über einen Tastaturhaken ab, der Layout-Wechsel entfällt dadurch.",
        "Win+Space is normally used by the Windows layout switcher. AutoCorrect intercepts the " +
        "combination through a keyboard hook, which disables the layout switch.");

    public static string HotkeyRegistrationFailed(string hotkey) => T(
        $"Der Hotkey \"{hotkey}\" ist bereits von einer anderen Anwendung belegt.\n\n" +
        "Bitte in den Einstellungen eine andere Kombination wählen.",
        $"The hotkey \"{hotkey}\" is already taken by another application.\n\n" +
        "Please choose a different combination in the settings.");

    public static string HotkeyUnknownKey(string token) => T(
        $"Unbekannte Taste: \"{token}\".",
        $"Unknown key: \"{token}\".");

    // ---------------------------------------------------------------- formatted messages

    public static string TextTooLong(int length, int max) => T(
        $"Der markierte Text ist mit {length} Zeichen zu lang (Maximum {max}).",
        $"The selected text is too long at {length} characters (maximum {max}).");

    public static string ModeUnavailable(ProcessingMode mode) => T(
        $"{ModeLabel(mode)} benötigt ein lokales Sprachmodell (ab Phase 2).",
        $"{ModeLabel(mode)} needs a local language model (from phase 2 on).");

    public static string ModeFallbackNote(ProcessingMode mode) => T(
        $"{ModeLabel(mode)} ist nicht verfügbar – {ModeCorrect} wird verwendet.",
        $"{ModeLabel(mode)} is not available – {ModeCorrect} is used instead.");

    /// <summary>Button caption for a processing mode.</summary>
    public static string ModeLabel(ProcessingMode mode) => mode switch
    {
        ProcessingMode.Correct => ModeCorrect,
        ProcessingMode.Rephrase => ModeRephrase,
        ProcessingMode.Formal => ModeFormal,
        ProcessingMode.Shorten => ModeShorten,
        _ => mode.ToString(),
    };
}
