# LanguageTool lokal betreiben

AutoCorrect spricht in Phase 1 ausschliesslich mit einem lokal laufenden
`languagetool-standalone`. Es geht nichts ins Internet.

## Installation

1. Java 17 oder neuer installieren (`winget install EclipseAdoptium.Temurin.17.JRE`).
2. `LanguageTool-stable.zip` von <https://languagetool.org/download/> herunterladen.
3. Entpacken, zum Beispiel nach `C:\Tools\LanguageTool`.

## Starten

```powershell
cd C:\Tools\LanguageTool
java -cp "languagetool-server.jar;libs/*" org.languagetool.server.HTTPServer --port 8081
```

Prüfen, ob der Dienst läuft:

```powershell
Invoke-RestMethod -Uri "http://localhost:8081/v2/languages" | Select-Object -First 3
```

Ein schneller Funktionstest mit Schweizer Rechtschreibung:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:8081/v2/check" `
  -Body @{ text = "Ich habe gestern ein Buch gelest."; language = "de-CH" }
```

## Automatisch mitstarten

Als Aufgabe im Aufgabenplaner, ohne Konsolenfenster:

```powershell
$action = New-ScheduledTaskAction -Execute "javaw.exe" `
    -Argument '-cp "C:\Tools\LanguageTool\languagetool-server.jar;C:\Tools\LanguageTool\libs\*" org.languagetool.server.HTTPServer --port 8081' `
    -WorkingDirectory "C:\Tools\LanguageTool"

$trigger = New-ScheduledTaskTrigger -AtLogOn

Register-ScheduledTask -TaskName "LanguageTool Server" `
    -Action $action -Trigger $trigger -Description "Lokaler LanguageTool-Server für AutoCorrect"
```

## Speicherbedarf

Der Server belegt je nach geladenen Sprachen 500 MB bis 1 GB RAM. Wer nur Deutsch braucht,
schränkt das mit `--languageModel` bzw. über die Konfigurationsdatei ein; für den Standardfall
ist keine Anpassung nötig.

## Andere Adresse verwenden

Läuft der Server auf einem anderen Port oder auf einem Rechner im LAN, wird die Adresse in den
AutoCorrect-Einstellungen eingetragen (Tray-Icon → *Einstellungen* → *LanguageTool-Adresse*).
Der Knopf *Verbindung testen* prüft sie sofort.

Für den Betrieb im LAN muss der Server mit `--public --allow-origin "*"` gestartet werden. Das
ist nur innerhalb eines vertrauenswürdigen Netzes sinnvoll: der Dienst kennt keine
Authentifizierung, und jeder Text, der geprüft wird, geht dann über das Netz.

## Fehlermeldung im Popup

Ist der Dienst nicht erreichbar, zeigt AutoCorrect im Popup den Startbefehl an. Häufigste
Ursachen:

| Symptom | Ursache |
|---|---|
| „nicht erreichbar" direkt beim Öffnen | Server läuft nicht oder anderer Port |
| Antwort dauert und läuft in den Timeout | Server startet gerade noch (erster Start dauert einige Sekunden) |
| Antwort kommt, aber ohne Korrekturen | Sprache passt nicht zum Text – Einstellung `language` prüfen |
