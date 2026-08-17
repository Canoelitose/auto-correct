# Testplan Phase 2

Automatisiert ist alles, was ohne echtes Modell prüfbar ist, und läuft bei jedem Push:

| Stufe | Was |
|---|---|
| Logik | Streaming, Abbruch, Fehlermeldungen, Filter der Modellantwort, Cache-Schlüssel und Verdrängung |
| Fake-Server | vollständige SSE-Antwort inklusive kaputter Chunks, `[DONE]`, fehlendem Modell |
| Integration | echter Ollama-Server, gesteuert über `AUTOCORRECT_LLM_ENDPOINT` |
| Publish | beide Verteilpakete müssen genau **eine** Datei sein (native SQLite-Bibliothek eingebettet) |

Der Integrationsteil braucht ein Modell und läuft deshalb nicht im CI:

```powershell
ollama pull qwen2.5:3b
$env:AUTOCORRECT_LLM_ENDPOINT = 'http://localhost:11434/v1'
dotnet run --project tests/AutoCorrect.Core.Tests
```

Was hier steht, ist der Rest: alles, wofür ein Mensch mit Maus, Tastatur und Bildschirm vor
echten Fremdanwendungen sitzen muss.

## Vorbereitung

- [ ] Ollama installiert, `ollama list` zeigt mindestens ein Modell
- [ ] AutoCorrect läuft, Tray-Icon sichtbar
- [ ] Zwischenspeicher geleert (Einstellungen → *Zwischenspeicher leeren*)

## 1 Die neuen Modi und die bessere Korrektur

| # | Schritt | Erwartet |
|---|---|---|
| 1.0 | `Halo dass ist ein tEst.` markieren, *Korrigieren* (Ollama läuft, LanguageTool nicht) | `Hallo, das ist ein Test.` – die Windows-Prüfung allein findet daran nichts |
| 1.1 | Satz markieren, `Win + Leertaste`, im Popup auf *Umformulieren* | Text erscheint **wachsend**, nicht auf einen Schlag |
| 1.2 | Statuszeile während der Verarbeitung | `Sprachmodell · Umformulieren · Wird verarbeitet …` |
| 1.3 | Ergebnis mit `Enter` übernehmen | umformulierter Text steht in der Ursprungsanwendung |
| 1.4 | *Förmlicher* auf eine saloppe Nachricht | höflichere Fassung, gleicher Inhalt |
| 1.5 | *Kürzer* auf einen langen Absatz | deutlich kürzer, Kernaussage bleibt |
| 1.6 | *Korrigieren* mit laufendem LanguageTool | sofort da, Statuszeile nennt `LanguageTool` – nicht das Modell |
| 1.7 | Englischen Satz umformulieren | Antwort bleibt englisch |
| 1.8 | Deutschen Satz umformulieren | kein `ß`, Schweizer Schreibung |
| 1.9 | `Ctrl + Alt + R` auf markiertem Text | Popup öffnet direkt im Modus *Umformulieren* |

## 2 Die Antwort ist sauber

Der Punkt, an dem kleine Modelle auffallen: sie kündigen ihre Antwort an oder setzen sie in
Anführungszeichen. Nichts davon darf im Dokument landen.

| # | Schritt | Erwartet |
|---|---|---|
| 2.1 | Zehn verschiedene Sätze umformulieren | nie ein führendes „Hier ist …:" im Ergebnis |
| 2.2 | dieselben Durchläufe | nie Anführungszeichen um den ganzen Text |
| 2.3 | dieselben Durchläufe | nie `---` im Ergebnis |
| 2.4 | Text umformulieren, der **selbst** in Anführungszeichen steht | Anführungszeichen bleiben erhalten |
| 2.5 | Text umformulieren, der mit einer Zeile wie `Wichtig:` beginnt | diese Zeile bleibt erhalten |

## 3 Zwischenspeicher

| # | Schritt | Erwartet |
|---|---|---|
| 3.1 | Denselben Text zweimal umformulieren | beim zweiten Mal sofort da, ohne Wartezeit |
| 3.2 | zweites Ergebnis mit dem ersten vergleichen | identisch |
| 3.3 | Modus wechseln (*Kürzer* statt *Umformulieren*), gleicher Text | wird neu erzeugt, nicht aus dem Speicher |
| 3.4 | Modell in den Einstellungen wechseln, gleicher Text und Modus | wird neu erzeugt |
| 3.5 | Einstellungen öffnen | Hinweis nennt die Anzahl gespeicherter Einträge |
| 3.6 | *Zwischenspeicher leeren*, Einstellungen erneut öffnen | 0 Einträge, Text wird wieder neu erzeugt |
| 3.7 | Während der Verarbeitung `Esc` drücken, danach denselben Text erneut | wird neu erzeugt – ein Abbruch darf kein Bruchstück gespeichert haben |
| 3.8 | AutoCorrect beenden und neu starten, bekannten Text umformulieren | weiterhin sofort da |

## 4 Fehlerfälle

| # | Schritt | Erwartet |
|---|---|---|
| 4.1 | Ollama beenden (`Ollama` im Task-Manager), *Umformulieren* | Meldung nennt `ollama pull qwen2.5:3b` |
| 4.2 | direkt danach nochmals *Umformulieren* | Meldung kommt sofort, kein erneutes Warten auf das Netz |
| 4.3 | *Korrigieren* bei beendetem Ollama | funktioniert unverändert |
| 4.4 | In den Einstellungen einen falschen Modellnamen eintragen | funktioniert trotzdem: das installierte Modell wird benutzt |
| 4.4b | Protokoll danach ansehen | Zeile „Model '…' is not installed; using '…' instead" |
| 4.4c | Alle Modelle entfernen (`ollama rm …`), *Umformulieren* | Meldung „Es ist kein Sprachmodell installiert" mit `ollama pull` |
| 4.5 | Adresse des Sprachmodells auf `keine-adresse` setzen, speichern | Dialog meldet die ungültige Adresse, speichert nicht |
| 4.6 | Ollama starten, ohne das Modell zu laden, dann *Umformulieren* | dauert beim ersten Mal spürbar, kommt dann durch |
| 4.7 | Sehr langen Text (mehrere Absätze) umformulieren | vollständige Antwort, kein Abbruch nach zwei Minuten |
| 4.8 | Während der Verarbeitung den Modus wechseln | laufende Anfrage bricht ab, neue startet |

## 4a Das Fenster

| # | Schritt | Erwartet |
|---|---|---|
| 4a.1 | Doppelklick aufs Tray-Symbol | Fenster öffnet, Cursor steht im Eingabefeld |
| 4a.2 | Text einfügen, *Korrigieren* | Ergebnis erscheint wachsend im unteren Feld |
| 4a.3 | *Weiterbearbeiten* klicken | Ergebnis rutscht nach oben in die Eingabe, unten wird leer |
| 4a.4 | *Kopieren* klicken, in Word einfügen | der korrigierte Text steht dort |
| 4a.5 | Fenster mit dem X schliessen | Programm läuft weiter, Hotkey funktioniert weiterhin |
| 4a.6 | Erneut doppelklicken | Fenster kommt zurück, Inhalt ist noch da |
| 4a.7 | Bei leerer Eingabe | alle vier Modus-Schaltflächen ausgegraut |
| 4a.8 | Während der Verarbeitung: Modus-Schaltflächen | ausgegraut, bis die Antwort fertig ist |
| 4a.9 | Oberflächensprache auf *English* umstellen | Fenster ist danach englisch, ohne Neustart |
| 4a.10 | Tray → *Beenden* bei offenem Fenster | Fenster schliesst, Prozess ist im Task-Manager weg |

## 4b Namensmaskierung (nur bei gehostetem Dienst)

Der Teil, bei dem ein Fehler echten Schaden anrichtet: was hinausgeht, muss stimmen.

| # | Schritt | Erwartet |
|---|---|---|
| 4b.1 | Adresse auf einen gehosteten Dienst stellen, `Anna Meier kommt am Montag.` umformulieren | Statuszeile zeigt `… · Namen ersetzt` |
| 4b.2 | Netzwerkmitschnitt derselben Anfrage ansehen | im gesendeten Text steht **weder** `Anna` **noch** `Meier` |
| 4b.3 | Ergebnis im Popup | die echten Namen stehen wieder drin, nie ein Platzhalter |
| 4b.4 | `Sehr geehrter Herr Brunnenwieser` umformulieren | Nachname wird ersetzt, obwohl er in keiner Liste steht |
| 4b.4a | `Ich habe mit Brunnenwieser gesprochen.` – ohne Anrede | ebenfalls ersetzt: das Windows-Wörterbuch kennt das Wort nicht |
| 4b.4b | `Die Rechnnung liegt auf dem Tisch.` (Tippfehler) | **nicht** ersetzt und korrigiert – ein Tippfehler ist kein Name |
| 4b.4c | `Das PDF der AG im Format A4.` | unverändert – Abkürzungen sind keine Namen |
| 4b.4d | `Herr Koch ruft an.` | *Koch* wird über die Anrede erkannt … |
| 4b.4e | `Koch ruft an.` – ohne Anrede | … allein stehend **nicht**, weil es ein Wörterbuchwort ist. Genau dafür ist *Immer ersetzen* da |
| 4b.5 | Text mit E-Mail, Telefonnummer und IBAN senden | alle drei im Mitschnitt ersetzt |
| 4b.6 | `Im Jahr 2024 kostete es 1500 Franken.` senden | unverändert – Jahreszahlen und Preise sind keine Telefonnummern |
| 4b.7 | Eigenen Begriff unter *Immer ersetzen* eintragen, im Text verwenden | im Mitschnitt ersetzt |
| 4b.8 | *Namen ersetzen* auf *Nie*, gleicher Text | Statuszeile ohne den Zusatz, Namen gehen hinaus |
| 4b.9 | Adresse zurück auf `localhost`, *Automatisch* | keine Maskierung, Namen bleiben im Prompt |
| 4b.10 | Falschen API-Schlüssel eintragen | Meldung „Zugang abgelehnt", **nicht** „Modell nicht installiert" |
| 4b.11 | Protokoll nach allen Versuchen durchsuchen | weder der API-Schlüssel noch ein Klartextname darin |

## 5 Ressourcen und Datenschutz

| # | Schritt | Erwartet |
|---|---|---|
| 5.1 | Task-Manager, AutoCorrect im Leerlauf nach mehreren Umformulierungen | weiterhin im Bereich 40–70 MB |
| 5.2 | `%LOCALAPPDATA%\AutoCorrect\logs\autocorrect.log` ansehen | **kein** verarbeiteter Text, höchstens `<52 chars>` |
| 5.3 | Netzwerkmitschnitt während einer Umformulierung (Standardeinstellungen) | nur Verkehr zu `localhost:11434`, nichts nach aussen |
| 5.4 | `%LOCALAPPDATA%\AutoCorrect\cache.db` | vorhanden; enthält bewusst Klartext, deshalb *lokal* und nicht im Roaming-Profil |
| 5.5 | `%APPDATA%\AutoCorrect` | enthält **keine** Textinhalte, nur `settings.json` |

## 6 Verteilung

| # | Schritt | Erwartet |
|---|---|---|
| 6.1 | Release-Exe herunterladen, Ordner ansehen | genau **eine** Datei, keine `e_sqlite3.dll` daneben |
| 6.2 | Diese Exe auf einem Rechner ohne .NET starten (self-contained) | startet, Tray-Icon erscheint |
| 6.3 | Dort umformulieren, danach nochmals derselbe Text | zweites Mal sofort – der Zwischenspeicher funktioniert auch aus der Einzeldatei |

## Was in Phase 2 bewusst fehlt

- Fähigkeitsprüfung (RAM, CPU-Merkmale, GPU) und die davon abhängige Modellempfehlung
- Server-Engine im lokalen Netz vor dem lokalen Modell
- MSI/MSIX und Signierung
