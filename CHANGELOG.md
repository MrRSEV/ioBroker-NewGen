# Changelog

Alle wesentlichen Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
und dieses Projekt folgt sinngemäß [Semantic Versioning](https://semver.org/lang/de/).

## [0.2.0] - 2026-10-05

### Hinzugefügt
- **Core/Bridge** – TCP-Bridge & Node.js-Adapter-Runtime (Sprint 2):
  - `BridgeMessageType` (Handshake, HandshakeAck, Frame, Heartbeat, Error) und `BridgeEnvelope` als kompaktes JSON-Transport-Format.
  - `BridgeProtocol` – Framing mit 4-Byte-Längenpräfix über `NetworkStream`, verhindert Nachrichtenfragmentierung.
  - `BridgeConnection` – kapselt eine einzelne TCP-Verbindung, serialisiert Schreibzugriffe.
  - `TcpBridgeServer` – TCP-Listener mit Handshake (Adapter sendet Namen, Host vergibt ID im Format `Name.id`), bidirektionaler Frame-Transport; eingehende Payloads werden 1:1 auf den `SystemMessageBus` gespiegelt.
  - `NodeAdapterRuntime` – verwaltet isolierte Node.js-Adapterprozesse auf Basis von `RSEV.Utilities.Processes` (`NodeProcess`, `RuntimeProcessController`), inklusive automatischem Crash-Recovery (periodischer Health-Check + Neustart) und Heartbeat-Monitoring.
- **Core/Registry** – `AdapterRegistry`, `AdapterRegistration`, `AdapterStatus`:
  - Thread-sichere zentrale Verwaltung aller Adapter (Custom + ioBroker-Legacy).
  - Automatische, eindeutige ID-Vergabe im Format `Name.id` (z. B. `mqtt.0`, `mqtt.1`).
  - Felder: Name, Id, Type, Status, Metadata, TcpEndpoint (ioBroker), InstanceReference (Custom, ab Sprint 3).
- `AdapterRouter` erweitert um `AttachTcpBridge(...)`: `IoBroker`-Frames werden nun tatsächlich über die TCP-Bridge an den adressierten Adapter zugestellt, statt nur geloggt zu werden (Sprint-1-Fallback).
- `MainRuntimeContext` erweitert um `AdapterRegistry`, `TcpBridge`, `NodeRuntime` sowie die Methoden `StartTcpBridgeAsync`, `InitializeNodeRuntime`, `ShutdownBridgeAndNodeRuntimeAsync`.
- `OnStart`/`OnStop` integrieren den Lifecycle von TCP-Bridge und Node.js-Adapter-Runtime (Port aus `config.toml` ? `ipc.ipcPort`, Fallback 5001).
- DokuWiki-Dokumentation unter `Doku/` erweitert um `core-bridge.txt`, `core-registry.txt`; `architektur.txt`, `core-routing.txt`, `runtime-lifecycle.txt`, `sprintplan.txt`, `start.txt` aktualisiert.

## [0.1.0] - 2026-10-05

### Hinzugefügt
- Grundgerüst des Runtime-Lifecycles (`FirstRun`, `OnStart`, `OnStop`) auf Basis von RSEV.Utilities.
- `MainRuntimeContext` als applikationsweiter Singleton-`RuntimeContext` inkl. eigenem Logger (`SystemLog`, schreibt `latest.log` in das Basisverzeichnis) und Konfigurationsregister (`ConfConfig`).
- `ConfigCreator` zum automatischen Anlegen einer Standard-`config.toml` beim Erststart.
- `ConfigLoader` zum Einlesen der `config.toml` in das RSEV.Utilities-Konfigurationsregister beim Systemstart, inklusive automatischem Zurücksetzen von `firstrun` auf `false`.
- `UnexpectedUserExitHandler` zur sauberen Behandlung von CTRL+C, Konsolen-Schließen sowie Windows-Shutdown-/Logoff-Events mit garantiertem `OnStop`-Aufruf.
- **Core/Matrix** – Semantische Zustands- und Referenzmatrix (`SemanticStateMatrix`):
  - `MatrixObject`, `StateNode`, `HistorySample` als Kernmodelle des Objektbaums.
  - `IEmbeddingService` / `HashingEmbeddingService` für deterministische semantische Embeddings.
  - `IVectorIndex` / `VectorIndex` für semantische Ähnlichkeitssuche (Kosinus-Similarity).
  - `IRoleService` / `DefaultRoleService` für Lese-/Schreibrechte pro Benutzer und State.
  - Vollständige JSONL-Snapshot-Persistenz (`LoadSnapshotAsync`, `WriteSnapshotAsync`, periodischer `StartCronSnapshot`).
  - API: `RegisterObject`, `GetObject`, `GetState`, `SetState`, `Subscribe`/`Unsubscribe`, `SemanticQuery`, `GetHistory`, `DisplayAsJson`.
- **Core/Bus** – Zentraler neutraler Systembus:
  - `BusFrame`-Record (AdapterId, Address, Payload, ValueType, Kind, Timestamp, Metadata).
  - `SystemMessageBus` (basierend auf `RSEV.Utilities.Messaging.BaseMessageBus`) mit thread-sicherem Publish/Subscribe inkl. Wildcard-Abonnements.
- **Core/Routing** – `AdapterRouter`-Skeleton zur Unterscheidung zwischen internem Routing (`CustomAdapter`) und TCP-Bridge-Routing (`IoBrokerAdapter`, folgt in einer späteren Version).
- Registrierung von `SemanticStateMatrix`, `SystemMessageBus` und `AdapterRouter` im `MainRuntimeContext`, Initialisierung beim Systemstart sowie automatisches Schreiben des finalen Snapshots beim Herunterfahren.

[0.2.0]: https://github.com/MrRSEV/ioBroker-NewGen/releases/tag/dev-0.2.0
[0.1.0]: https://github.com/MrRSEV/ioBroker-NewGen/releases/tag/dev-0.1.0
