# Cloud-KI statt lokalem Modell

Standard bleibt lokal. Diese Seite beschreibt den anderen Weg: einen gehosteten Dienst wie
NVIDIA Build, der stärkere Modelle anbietet, ohne dass du etwas herunterladen musst.

**Lies zuerst diesen Absatz.** Bei einem gehosteten Dienst verlässt dein markierter Text den
Rechner. AutoCorrect ersetzt vorher Namen, E-Mail-Adressen, Telefonnummern und IBAN durch
Platzhalter (siehe unten), aber der übrige Satz geht so, wie er ist, an den Anbieter. Für
Texte, die den Rechner unter keinen Umständen verlassen dürfen – Arztberichte, Verträge,
Personaldaten – bleibt ein lokales Modell die richtige Wahl.

| | Lokal (Ollama) | Gehostet (z. B. NVIDIA) |
|---|---|---|
| Text verlässt den Rechner | nein | ja, maskiert |
| Vorbereitung | 2 GB Modell laden | Konto und Schlüssel |
| Qualität | ordentlich (3B–8B) | deutlich besser (bis 70B+) |
| Erste Antwort | 10–60 s kalt, danach schnell | immer schnell |
| Kosten | Strom | Gratis-Kontingent, danach kostenpflichtig |
| Ohne Internet | funktioniert | funktioniert nicht |

---

## NVIDIA Build einrichten

### 1. Schlüssel holen

Auf [build.nvidia.com](https://build.nvidia.com) ein Konto anlegen und einen API-Schlüssel
erzeugen. Er beginnt mit `nvapi-`. Kreditkarte ist dafür nicht nötig; das Gratis-Kontingent
umfasst eine begrenzte Zahl von Anfragen pro Minute und insgesamt. Die aktuellen Bedingungen
stehen auf der Seite selbst – sie ändern sich, verlass dich nicht auf diese Zeile.

### 2. In AutoCorrect eintragen

Tray-Icon → *Einstellungen*:

| Feld | Wert |
|---|---|
| Adresse des Sprachmodells | `https://integrate.api.nvidia.com/v1` |
| Modell | z. B. `meta/llama-3.1-8b-instruct` oder `qwen/qwen2.5-7b-instruct` |
| API-Schlüssel | dein `nvapi-…` |
| Namen vor dem Senden ersetzen | *Automatisch* (bleibt so) |

Welche Modellnamen es gibt, steht im Katalog auf build.nvidia.com. Trägst du einen Namen ein,
den es dort nicht gibt, meldet AutoCorrect das – die automatische Auswahl greift nur bei
Servern, die ihre Modelle auflisten.

### 3. Prüfen

Speichern, dann Text markieren und `Win + Leertaste`. In der Statuszeile des Popups muss
`Sprachmodell · … · Namen ersetzt` stehen, sobald ein Name im Text war. Steht dort *Namen
ersetzt* nicht, wurde auch nichts erkannt – siehe die Grenzen weiter unten.

---

## Was ersetzt wird

Vor dem Senden werden ersetzt:

| Was | Erkannt an | Wird zu |
|---|---|---|
| Vorname + Nachname | bekannter Vorname aus einer Liste | `Alex Muster` |
| Name nach Anrede | `Herr`, `Frau`, `Dr.`, `Prof.`, `Mr`, `Mrs` … | `Alex Muster` |
| Eigene Begriffe | deine Liste in den Einstellungen | `Muster` |
| E-Mail-Adresse | `…@….…` | `kontakt1@example.com` |
| Telefonnummer | 7+ Ziffern mit Trennzeichen | `+41 00 000 00 01` |
| IBAN | 2 Buchstaben, 2 Ziffern, 15–34 Zeichen | `CH00 0000 …` |

Im Ergebnis werden die echten Angaben wieder eingesetzt, noch während der Text im Popup
wächst. Du siehst also nie einen Platzhalter.

**Warum echte Namen als Platzhalter und nicht `[NAME1]`.** Ein Klammer-Platzhalter ist für ein
Sprachmodell ein Fremdkörper: es kommentiert ihn, lässt ihn weg, übersetzt ihn oder beugt ihn
falsch – und dann steht er im fertigen Text. `Herr Muster` ist einfach ein Satz. Grammatik,
Fall und Bezüge bleiben heil, und das Zurücksetzen ist eine simple Ersetzung.

---

## Die Grenzen, ehrlich

Das ist Mustererkennung, kein Verständnis. Konkret:

- **Ein unbekannter Nachname ohne Anrede rutscht durch.** „Ich habe mit Brunnenwieser
  gesprochen" bleibt so, wie es dasteht. Genau dafür gibt es das Feld *Immer ersetzen* – trag
  dort ein, was ein Dienst nie sehen soll.
- **Adressen, Geburtsdaten und Ortsnamen werden nicht erkannt.** Nur die Muster aus der Tabelle
  oben.
- **Der übrige Satz geht unverändert hinaus.** Wer im Text steht, ist maskiert; *worum* es geht,
  nicht.
- **Ändert das Modell den Platzhalter, geht der Name verloren.** Formuliert es `Alex Muster` zu
  `Herr Muster` um, kommt beim Zurücksetzen nur der Nachname zurück. Bei Umformulierungen also
  das Ergebnis kurz anschauen, bevor du `Enter` drückst.
- **Das ist keine Anonymisierung im rechtlichen Sinn.** Für Daten mit Schutzbedarf gilt: lokales
  Modell, oder gar nicht.

Wer das nicht will, stellt *Namen vor dem Senden ersetzen* auf **Immer** und trägt seine
festen Begriffe ein – oder bleibt bei Ollama, wo die Frage sich gar nicht stellt.

---

## Andere Anbieter

Alles, was die OpenAI-kompatible Schnittstelle anbietet, funktioniert genauso: Adresse,
Modellname und Schlüssel eintragen. Getestet ist die Schnittstelle selbst, nicht jeder
einzelne Anbieter.

Der Schlüssel steht im Klartext in `%APPDATA%\AutoCorrect\settings.json`, geschützt durch die
Dateirechte deines Windows-Kontos und sonst nichts. Ins Protokoll wird er nie geschrieben, und
in keiner Fehlermeldung taucht er auf.

---

## Zurück zu lokal

Adresse wieder auf `http://localhost:11434/v1` stellen und den Schlüssel leeren. Ab dann geht
nichts mehr hinaus. Der Zwischenspeicher enthält dann noch Antworten aus der Cloud-Zeit – in
den Einstellungen mit *Zwischenspeicher leeren* entfernen, falls das stören sollte.
