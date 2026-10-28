# Changelog

All significant changes to this project are documented in this file.

The format follows the principles of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project loosely adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-10-28

### Added
- Central adapter lifecycle logging in `MainRuntimeContext`: every adapter start (`Connected`), real stop (`Connected` → `Disconnected`) and error state (`Faulted`) is written to the log, for both custom C# adapters and legacy Node.js adapters (including restarts after crashes).
- Admin dashboard is now also served at `/` and `/index.html` (in addition to `/admin`).
- Integration tests cover the new dashboard routes.
- Custom adapter runtime with dependency-ordered start/stop, adapter deployment/installer, backup service, adapter migration catalog, release candidate builder, native adapters (MQTT, Zigbee, Hue, Shelly, FritzBox, Bosch, KNX, Modbus, Homematic, Alexa) and the external AdminUI assembly.

### Fixed
- `http://127.0.0.1:8080/` returned `404 Not Found` because the dashboard was only registered at `/admin`.

## [0.2.0] - 2026-10-05

### Hinzugef�gt
- **Core/Bridge** � TCP-Bridge & Node.js-Adapter-Runtime (Sprint 2):
  - `BridgeMessageType` (Handshake, HandshakeAck, Frame, Heartbeat, Error) und `BridgeEnvelope` als kompaktes JSON-Transport-Format.
  - `BridgeProtocol` � Framing mit 4-Byte-L�ngenpr�fix �ber `NetworkStream`, verhindert Nachrichtenfragmentierung.
  - `BridgeConnection` � kapselt eine einzelne TCP-Verbindung, serialisiert Schreibzugriffe.
  - `TcpBridgeServer` � TCP-Listener mit Handshake (Adapter sendet Namen, Host vergibt ID im Format `Name.id`), bidirektionaler Frame-Transport; eingehende Payloads werden 1:1 auf den `SystemMessageBus` gespiegelt.
  - `NodeAdapterRuntime` � verwaltet isolierte Node.js-Adapterprozesse auf Basis von `RSEV.Utilities.Processes` (`NodeProcess`, `RuntimeProcessController`), inklusive automatischem Crash-Recovery (periodischer Health-Check + Neustart) und Heartbeat-Monitoring.
- **Core/Registry** � `AdapterRegistry`, `AdapterRegistration`, `AdapterStatus`:
  - Thread-sichere zentrale Verwaltung aller Adapter (Custom + ioBroker-Legacy).
  - Automatische, eindeutige ID-Vergabe im Format `Name.id` (z. B. `mqtt.0`, `mqtt.1`).
  - Felder: Name, Id, Type, Status, Metadata, TcpEndpoint (ioBroker), InstanceReference (Custom, ab Sprint 3).
- `AdapterRouter` erweitert um `AttachTcpBridge(...)`: `IoBroker`-Frames werden nun tats�chlich �ber die TCP-Bridge an den adressierten Adapter zugestellt, statt nur geloggt zu werden (Sprint-1-Fallback).
- `MainRuntimeContext` erweitert um `AdapterRegistry`, `TcpBridge`, `NodeRuntime` sowie die Methoden `StartTcpBridgeAsync`, `InitializeNodeRuntime`, `ShutdownBridgeAndNodeRuntimeAsync`.
- `OnStart`/`OnStop` integrieren den Lifecycle von TCP-Bridge und Node.js-Adapter-Runtime (Port aus `config.toml` ? `ipc.ipcPort`, Fallback 5001).
- DokuWiki-Dokumentation unter `Doku/` erweitert um `core-bridge.txt`, `core-registry.txt`; `architektur.txt`, `core-routing.txt`, `runtime-lifecycle.txt`, `sprintplan.txt`, `start.txt` aktualisiert.

## [0.1.0] - 2026-10-05

### Added
- Basic framework of the runtime lifecycle (`FirstRun`, `OnStart`, `OnStop`) based on RSEV.Utilities.
- `MainRuntimeContext` as an application-wide singleton `RuntimeContext`, including its own logger (`SystemLog`, writes `latest.log` into the base directory) and configuration registry (`ConfConfig`).
- `ConfigCreator` for automatically creating a default `config.toml` on first startup.
- `ConfigLoader` for loading `config.toml` into the RSEV.Utilities configuration registry during system startup, including automatic resetting of `firstrun` to `false`.
- `UnexpectedUserExitHandler` for clean handling of CTRL+C, console-close events, and Windows shutdown/logoff events with guaranteed invocation of `OnStop`.
- **Core/Matrix** – Semantic state and reference matrix (`SemanticStateMatrix`):
  - `MatrixObject`, `StateNode`, `HistorySample` as core models of the object tree.
  - `IEmbeddingService` / `HashingEmbeddingService` for deterministic semantic embeddings.
  - `IVectorIndex` / `VectorIndex` for semantic similarity search (cosine similarity).
  - `IRoleService` / `DefaultRoleService` for read/write permissions per user and state.
  - Full JSONL snapshot persistence (`LoadSnapshotAsync`, `WriteSnapshotAsync`, periodic `StartCronSnapshot`).
  - API: `RegisterObject`, `GetObject`, `GetState`, `SetState`, `Subscribe`/`Unsubscribe`, `SemanticQuery`, `GetHistory`, `DisplayAsJson`.
- **Core/Bus** – Central neutral system bus:
  - `BusFrame` record (AdapterId, Address, Payload, ValueType, Kind, Timestamp, Metadata).
  - `SystemMessageBus` (based on `RSEV.Utilities.Messaging.BaseMessageBus`) with thread‑safe publish/subscribe including wildcard subscriptions.
- **Core/Routing** – `AdapterRouter` skeleton to distinguish between internal routing (`CustomAdapter`) and TCP‑bridge routing (`IoBrokerAdapter`, coming in a later version).
- Registration of `SemanticStateMatrix`, `SystemMessageBus`, and `AdapterRouter` in `MainRuntimeContext`, initialization during system startup, and automatic writing of the final snapshot during shutdown.

[0.2.0]: https://github.com/MrRSEV/ioBroker-NewGen/releases/tag/dev-0.2.0
[0.1.0]: https://github.com/MrRSEV/ioBroker-NewGen/releases/tag/dev-0.1.0
