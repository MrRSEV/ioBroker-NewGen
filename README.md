# ioBroker-NewGen

> Preview auf ein hier entstehendes Projekt namens **ioBroker-NewGen**.
>
> Aktueller Zustand: **In Entwicklung**.

## Kurzbeschreibung

**ioBroker-NewGen** ist eine modernisierte, stabile und objektorientierte Smarthome-Matrix als langfristige Weiterentwicklung des ioBroker-Ökosystems.
Ziel ist eine klare Trennung zwischen Kernlogik, Adapter-Runtime, Routing und Benutzeroberfläche.

## Ziele

### Primäre Ziele
- Ersetzen der historischen ioBroker-Architektur durch eine moderne C#-Zustandsmatrix
- Isolierte Node.js-Runtimes für Legacy-Adapter
- Einheitliche Adapter-API für alle Adaptertypen
- Stabile, objektorientierte Echtzeit-State-Sync-Engine
- Neues Admin-UI in C#
- Langfristige Migration beliebter ioBroker-Adapter nach C#

### Sekundäre Ziele
- Kompatibilität zu bestehenden ioBroker-Adaptern, maximal 10 gleichzeitig
- Minimale Hardware-Anforderungen für Raspberry Pi 5
- Saubere Trennung zwischen internem und externem Routing
- Modernes, modulares und erweiterbares System

## Architektur

### Kernkomponenten
- **StateMatrix**: objektorientierte, thread-sichere Single Source of Truth
- **SystemBus**: neutraler Datenbus ohne Kenntnis des Adaptertyps
- **AdapterRouter**: entscheidet zwischen internem Transport und TCP-Bridge
- **AdminUI**: C#-basierte Oberfläche für Objektbaum, Live-State, Adapter-Management und Logs

### Adapter-Modell
- **IAdapter** als gemeinsame Schnittstelle mit `Name`, `Id`, `Type`, `SendPayload` und `ReceivePayload`
- **CustomAdapter**: C#-Assembly, host-intern, direkte Systembus-Nutzung
- **IoBrokerAdapter**: isolierter Node.js-Prozess, Anbindung über TCP-Bridge

### State-Layer
- Direkter Zugriff auf die Matrix
- Validiertes Schreiben in die Matrix
- Metadaten aus Adapter-Konstruktoren wie `min`, `max`, `type`, `role`, `unit`
- Optionale Subscriptions über Event-Bus für Makro-Runtimes

### Objektbaum
- Der Objektbaum wird aus der Matrix abgeleitet
- Darstellung als JSON möglich
- Deterministisch, stabil und vollständig inklusive Metadaten

### Rules Engine
- Makro-Runtimes auf Basis von JavaScript und TypeScript
- Event-getriebene Ausführung über den Event-Bus
- Sandbox mit Zeitlimit, Memory-Limit, Zugriffsbeschränkungen und Crash-Recovery

### Weitere Bausteine
- **History**: Adapter-seitige Historienverarbeitung über `OnStateChanged`
- **Config System**: Laden, Speichern und optionale Validierung pro Adapter
- **User/Role/Permission System**: Auth, Rollen, Rechte, Tokens und Sessions
- **Backup System**: Snapshots, Dumps und optionale Cloud-Backups

## Hardware-Zielbild

### Minimal
- 4 Kerne ARM oder x86
- 8 GB RAM
- 64–128 GB SSD
- Pi-5-kompatibel bei maximal 10 ioBroker-Adaptern

### Empfohlen
- 4–6 x86-Kerne
- 16 GB RAM
- 128–256 GB SSD

### High-End
- 8–12 Kerne
- 32–64 GB RAM
- 256–512 GB NVMe

## Migrationsstrategie

1. Kritische Adapter migrieren
2. Mittlere Adapter migrieren
3. Kleine Adapter migrieren
4. ioBroker-Adapter nur noch als Legacy-Feature anbieten

## Vision

Ein modernes, stabiles, objektorientiertes Smarthome-System, das ioBroker langfristig ersetzt, aber kompatibel bleibt.

Nicht als Konkurrenz zu Home Assistant, sondern als moderne Alternative für Entwickler und Power-User.

## Core Classes

- **Matrix**: zentrale Speicherstruktur für Datenpunkte, Geräteobjekte und Metadaten
- **BusFrame**: einheitliches Frame-Format für alle Adapter
- **EventBus**: zentrales Event-Routing
- **StateEngine**: API-Schicht über der Matrix
- **ObjectTree**: abgeleitete hierarchische Darstellung aus der Matrix
- **MacroRuntime**: Ausführung von JS/TS-Makros
- **AdapterLoader**: Laden und Instanziieren von Adapter-Assemblies
- **AdapterSupervisor**: Überwachung und Stabilisierung der Adapter
- **AuthEngine**: User-, Rollen- und Rechteverwaltung
- **BackupEngine**: Sicherung und Wiederherstellung des Systems

## MIT-Lizenz

The MIT License (MIT)

Copyright (c) 2014-2026 bluefox <dogafox@gmail.com>,
Copyright (c) 2014      hobbyquaker
Copyright (c) 2026      MrRSEV - Richard Schumacher

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
