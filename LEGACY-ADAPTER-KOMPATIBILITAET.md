# Kompatibilität der Legacy-Adapter

Diese Übersicht dokumentiert Legacy-Adapter, deren Admin-Oberfläche mit ioBroker-NewGen geprüft wurde.

| Adapter | Adapter-Version | Runtime-Version | Admin-Version | Status |
| --- | --- | --- | --- | --- |
| `iobroker.javascript` | `10.4.10` | `2.4.0-beta` | `1.3.0` | Kompatibel; Skripte-Tab, Editor, Blockly-Objektauswahl und Skript-States funktionieren |
| `iobroker.wled` | `0.9.2` | `2.4.0-beta` | `1.3.0` | Kompatibel; Konfiguration (Geräte hinzufügen), Objekte und Steuern von States (z. B. `on`, `bri`) funktionieren |
| `iobroker.iot` | `7.1.6` | `2.4.0-beta` | `1.3.0` | Nicht kompatibel; Adapter bricht beim Start ab (`Cannot read properties of undefined (reading 'bind')`) |
