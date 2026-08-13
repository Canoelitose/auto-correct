# Bauen, Testen, Verteilen

## Voraussetzungen

- .NET 8 SDK
- Windows 10 (1809+) oder Windows 11 zum **Ausführen**

Zum **Bauen** genügt auch Linux oder macOS: das Projekt setzt `EnableWindowsTargeting=true`,
damit `dotnet build` und `dotnet publish` für `win-x64` auch in einem Container laufen. Das ist
für CI nützlich; getestet werden kann die Anwendung nur unter Windows.

## Befehle

```powershell
dotnet build AutoCorrect.sln
dotnet run --project tests/AutoCorrect.Core.Tests    # Logik- und Sprachtests
dotnet run --project src/AutoCorrect.App             # startet die Anwendung
```

### Windows-Tests

```powershell
dotnet build AutoCorrect.sln -c Release
tests/AutoCorrect.App.Tests/bin/Release/net8.0-windows/AutoCorrect.App.Tests.exe
```

Diese Stufe braucht eine echte Windows-Sitzung und prüft, was sonst nur von Hand prüfbar wäre:
Registry-Autostart, verstecktes Nachrichtenfenster, `RegisterHotKey` samt Konfliktfall,
Tray-Icon, Zwischenablage sichern und wiederherstellen, das Popup mit geladenem XAML sowie den
kompletten Weg Auswahl lesen → Ergebnis einfügen. Tests, die einen interaktiven Desktop
brauchen, melden sich als übersprungen, statt aus dem falschen Grund fehlzuschlagen.

Beides läuft bei jedem Push in GitHub Actions auf `windows-latest`, inklusive eines echten
LanguageTool-Servers und eines Startversuchs der fertigen Exe.

Der Testlauf hat keine NuGet-Abhängigkeit; der Runner liegt in
`tests/AutoCorrect.Core.Tests/TestRunner.cs` und gibt bei einem Fehlschlag Exit-Code 1 zurück.
Einzelne Tests filtern: `dotnet run --project tests/AutoCorrect.Core.Tests -- Hotkey`

### Tests gegen einen echten LanguageTool-Server

Die normalen Tests laufen ohne Netz gegen einen eingebauten Fake-Server. Zusätzlich gibt es
sieben Integrationstests, die einen echten `languagetool-standalone` ansprechen. Sie werden nur
registriert, wenn die Umgebungsvariable gesetzt ist:

```powershell
$env:AUTOCORRECT_LT_ENDPOINT = "http://localhost:8081/v2/check"
dotnet run --project tests/AutoCorrect.Core.Tests
```

Sie prüfen unter anderem, dass `de-CH` kein `ß` einführt und dass die Offsets auch bei langen
Texten mit vielen Korrekturen ausgerichtet bleiben. Verifiziert gegen LanguageTool 6.6.

## Verteilpakete

```powershell
pwsh build/publish.ps1 -Target Both -Test
```

| Variante | Grösse (gemessen) | Voraussetzung auf dem Zielgerät |
|---|---|---|
| `client-framework-dependent` | **0.4 MB**, eine Exe | .NET 8 Desktop Runtime |
| `client-self-contained` | **68 MB** | keine |

Die Runtime lässt sich per `winget install Microsoft.DotNet.DesktopRuntime.8` oder über
Intune verteilen. In einer verwalteten Umgebung ist die framework-abhängige Variante fast
immer die bessere Wahl: die Runtime wird einmal verteilt, der Client bleibt bei 0.4 MB.

## Entscheidungen

### PublishTrimmed ist bei WPF nicht möglich

Die Vorgabe nennt `PublishTrimmed=true`. Das .NET-8-SDK lehnt das für WPF ab:

```
error NETSDK1168: WPF is not supported or recommended with trimming enabled.
Please go to https://aka.ms/dotnet-illink/wpf for more details.
```

Es gibt eine undokumentierte Möglichkeit, den Fehler zu unterdrücken, aber WPF lädt Typen
über XAML per Reflexion; ein getrimmter Build fällt dann erst zur Laufzeit auseinander, oft
erst in einem selten benutzten Dialog. Deshalb ist Trimming hier nicht gesetzt.

**Folge für die Zielgrösse:** die geforderten < 40 MB sind self-contained nicht erreichbar.
Framework-abhängig sind es 0.4 MB, also weit darunter.

### Kein Windows Forms für das Tray-Icon

`NotifyIcon` aus Windows Forms wäre der kurze Weg, zieht aber die komplette
WinForms-Bibliothek in den Prozess. Das Tray-Icon ist stattdessen direkt gegen
`Shell_NotifyIcon` implementiert (`src/AutoCorrect.App/Tray/`). Das spart Speicher im
Leerlauf und hält die Abhängigkeiten bei „nur WPF".

### Keine externen NuGet-Pakete

Der gesamte Code kommt mit dem aus, was in .NET 8 enthalten ist. `System.Text.Json` ist
in-box, `Microsoft.Win32.Registry` ebenfalls. `Microsoft.Data.Sqlite` wird erst in Phase 2
für den Cache gebraucht und ist noch nicht referenziert.

### Positionierung über SetWindowPos statt Window.Left/Top

`Window.Left` und `Window.Top` rechnen in geräteunabhängigen Einheiten, `GetCursorPos`
liefert physische Pixel. Auf gemischten DPI-Setups gehen die beiden auseinander. Das Popup
wird deshalb über `SetWindowPos` mit physischen Koordinaten gesetzt
(`src/AutoCorrect.App/Ui/ScreenPlacement.cs`), die Skalierung kommt aus `GetDpiForMonitor`
des jeweiligen Monitors.

## ARM64

`build/publish.ps1 -Runtime win-arm64` baut ein ARM64-Paket. Für Phase 1 ist das vollständig
nutzbar, weil LanguageTool auf der JVM läuft und keine AVX2-Instruktionen braucht. Erst
Phase 3 muss unterscheiden (ARM nutzt NEON statt AVX2).

## MSI / MSIX

Noch nicht umgesetzt, gehört zu Phase 3.3. Vorbereitet ist:

- Die Einstellungen liegen als JSON in `%APPDATA%`, damit sie per Gruppenrichtlinie oder
  Skript vorbelegt werden können.
- Der Autostart läuft über `HKCU\...\Run` und braucht keine Administratorrechte.
- Client und Modell sind bewusst getrennt: das Modellpaket (~2.5 GB) kommt in Phase 3 als
  eigenes Paket, damit es nur auf geeignete Geräte verteilt wird.
