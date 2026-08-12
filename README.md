# AutoCorrect

Lokales Textkorrektur-Tool für Windows. Markierten Text in **jeder** Anwendung per Hotkey
abgreifen, korrigieren lassen und zurückschreiben. Die Verarbeitung läuft vollständig lokal
bzw. im lokalen Netz – es gehen keine Daten an externe Dienste.

**Status: Phase 1 fertig und lauffähig.** Phase 2 (lokales Sprachmodell mit Streaming) und
Phase 3 (Fähigkeitsprüfung, Verteilung) sind vorbereitet, aber noch nicht implementiert.

---

## Download

Fertige Exe unter [Releases](https://github.com/Canoelitose/auto-correct/releases):

| Datei | Grösse | Voraussetzung |
|---|---|---|
| `AutoCorrect-<version>-win-x64.exe` | ca. 63 MB | keine – herunterladen und starten |
| `AutoCorrect-<version>-win-x64-runtime-required.exe` | ca. 0.4 MB | .NET 8 Desktop Runtime |

Windows 10 (1809+) oder Windows 11, x64. Die Exe ist nicht signiert, deshalb meldet sich
SmartScreen beim ersten Start: *Weitere Informationen* → *Trotzdem ausführen*.

Ein neues Release baut und veröffentlicht GitHub Actions (`.github/workflows/release.yml`).
Drei Wege, alle mit dem gleichen Ergebnis:

```bash
git tag v1.0.1 && git push origin v1.0.1        # Tag pushen
git push origin HEAD:release/v1.0.1             # Branch pushen, der Workflow legt den Tag an
```

oder in GitHub unter *Actions* → *Release* → *Run workflow* die Version eintippen.

---

## Schnellstart

### 1. LanguageTool starten

```powershell
# einmalig herunterladen und entpacken
java -cp "languagetool-server.jar;libs/*" org.languagetool.server.HTTPServer --port 8081
```

Details und ein Autostart-Skript: [docs/SETUP-LANGUAGETOOL.md](docs/SETUP-LANGUAGETOOL.md)

### 2. AutoCorrect bauen und starten

```powershell
dotnet run --project src/AutoCorrect.App
```

Die Anwendung startet ohne Fenster, nur als Icon im System-Tray.

### 3. Benutzen

| Taste | Wirkung |
|---|---|
| `Ctrl + Alt + Space` | Popup mit korrigiertem Text an der Cursorposition |
| `Ctrl + Alt + R` | direkt in den Modus *Umformulieren* (ab Phase 2) |
| `Enter` | Ergebnis übernehmen und in die Ursprungsanwendung einfügen |
| `Esc` | abbrechen und schliessen |
| `Ctrl + C` | Ergebnis nur kopieren |

Rechtsklick auf das Tray-Icon: *Einstellungen*, *Über*, *Mit Windows starten*, *Beenden*.

---

## Projektstruktur

```
AutoCorrect.sln
├── src/AutoCorrect.Core/          net8.0, plattformneutral, vollständig testbar
│   ├── Engines/
│   │   ├── ITextEngine.cs         das Interface, gegen das die gesamte UI arbeitet
│   │   ├── EngineRouter.cs        verteilt Modi auf Engines (Ort der Phase-3-Fallbackkette)
│   │   └── LanguageTool/          LanguageToolEngine, DTOs, CorrectionApplier
│   ├── Configuration/             AppSettings, SettingsStore, HotkeyDefinition, VirtualKeys
│   ├── Diagnostics/               FileLogger (rotierend), Log
│   └── Localization/UiText.cs     alle deutschen Texte an einer Stelle
│
├── src/AutoCorrect.App/           net8.0-windows, WPF
│   ├── App.xaml(.cs)              Start, Single-Instance, globale Fehlerbehandlung
│   ├── AppController.cs           verdrahtet Tray, Hotkeys, Capture und Popup
│   ├── Engines/EngineFactory.cs   Composition Root, einziger Ort mit konkreten Engines
│   ├── Interop/                   NativeMethods (alle P/Invoke), MessageWindow, InputSimulator
│   ├── Hotkeys/HotkeyManager.cs   RegisterHotKey inklusive Konfliktmeldung
│   ├── Capture/                   UI Automation, Clipboard-Fallback, Einfügen
│   ├── Tray/                      Shell_NotifyIcon-Tray ohne WinForms-Abhängigkeit
│   ├── Ui/                        PopupWindow, SettingsWindow, AboutWindow, ScreenPlacement
│   └── Startup/AutoStartManager   Registry-Eintrag HKCU\...\Run
│
├── tests/AutoCorrect.Core.Tests/  62 Tests offline, 69 mit LanguageTool-Server, ohne NuGet
├── build/                         publish.ps1, make-icon.py
└── docs/                          Build, LanguageTool-Setup, Testplan, Architektur
```

---

## Bauen und Testen

```powershell
dotnet build AutoCorrect.sln          # Core, App und Tests
dotnet run --project tests/AutoCorrect.Core.Tests   # 62 Tests, Exit-Code 0 = grün
pwsh build/publish.ps1 -Target Both -Test           # beide Verteilpakete
```

Mit gesetzter Umgebungsvariable `AUTOCORRECT_LT_ENDPOINT` kommen sieben Integrationstests
gegen einen echten LanguageTool-Server dazu (gegen LanguageTool 6.6 verifiziert).
Was nur von Hand prüfbar ist, steht in [docs/TESTPLAN-PHASE1.md](docs/TESTPLAN-PHASE1.md).

Ausführliche Angaben inklusive der gemessenen Paketgrössen: [docs/BUILD.md](docs/BUILD.md)

---

## Beispiele aus einem echten Durchlauf

Gegen LanguageTool 6.6 mit `de-CH`:

```
vorher : Ich habe gestern ein Buch gelest und dan geschlafen.
nachher: Ich habe gestern ein Buch gelesen und dann geschlafen.

vorher : wir treffen uns am montag um 14 uhr im buero.
nachher: Wir treffen uns am Montag um 14 Uhr im Büro.

vorher : Die Strasse ist gross und weiss gestrichen worden.
nachher: Die Strasse ist gross und weiss gestrichen worden.   (Schweizer Schreibung bleibt)
```

---

## Abweichungen von der Vorgabe

Zwei Vorgaben sind mit WPF auf .NET 8 nicht erfüllbar. Beides ist gemessen, nicht geschätzt:

1. **`PublishTrimmed=true`** – das .NET-8-SDK bricht bei WPF ab:
   `error NETSDK1168: WPF is not supported or recommended with trimming enabled`.
   Trimming ist für WPF nicht unterstützt.

2. **Client < 40 MB self-contained** – ohne Trimming ist ein self-contained WPF-Build
   ca. **68 MB** (62 MB Executable plus native WPF-Bibliotheken). Unter 40 MB kommt man
   nur framework-abhängig: dann ist die Exe **0.4 MB**, setzt aber die .NET 8 Desktop
   Runtime auf dem Zielgerät voraus. Beide Varianten sind als Publish-Profil hinterlegt.

Weitere bewusste Entscheidungen sind in [docs/BUILD.md](docs/BUILD.md#entscheidungen) und
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) begründet.

---

## Bekannte Grenzen

- **Anwendungen mit erhöhten Rechten** sind für ein normal laufendes Tool unsichtbar (UIPI).
  Weder UI Automation noch der simulierte `Ctrl+C` erreichen sie. Das ist eine
  Windows-Sicherheitsgrenze und wird dokumentiert, nicht umgangen.
- **Popup-Zeit:** über UI Automation ist das Popup nach ca. 20–40 ms sichtbar. Wenn die
  Anwendung kein `TextPattern` unterstützt, greift der Clipboard-Fallback, dessen Wartezeit
  von 80–120 ms technisch notwendig ist; dann sind es ca. 120–170 ms.
- **Kein `ß`:** Sprache steht fest auf `de-CH`, LanguageTool liefert damit Schweizer
  Rechtschreibung.
- Der verarbeitete Text wird **nie** ins Protokoll geschrieben, nur seine Länge.

---

## Konfiguration

`%APPDATA%\AutoCorrect\settings.json` – kann per Skript oder Gruppenrichtlinie vorbelegt werden.

```json
{
  "primaryHotkey": "Ctrl+Alt+Space",
  "rephraseHotkey": "Ctrl+Alt+R",
  "startWithWindows": false,
  "languageToolEndpoint": "http://localhost:8081/v2/check",
  "language": "de-CH",
  "llmEndpoint": "http://localhost:11434/v1",
  "llmModel": "qwen2.5:3b-instruct-q4_K_M",
  "logLevel": "Warning",
  "maxInputLength": 5000,
  "clipboardWaitMilliseconds": 100,
  "preferUiAutomation": true,
  "requestTimeoutSeconds": 15
}
```

Protokoll: `%LOCALAPPDATA%\AutoCorrect\logs\autocorrect.log` (rotierend, 1 MB, 3 Dateien).
