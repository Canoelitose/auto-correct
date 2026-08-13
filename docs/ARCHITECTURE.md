# Architektur

## Zwei Projekte, eine Grenze

```
AutoCorrect.Core   net8.0           kein Windows, keine UI, vollständig testbar
AutoCorrect.App    net8.0-windows   WPF, P/Invoke, alles Betriebssystemnahe
```

Die Trennung ist nicht kosmetisch. Alles, was fachlich falsch sein kann – Offsets von
Korrekturen, Parsen von Hotkeys, Umgang mit einer beschädigten Konfigurationsdatei, Rotation
des Protokolls – liegt in `Core` und wird von 62 Tests abgedeckt, die auf jedem Rechner
laufen. In `App` bleibt der Teil, der ohne Windows-Sitzung ohnehin nicht prüfbar ist.

## Der Ablauf einer Korrektur

```
WM_HOTKEY  →  HotkeyManager
              │
              ├─ GetForegroundWindow()          Zielfenster merken
              │
              ├─ SelectionCapture
              │   ├─ 1. UI Automation           TextPattern.GetSelection(), Budget 120 ms
              │   └─ 2. Clipboard-Fallback      sichern → leeren → Ctrl+C → warten → lesen → zurück
              │
              ├─ leer?      → Tray-Hinweis, kein Popup
              ├─ > 5000 Z.? → Tray-Hinweis, kein Popup
              │
              └─ PopupWindow.StartSession()
                  ├─ positionieren, anzeigen, fokussieren      ← hier ist das Popup sichtbar
                  └─ ITextEngine.ProcessAsync()                ← erst jetzt beginnt die Arbeit
                      └─ Chunks → Puffer → DispatcherTimer alle 30 ms → TextBox

Enter → TextInjector.InsertAsync()
        Clipboard sichern → Ergebnis setzen → Zielfenster nach vorn → Ctrl+V
        → 500 ms → Clipboard zurück
```

## ITextEngine

```csharp
public interface ITextEngine
{
    string Name { get; }
    bool SupportsMode(ProcessingMode mode);
    Task<bool> IsAvailableAsync(CancellationToken ct);
    IAsyncEnumerable<string> ProcessAsync(string input, ProcessingMode mode, CancellationToken ct);
}
```

Die UI kennt ausschliesslich dieses Interface. Konkrete Engines tauchen an genau einer Stelle
auf: `src/AutoCorrect.App/Engines/EngineFactory.cs`. Selbst der Knopf *Verbindung testen* im
Einstellungsdialog bekommt seine Prüffunktion als Delegat übergeben, damit der Dialog keine
LanguageTool-spezifische Zeile enthält.

`EngineRouter` ist selbst eine `ITextEngine`. Er verteilt jeden Modus an die erste Engine, die
ihn unterstützt. Damit ist die Fallback-Kette aus Phase 3 (Server → lokales Modell → nur
Korrektur) eine Frage der Reihenfolge, nicht eine Frage von Änderungen an der UI.

## Warum die Korrekturen von hinten nach vorne angewendet werden

LanguageTool liefert je Fund einen Offset in das **ursprüngliche** Textstück. Wird der erste
Fund ersetzt und ist der Ersatz länger oder kürzer, verschieben sich alle folgenden Offsets.
`CorrectionApplier` sortiert deshalb absteigend nach Offset und arbeitet von hinten nach vorne;
dabei bleiben alle noch nicht verarbeiteten Offsets gültig. Überlappende Funde werden
verworfen, statt zwei widersprüchliche Vorschläge zu vermischen.

## Zwei Fallen im Ablauf, die im Code festgehalten sind

**Wartezeit nach `Ctrl+C`.** Ohne sie liest man den vorherigen Inhalt der Zwischenablage. Die
Wartezeit ist als Einstellung mit 80 ms Untergrenze hinterlegt, damit sie nicht versehentlich
wegoptimiert wird.

**Win+Leertaste geht nicht über RegisterHotKey.** Die Shell besitzt die Kombination für den
Layout-Wechsel, `RegisterHotKey` scheitert dort. `HotkeyManager` weicht deshalb auf einen
`WH_KEYBOARD_LL`-Haken aus, der vor der Shell sitzt, die Kombination abfängt und mit Rückgabe 1
schluckt. Zwei Regeln dabei: Der Callback muss sofort zurückkehren, sonst entfernt Windows den
Haken – die Arbeit wird nur auf den Dispatcher gelegt. Und weil die Shell die geschluckte Taste
nie sieht, würde sie beim Loslassen der Windows-Taste das Startmenü öffnen; ein eingeschobener
Ctrl-Tipp verhindert das.

**Noch gedrückte Zusatztasten.** Wenn `Ctrl+Alt+Space` auslöst, hält der Benutzer meistens noch
`Ctrl+Alt`. Ein danach gesendetes `Ctrl+C` käme in der Zielanwendung als `Ctrl+Alt+C` an.
`InputSimulator.ReleaseHeldModifiers()` schickt deshalb zuerst Key-Up für alles, was laut
`GetAsyncKeyState` noch unten ist.

## Wo Phase 2 und 3 andocken

| Vorhaben | Ort |
|---|---|
| LLM-Engine mit Streaming | neue `ITextEngine` in `Core/Engines/`, Registrierung in `EngineFactory` |
| Token-Puffer im UI | steht schon: `PopupWindow`, 30-ms-`DispatcherTimer` |
| Abbruch bei Moduswechsel | steht schon: `CancelRunning()` in `PopupWindow` |
| Prompts als Konstanten | neue Klasse neben `Core/Localization/UiText.cs` |
| SQLite-Cache | neuer Dekorator um `ITextEngine`, dann greift er für jede Engine |
| Fähigkeitsprüfung | neue Klasse in `App`, Ergebnis steuert die Reihenfolge im `EngineRouter` |
| Fallback-Anzeige in der Statuszeile | `PopupWindow.SetStatus()` zeigt bereits `Engine · Modus · Zustand` |

## Regeln, die im Code eingehalten werden

- Jeder P/Invoke steht in `Interop/NativeMethods.cs`, nirgends sonst.
- Kein `.Result`, kein `.Wait()`; die einzige Wartestelle im UI-Thread ist `await Task.Delay`.
- Fehler werden dem Benutzer gezeigt, nicht nur protokolliert: fehlende Engine und
  Hotkey-Konflikte erscheinen im Popup bzw. als Meldung, nicht nur im Protokoll.
- Der verarbeitete Text wird nie protokolliert. `Log.Describe()` erzeugt `<n chars>` und wird
  überall dort verwendet, wo sonst der Text stünde.
