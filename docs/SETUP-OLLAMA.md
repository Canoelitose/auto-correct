# Lokales Sprachmodell einrichten (Phase 2)

Das Sprachmodell ist für die drei Modi zuständig, die den Text umschreiben – und es verbessert
auch das Korrigieren deutlich:

| Modus | Was er macht |
|---|---|
| Korrigieren | Rechtschreibung, Grammatik, Gross-/Kleinschreibung, Zeichensetzung |
| Umformulieren | gleicher Inhalt, natürlichere Formulierung |
| Förmlich | höflicher und formeller, für Mails und Briefe |
| Kürzen | deutlich kürzer, ohne Inhalt zu verlieren |

**Warum Korrigieren ein Modell braucht.** Nimm den Satz `Halo dass ist ein tEst.` Die
Rechtschreibprüfung von Windows findet daran nichts: `Halo` ist ein deutsches Wort (die
Lichterscheinung), `dass` ist eine Konjunktion, `tEst` ist `Test` mit einem Grossbuchstaben in
der Mitte. Jedes Wort für sich existiert. Erst wer den Satz *versteht*, sieht die Fehler –
und genau das kann ein Sprachmodell. Die Reihenfolge ist deshalb:

| Engine | Kann | Wann sie dran ist |
|---|---|---|
| LanguageTool | Grammatikregeln, schnell und vorhersagbar | wenn der Server läuft |
| Sprachmodell | versteht den Satz, findet auch, was keine Regel abdeckt | wenn Ollama läuft |
| Windows | nur ob ein Wort existiert | immer, ganz ohne Installation |

Alles läuft auf deinem Rechner, es gehen keine Daten an externe Dienste. Wer stattdessen einen
gehosteten Dienst nutzen möchte: [SETUP-CLOUD.md](SETUP-CLOUD.md).

---

## Der schnelle Weg: Ollama

### 1. Ollama installieren

[ollama.com/download](https://ollama.com/download) herunterladen und installieren. Ollama läuft
danach als Dienst im Hintergrund und hört auf `http://localhost:11434`.

### 2. Modell holen

```powershell
ollama pull qwen2.5:3b
```

Das sind knapp 2 GB, einmalig. Danach:

```powershell
ollama run qwen2.5:3b "Formuliere um: Das Meeting ist am Montag."
```

**Der Name muss nicht stimmen.** Hast du schon ein anderes Modell installiert, benutzt
AutoCorrect es einfach – es fragt den Server, was da ist, und nimmt das passendste (bevorzugt
ein kleines, weil es schneller antwortet). Nur wenn gar nichts installiert ist, kommt die
Meldung mit dem `ollama pull`-Befehl.

Kommt eine sinnvolle Antwort, ist alles bereit. AutoCorrect braucht keine weitere Einstellung –
Adresse und Modellname stehen bereits als Standard drin.

### 3. Prüfen

Tray-Icon → *Einstellungen*. Unter *Adresse des Sprachmodells* muss
`http://localhost:11434/v1` stehen, unter *Modell* der Name aus Schritt 2.

Dann Text markieren, `Win + Leertaste`, im Popup auf *Umformulieren* klicken.

---

## Welches Modell?

Gemessen wird, was zählt: wie lange es dauert, bis das erste Wort im Popup steht, und ob das
Ergebnis auf Deutsch brauchbar ist.

| Modell | Grösse | Braucht | Taugt für |
|---|---|---|---|
| `qwen2.5:3b` | ca. 2 GB | ca. 3 GB RAM | Standard. Schnell genug auf einer CPU, Deutsch solide. |
| `qwen2.5:1.5b` | ca. 1 GB | ca. 2 GB RAM | für schwache Rechner, sehr schnell, Qualität etwas schlechter |
| `llama3.2:1b` | ca. 1.3 GB | ca. 2 GB RAM | am schnellsten, reicht fürs Korrigieren, schwächer beim Umformulieren |
| `qwen2.5:7b` | ca. 4.7 GB | ca. 6 GB RAM | spürbar besseres Deutsch, ungefähr doppelte Wartezeit |
| `llama3.1:8b` | ca. 4.9 GB | ca. 6 GB RAM | Alternative, Englisch etwas stärker als Deutsch |

Faustregel: mit Grafikkarte (ab 6 GB VRAM) das 7B-Modell, ohne Grafikkarte das 3B-Modell.
Ollama nutzt eine vorhandene NVIDIA- oder AMD-Karte von selbst.

Nach dem Wechsel kannst du den Namen in den Einstellungen eintragen – musst du aber nicht:
ist der eingetragene Name nicht installiert, wird automatisch ein vorhandenes Modell benutzt.

---

## Achtung: „cloud"-Modelle sind nicht lokal

Ollama bietet inzwischen auch Modelle an, die **nicht** heruntergeladen werden, sondern auf
Ollamas Servern laufen. Erkennbar am Tag `cloud`:

```
kimi-k3:cloud          läuft in Ollamas Rechenzentrum, braucht ein Pro-/Max-Abo
qwen2.5:3b             läuft auf deinem Rechner
```

Der Haken: beide werden über dieselbe Adresse `localhost:11434` angesprochen. Es sieht also
lokal aus, ist es aber nicht – dein Text geht bei einem `:cloud`-Modell hinaus.

AutoCorrect achtet deshalb nicht nur auf die Adresse, sondern auch auf den Modellnamen: bei
einem `:cloud`-Modell greift die Namensmaskierung genauso wie bei einem gehosteten Dienst,
obwohl die Adresse lokal ist. In der Statuszeile steht dann *Namen ersetzt*.

Wenn du sicher lokal bleiben willst, nimm ein Modell **ohne** `:cloud` im Namen. `ollama list`
zeigt, was wirklich auf deinem Rechner liegt.

---

## Warum dauert der erste Aufruf so lange?

Ollama lädt das Modell erst beim ersten Aufruf in den Speicher. Das dauert je nach Rechner
10–60 Sekunden; AutoCorrect wartet dafür bis zu zwei Minuten auf das erste Wort. Danach bleibt
das Modell einige Minuten geladen und die Antwort beginnt in der Regel nach unter einer Sekunde.

Damit das erste Mal nicht in den Arbeitsablauf fällt, kann man Ollama nach dem Anmelden einmal
vorwärmen:

```powershell
# Aufgabenplanung → Aufgabe erstellen → Bei Anmeldung
ollama run qwen2.5:3b "warm" --keepalive 60m
```

`OLLAMA_KEEP_ALIVE=-1` als Umgebungsvariable hält das Modell dauerhaft geladen – bequem, kostet
aber die ganze Zeit den Arbeitsspeicher.

---

## Zwischenspeicher

Was das Modell einmal formuliert hat, landet in
`%LOCALAPPDATA%\AutoCorrect\cache.db`. Dieselbe Anfrage wird danach sofort beantwortet, statt
noch einmal generiert zu werden. Gespeichert werden maximal 5000 Einträge, die ältesten fallen
heraus.

Der Zwischenspeicher enthält deine Texte im Klartext. Er liegt bewusst unter `LOCALAPPDATA` und
nicht unter `APPDATA`, damit er nicht mit einem servergespeicherten Profil mitwandert. Leeren
kannst du ihn in den Einstellungen mit *Zwischenspeicher leeren* oder von Hand:

```powershell
Remove-Item "$env:LOCALAPPDATA\AutoCorrect\cache.db*" -Force
```

---

## Andere Server statt Ollama

AutoCorrect spricht die OpenAI-kompatible Schnittstelle (`POST /v1/chat/completions` mit
`"stream": true`). Alles, was diese Schnittstelle anbietet, funktioniert – nur die Adresse in
den Einstellungen anpassen.

**llama.cpp:**

```powershell
llama-server --model qwen2.5-3b-instruct-q4_k_m.gguf --port 8080 --ctx-size 4096
```

Adresse: `http://localhost:8080/v1`

**LM Studio:** Server starten (Standardport 1234), Adresse `http://localhost:1234/v1`.

**Ein Modell im lokalen Netz**, zum Beispiel auf einem stärkeren Rechner im selben Haushalt:
`http://192.168.1.20:11434/v1`. Das bleibt im Rahmen der Vorgabe – lokal beziehungsweise im
lokalen Netz, nicht bei einem externen Dienst.

---

## Wenn es nicht geht

| Meldung im Popup | Ursache | Abhilfe |
|---|---|---|
| *Das lokale Sprachmodell ist nicht erreichbar* | Ollama läuft nicht | `ollama serve`, oder Ollama neu starten |
| *Es ist kein Sprachmodell installiert* | wirklich keines da | `ollama pull qwen2.5:3b` |
| *Das Sprachmodell hat nicht rechtzeitig geantwortet* | Modell lädt noch | nochmals versuchen, das zweite Mal ist schnell |

Prüfen, ob der Server wirklich antwortet:

```powershell
curl http://localhost:11434/v1/models
```

Weitere Hinweise stehen im Protokoll unter `%LOCALAPPDATA%\AutoCorrect\logs`. Dort steht, was
schiefging – der verarbeitete Text steht dort **nie**, nur seine Länge.
