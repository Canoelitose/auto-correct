# AutoCorrect

Lokales Textkorrektur-Tool für Windows. Markierten Text in **jeder** Anwendung per Hotkey
abgreifen, korrigieren lassen und zurückschreiben. Die Verarbeitung läuft vollständig lokal
bzw. im lokalen Netz – ab Werk gehen keine Daten an externe Dienste.

Wer trotzdem einen gehosteten Dienst nutzen will (stärkere Modelle, kein Download), kann die
Adresse umstellen. Dann ersetzt AutoCorrect vorher Namen, E-Mail-Adressen, Telefonnummern und
IBAN durch Platzhalter und setzt sie im Ergebnis wieder ein:
[docs/SETUP-CLOUD.md](docs/SETUP-CLOUD.md) – inklusive dessen, was diese Maskierung **nicht**
kann.

**Deutsch und Englisch in einer Exe** – Oberfläche umschaltbar, Textsprache wird automatisch
erkannt.

**Status: Phase 1 und Phase 2 fertig.** Bei jedem Push laufen auf einem Windows-Rechner die
Logik-, Sprach- und Windows-Tests, dazu ein Start der fertigen Exe. Phase 2 bringt das lokale
Sprachmodell mit Streaming, die Modi *Umformulieren*, *Förmlich* und *Kürzen* sowie den
Zwischenspeicher. Phase 3 (Fähigkeitsprüfung, Verteilung) ist vorbereitet, aber noch nicht
implementiert.

---

## Download

**[→ Neueste Version herunterladen](https://github.com/Canoelitose/auto-correct/releases/latest)**

| Datei | Grösse | Voraussetzung |
|---|---|---|
| `AutoCorrect-<version>-win-x64.exe` | ca. 64 MB | keine – herunterladen und starten |
| `AutoCorrect-<version>-win-x64-runtime-required.exe` | ca. 2.4 MB | .NET 8 Desktop Runtime |

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

Herunterladen, starten, Text markieren, `Win + Leertaste` – fertig. Die Rechtschreibprüfung
von Windows wird direkt genutzt, es ist **keine Installation nötig**.

LanguageTool und das Sprachmodell sind die optionalen Ausbaustufen. Läuft LanguageTool, wird es
fürs Korrigieren automatisch bevorzugt; das Sprachmodell übernimmt die drei Modi, die den Text
umschreiben.

| Engine | Kann | Aufwand |
|---|---|---|
| LanguageTool | Grammatik und Zeichensetzung nach Regeln, schnell | Java + Server starten |
| Sprachmodell (Ollama) | versteht den Satz: korrigiert, formuliert um, kürzt | Ollama + Modell herunterladen |
| Windows-Rechtschreibprüfung | nur ob ein Wort existiert | keiner, ist in Windows enthalten |

Die Windows-Prüfung allein stösst schnell an ihre Grenze: an `Halo dass ist ein tEst.` findet
sie nichts, weil jedes einzelne Wort existiert. Wer solche Sätze korrigiert haben will, braucht
LanguageTool oder ein Sprachmodell.

Welche Engine geantwortet hat, steht in der Statuszeile des Popups.

### 1. Optional: LanguageTool starten

```powershell
# einmalig herunterladen und entpacken
java -cp "languagetool-server.jar;libs/*" org.languagetool.server.HTTPServer --port 8081
```

Details und ein Autostart-Skript: [docs/SETUP-LANGUAGETOOL.md](docs/SETUP-LANGUAGETOOL.md)

### 2. Optional: Sprachmodell fürs Umformulieren – und für bessere Korrekturen

```powershell
# Ollama von ollama.com installieren, dann einmalig:
ollama pull qwen2.5:3b
```

Der Name muss nicht stimmen: ist ein anderes Modell installiert, wird es automatisch benutzt.
Mehr dazu, inklusive Modellvergleich und llama.cpp: [docs/SETUP-OLLAMA.md](docs/SETUP-OLLAMA.md)

Statt lokal geht auch ein gehosteter Dienst. Bei NVIDIA reicht dafür der API-Schlüssel in den
Einstellungen – Adresse und Modell stellen sich selbst ein. Mit Maskierung der Namen und einer
ehrlichen Liste ihrer Grenzen: [docs/SETUP-CLOUD.md](docs/SETUP-CLOUD.md)

### 3. AutoCorrect bauen und starten

```powershell
dotnet run --project src/AutoCorrect.App
```

AutoCorrect lässt sich auf zwei Arten benutzen:

- **Per Hotkey in jedem Programm** – Text markieren, `Win + Leertaste`, fertig. Das ist der
  schnelle Weg und der eigentliche Zweck.
- **Im eigenen Fenster** – Doppelklick aufs Tray-Symbol öffnet ein Fenster, in das Text
  eingefügt oder getippt wird. Nützlich für längere Texte, zum Ausprobieren und wenn der Text
  nirgends sonst markiert ist.

Die Anwendung läuft als Symbol im Infobereich der Taskleiste. Windows 11 versteckt neue Symbole
hinter dem Pfeil `^` – von dort auf die Taskleiste ziehen, dann bleibt es sichtbar. Das Fenster
zu schliessen beendet das Programm **nicht**; dafür ist *Beenden* im Rechtsklick-Menü da.

### 4. Benutzen

| Taste | Wirkung |
|---|---|
| Doppelklick aufs Tray-Symbol | Fenster zum direkten Bearbeiten öffnen |
| `Win + Leertaste` | Popup mit korrigiertem Text an der Cursorposition |
| `Ctrl + Alt + R` | direkt in den Modus *Umformulieren* |
| `Enter` | Ergebnis übernehmen und in die Ursprungsanwendung einfügen |
| `Esc` | abbrechen und schliessen |
| `Ctrl + C` | Ergebnis nur kopieren |

Rechtsklick auf das Tray-Icon: *Fenster öffnen*, *Einstellungen*, *Über*, *Mit Windows
starten*, *Deinstallieren*, *Beenden*.

---

## Deinstallieren

AutoCorrect ist eine einzelne Exe ohne Installer, es steht also nichts unter *Apps und Features*.

**Der einfache Weg:** Rechtsklick aufs Tray-Symbol → *Deinstallieren …*. Das entfernt
Einstellungen, Protokolle und den Autostart-Eintrag, beendet das Programm und öffnet den Ordner
mit der Exe, damit du sie löschen kannst.

**Von Hand**, falls das Programm nicht mehr startet:

```powershell
Remove-Item "$env:APPDATA\AutoCorrect", "$env:LOCALAPPDATA\AutoCorrect" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name AutoCorrect -ErrorAction SilentlyContinue
```

Danach die `AutoCorrect.exe` löschen. Mehr hinterlässt das Programm nicht – keine Dienste, keine
Treiber, nichts in `Program Files`.

---

## Sprachen

Beides steckt in derselben Exe, es gibt keine getrennten Sprachversionen.

**Oberfläche** – Tray-Icon → *Einstellungen* → *Sprache der Oberfläche*:

| Einstellung | Wirkung |
|---|---|
| Automatisch | folgt der Windows-Anzeigesprache (Standard) |
| Deutsch | deutsche Oberfläche, Schweizer Rechtschreibung (`ss`, kein `ß`) |
| English | englische Oberfläche |

Die Umschaltung wirkt sofort, ohne Neustart.

**Sprache des korrigierten Textes** – *Einstellungen* → *Sprache des Textes*:

| Einstellung | Wirkung |
|---|---|
| Automatisch | erkennt Deutsch und Englisch pro Textstück selbst (Standard) |
| Deutsch (Schweiz/Deutschland/Österreich) | feste Sprache |
| Englisch (USA/UK) | feste Sprache |

Bei *Automatisch* kannst du im selben Arbeitsablauf zwischen deutschen und englischen Texten
wechseln, ohne etwas umzustellen. Die Variantenvorgabe `de-CH,en-US` sorgt dafür, dass
Schweizer Texte nicht plötzlich ein `ß` bekommen.

Gegen LanguageTool 6.6 verifiziert: `Ich habe ein Buch gelest.` → erkannt als `de-CH`,
`I has went to the shop.` → erkannt als `en-US`.

---

## Projektstruktur

```
AutoCorrect.sln
├── src/AutoCorrect.Core/          net8.0, plattformneutral, vollständig testbar
│   ├── Engines/
│   │   ├── ITextEngine.cs         das Interface, gegen das die gesamte UI arbeitet
│   │   ├── EngineRouter.cs        verteilt Modi auf Engines (Ort der Phase-3-Fallbackkette)
│   │   ├── LanguageTool/          LanguageToolEngine, DTOs, CorrectionApplier
│   │   └── Llm/                   LlmEngine (OpenAI-kompatibel, Streaming), Prompts
│   ├── Caching/ResultCache.cs     SQLite-Zwischenspeicher unter %LOCALAPPDATA%
│   ├── Privacy/                   PrivacyMask, MaskRestorer: Namen vor dem Senden ersetzen
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
│   ├── Ui/                        MainWindow, PopupWindow, SettingsWindow, AboutWindow
│   └── Startup/AutoStartManager   Registry-Eintrag HKCU\...\Run
│
├── tests/AutoCorrect.Core.Tests/  Logik-Tests, ohne NuGet, laufen überall
├── tests/AutoCorrect.App.Tests/   Windows-Tests: Tray, Hotkey, Clipboard, Popup, Einfügen
├── build/                         publish.ps1, make-icon.py
└── docs/                          Build, Setup für LanguageTool und Ollama, Testpläne, Architektur
```

---

## Bauen und Testen

```powershell
dotnet build AutoCorrect.sln                        # Core, App und Tests
dotnet run --project tests/AutoCorrect.Core.Tests   # Logik-Tests, Exit-Code 0 = grün
pwsh build/publish.ps1 -Target Both -Test           # beide Verteilpakete
```

Drei Stufen von Tests:

| Stufe | Was | Wo |
|---|---|---|
| Logik | Korrektur-Offsets, Einstellungen, Hotkey-Parsing, Übersetzungen | überall |
| Integration | echter LanguageTool-Server, Deutsch und Englisch | mit `AUTOCORRECT_LT_ENDPOINT` |
| Integration | echtes Sprachmodell: Streaming, Cache, fehlendes Modell | mit `AUTOCORRECT_LLM_ENDPOINT` |
| Windows | Tray, RegisterHotKey inkl. Konflikt, Zwischenablage, Popup, Einfügen | nur Windows |

Die Windows-Stufe startet die Anwendung wirklich und läuft bei jedem Push in GitHub Actions;
sie liest die Auswahl aus einem echten Textfeld und schreibt das Ergebnis zurück.
Was danach noch von Hand zu prüfen ist, steht in [docs/TESTPLAN-PHASE1.md](docs/TESTPLAN-PHASE1.md)
und [docs/TESTPLAN-PHASE2.md](docs/TESTPLAN-PHASE2.md).

Ausführliche Angaben inklusive der gemessenen Paketgrössen: [docs/BUILD.md](docs/BUILD.md)

---

## Beispiele aus einem echten Durchlauf

Korrigieren, gegen LanguageTool 6.6 mit `de-CH`:

```
vorher : Ich habe gestern ein Buch gelest und dan geschlafen.
nachher: Ich habe gestern ein Buch gelesen und dann geschlafen.

vorher : wir treffen uns am montag um 14 uhr im buero.
nachher: Wir treffen uns am Montag um 14 Uhr im Büro.

vorher : Die Strasse ist gross und weiss gestrichen worden.
nachher: Die Strasse ist gross und weiss gestrichen worden.   (Schweizer Schreibung bleibt)
```

Wie *Umformulieren*, *Förmlicher* und *Kürzer* ausfallen, hängt vom gewählten Modell ab –
hier stehen deshalb bewusst keine erfundenen Beispiele. Was das Programm garantiert, ist
geprüft: die Antwort kommt wachsend an, ohne Einleitung, ohne Anführungszeichen um den ganzen
Text, und dieselbe Anfrage wird beim zweiten Mal sofort beantwortet.

---

## Abweichungen von der Vorgabe

Zwei Vorgaben sind mit WPF auf .NET 8 nicht erfüllbar. Beides ist gemessen, nicht geschätzt:

1. **`PublishTrimmed=true`** – das .NET-8-SDK bricht bei WPF ab:
   `error NETSDK1168: WPF is not supported or recommended with trimming enabled`.
   Trimming ist für WPF nicht unterstützt.

2. **Client < 40 MB self-contained** – ohne Trimming ist ein self-contained WPF-Build
   ca. **67 MB**. Unter 40 MB kommt man nur framework-abhängig: dann ist die Exe **2.4 MB**,
   setzt aber die .NET 8 Desktop Runtime auf dem Zielgerät voraus. Beide Varianten sind als
   Publish-Profil hinterlegt.

Von den erlaubten Paketen wird eines benutzt: `Microsoft.Data.Sqlite` für den Zwischenspeicher
aus Phase 2. `System.Text.Json` ist in .NET 8 enthalten, weitere NuGet-Pakete gibt es nicht.

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
- **Kein `ß`:** für deutsche Texte gilt `de-CH`, damit bleibt es bei Schweizer Rechtschreibung.
- **Win+Leertaste** ist ab Werk der Hotkey. Windows benutzt die Kombination sonst für den
  Tastaturlayout-Wechsel; AutoCorrect fängt sie über einen Low-Level-Tastaturhaken ab und
  schluckt sie, der Layout-Wechsel entfällt dadurch. Wer ihn braucht, stellt in den
  Einstellungen z. B. auf `Ctrl+Alt+Leertaste` um.
- Der verarbeitete Text wird **nie** ins Protokoll geschrieben, nur seine Länge. Der
  API-Schlüssel ebenso wenig.
- **Die Namensmaskierung ist Mustererkennung, kein Verständnis.** Ein unbekannter Nachname ohne
  Anrede kann durchrutschen, Adressen und Geburtsdaten werden gar nicht erkannt, und der
  restliche Satz geht unverändert hinaus. Details in
  [docs/SETUP-CLOUD.md](docs/SETUP-CLOUD.md). Der
  Zwischenspeicher unter `%LOCALAPPDATA%\AutoCorrect\cache.db` enthält dagegen naturgemäss
  Klartext; er lässt sich in den Einstellungen jederzeit leeren.
- **Erste Umformulierung nach dem Start:** das Modell muss geladen werden, das dauert je nach
  Rechner 10–60 Sekunden. Das Popup sagt das dann auch. Danach beginnt die Antwort meist in
  unter einer Sekunde.

---

## Konfiguration

`%APPDATA%\AutoCorrect\settings.json` – kann per Skript oder Gruppenrichtlinie vorbelegt werden.

```json
{
  "primaryHotkey": "Win+Space",
  "rephraseHotkey": "Ctrl+Alt+R",
  "startWithWindows": false,
  "languageToolEndpoint": "http://localhost:8081/v2/check",
  "language": "auto",
  "preferredVariants": "de-CH,en-US",
  "interfaceLanguage": "auto",
  "llmEndpoint": "http://localhost:11434/v1",
  "llmModel": "qwen2.5:3b",
  "llmApiKey": "",
  "llmMaskNames": "auto",
  "llmProtectedTerms": [],
  "logLevel": "Warning",
  "maxInputLength": 5000,
  "clipboardWaitMilliseconds": 100,
  "preferUiAutomation": true,
  "requestTimeoutSeconds": 15
}
```

Protokoll: `%LOCALAPPDATA%\AutoCorrect\logs\autocorrect.log` (rotierend, 1 MB, 3 Dateien).
Zwischenspeicher: `%LOCALAPPDATA%\AutoCorrect\cache.db` (SQLite, höchstens 5000 Einträge).
