# Testplan Phase 1

Die 62 automatischen Tests decken alles ab, was ohne Windows prüfbar ist: Korrektur-Logik,
LanguageTool-Anbindung gegen einen echten HTTP-Server, Einstellungen, Hotkey-Parsing,
Protokollierung. Sie laufen mit `dotnet run --project tests/AutoCorrect.Core.Tests`.

Alles darunter braucht eine Windows-Sitzung mit Maus, Tastatur und Bildschirm. Diese Liste
ist zum Abhaken gedacht.

## Vorbereitung

- [ ] LanguageTool läuft (`http://localhost:8081/v2/languages` antwortet)
- [ ] `dotnet run --project src/AutoCorrect.App` gestartet, Tray-Icon sichtbar

## 1 Tray

| # | Schritt | Erwartet |
|---|---|---|
| 1.1 | Anwendung starten | kein Fenster, nur Tray-Icon |
| 1.2 | Rechtsklick auf das Icon | Menü mit Einstellungen, Über, Mit Windows starten, Beenden |
| 1.3 | Menü offen, daneben klicken | Menü schliesst |
| 1.4 | *Mit Windows starten* anhaken | Wert `AutoCorrect` unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` vorhanden |
| 1.5 | Haken entfernen | Registry-Wert ist wieder weg |
| 1.6 | *Über* | Fenster zeigt Version, Hotkeys, Pfade |
| 1.7 | *Beenden* | Prozess endet, Icon verschwindet sofort |
| 1.8 | Anwendung zweimal starten | die zweite Instanz beendet sich sofort, ein Icon |
| 1.9 | Explorer neu starten (Task-Manager) | Tray-Icon kommt von selbst zurück |

## 2 Hotkey

| # | Schritt | Erwartet |
|---|---|---|
| 2.1 | `Ctrl+Alt+Space` ohne Auswahl | kein Popup, kurzer Tray-Hinweis „Kein markierter Text gefunden." |
| 2.2 | Hotkey in Einstellungen ändern, speichern | neue Kombination wirkt sofort, alte nicht mehr |
| 2.3 | Als Hotkey `Win+Space` eingeben | wird abgelehnt mit Begründung |
| 2.4 | Hotkey ohne Zusatztaste (nur `F5`) | wird abgelehnt |
| 2.5 | Belegte Kombination wählen (z. B. eine, die ein anderes Tool hat) | Meldung, dass sie belegt ist; Einstellungen öffnen sich erneut |
| 2.6 | `Ctrl+Alt+R` | Popup öffnet; in Phase 1 Hinweis, dass *Korrigieren* verwendet wird |

## 3 Text abgreifen

Jeweils einen Satz mit Fehlern markieren, z. B.
`Ich habe gestern ein Buch gelest und dan geschlafen.`

| # | Anwendung | Erwartet |
|---|---|---|
| 3.1 | Notepad | Text erkannt, korrigiert |
| 3.2 | WordPad / Word | Text erkannt (hier greift meist UI Automation) |
| 3.3 | Chrome oder Edge, Textfeld | Text erkannt |
| 3.4 | Outlook, neue Mail | Text erkannt |
| 3.5 | Terminal / PowerShell | Text erkannt (hier greift der Clipboard-Fallback) |
| 3.6 | Anwendung als Administrator gestartet | keine Auswahl, Tray-Hinweis – erwartet, siehe UIPI in der README |

**Wichtig bei jedem Durchgang:** vorher etwas anderes in die Zwischenablage legen und danach
`Ctrl+V` in einem Editor prüfen. Der alte Inhalt muss unverändert zurück sein.

| # | Schritt | Erwartet |
|---|---|---|
| 3.7 | Bild in die Zwischenablage kopieren, dann Text korrigieren | Bild ist nach dem Vorgang noch in der Zwischenablage |
| 3.8 | Text mit mehr als 5000 Zeichen markieren | kein Popup, Tray-Hinweis mit Zeichenzahl |
| 3.9 | Hotkey drücken und `Ctrl+Alt` gedrückt halten | Text wird trotzdem korrekt gelesen (Modifier werden losgelassen) |

## 4 Popup

| # | Schritt | Erwartet |
|---|---|---|
| 4.1 | Hotkey mit Auswahl | Popup erscheint sofort an der Cursorposition, zeigt Ladezustand |
| 4.2 | Popup nahe am rechten Bildschirmrand öffnen | Popup klappt nach links, ragt nicht hinaus |
| 4.3 | Popup nahe am unteren Rand öffnen | Popup klappt nach oben |
| 4.4 | Zweiter Monitor mit anderer Skalierung (z. B. 100 % und 150 %) | Popup sitzt auf beiden Monitoren richtig am Cursor und ist scharf |
| 4.5 | Ergebnis im unteren Feld bearbeiten | Bearbeitung möglich, sobald die Verarbeitung fertig ist |
| 4.6 | `Enter` | Popup schliesst, Text ersetzt die Auswahl in der Ursprungsanwendung |
| 4.7 | `Esc` | Popup schliesst, nichts wird eingefügt |
| 4.8 | `Ctrl+C` ohne Markierung im Ergebnisfeld | Ergebnis in der Zwischenablage, Popup schliesst |
| 4.9 | Teil des Ergebnisses markieren, `Ctrl+C` | nur der markierte Teil wird kopiert, Popup bleibt offen |
| 4.10 | `Shift+Enter` im Ergebnisfeld | Zeilenumbruch, Popup bleibt offen |
| 4.11 | Woanders hinklicken | Popup schliesst |
| 4.12 | Nicht verfügbare Modi | *Umformulieren*, *Förmlicher*, *Kürzer* ausgegraut mit Tooltip |
| 4.13 | Text ohne Fehler korrigieren | Statuszeile zeigt „Keine Korrekturen gefunden" |

## 5 Fehlerfälle

| # | Schritt | Erwartet |
|---|---|---|
| 5.1 | LanguageTool beenden, dann korrigieren | Popup zeigt Fehlermeldung mit dem Startbefehl |
| 5.2 | Falsche Adresse eintragen, *Verbindung testen* | „antwortet nicht" |
| 5.3 | `settings.json` mit Unsinn füllen, Anwendung starten | startet mit Standardwerten, `settings.json.invalid` bleibt liegen |
| 5.4 | `settings.json` löschen, Anwendung starten | startet mit Standardwerten |

## 6 Ressourcen und Datenschutz

| # | Schritt | Erwartet |
|---|---|---|
| 6.1 | Task-Manager, Anwendung im Leerlauf | Arbeitsspeicher im Bereich 40–70 MB, CPU bei 0 % |
| 6.2 | Zeit vom Hotkey bis zum sichtbaren Popup | UI-Automation-Pfad ca. 20–40 ms, Clipboard-Pfad ca. 120–170 ms |
| 6.3 | `%LOCALAPPDATA%\AutoCorrect\logs\autocorrect.log` nach mehreren Korrekturen ansehen | **kein** korrigierter Text, höchstens Angaben wie `<52 chars>` |
| 6.4 | Netzwerkmitschnitt während einer Korrektur (z. B. Fiddler) | nur Verkehr zu `localhost:8081` |

## Was in Phase 1 bewusst fehlt

- *Umformulieren*, *Förmlicher*, *Kürzer* brauchen die LLM-Engine aus Phase 2
- Cache (SQLite), Fähigkeitsprüfung, Fallback-Kette und MSI/MSIX gehören zu Phase 2 und 3
