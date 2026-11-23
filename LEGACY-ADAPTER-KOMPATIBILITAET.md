# Kompatibilität der Legacy-Adapter

Diese Übersicht dokumentiert Legacy-Adapter, deren Admin-Oberfläche mit ioBroker-NewGen geprüft wurde.

| Adapter | Adapter-Version | Runtime-Version | Admin-Version | Status |
| --- | --- | --- | --- | --- |
| `iobroker.javascript` | `10.4.10` | `2.4.0-beta` | `1.3.0` | Kompatibel; Skripte-Tab, Editor, Blockly-Objektauswahl und Skript-States funktionieren |
| `iobroker.wled` | `0.9.2` | `2.4.0-beta` | `1.3.0` | Kompatibel; Konfiguration (Geräte hinzufügen), Objekte und Steuern von States (z. B. `on`, `bri`) funktionieren |
| `iobroker.iot` | `7.1.6` | `2.4.0-beta` | `1.3.0` | Adapter läuft; Alexa-Geräte V3 und die zugehörige Admin-Integration wurden auf Integrität und Bedienbarkeit geprüft |

Für moderne Legacy-Admin-Oberflächen muss der Socket-Client im iframe den vollständigen Adapterpfad verwenden. Bei iot wird die Instanz zusätzlich aus der WebSocket-Route ermittelt, damit `getCurrentInstance` nicht auf `javascript.0` zurückfällt.

## iobroker.iot – aktueller Prüfstand

- Alexa-Geräte V3: Integrität überprüft; Konfiguration, Geräteanzeige und das Hinzufügen eines Geräts funktionieren.
- Adapterbetrieb: Der Adapter startet und läuft mit der Legacy-Adapter-Kompatibilitätsschicht.
- Services und IFTTT: Noch ungetestet.
- Intelligente Raumaufteilung: Noch nicht implementiert.
