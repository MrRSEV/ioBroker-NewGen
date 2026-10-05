# Changelog

All significant changes to this project are documented in this file.

The format follows the principles of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project loosely adheres to [Semantic Versioning](https://semver.org/).

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

[0.1.0]: https://github.com/MrRSEV/ioBroker-NewGen/releases/tag/dev-0.1.0
