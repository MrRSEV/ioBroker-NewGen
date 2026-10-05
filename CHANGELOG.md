# Changelog

Alle wesentlichen Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

Das Format orientiert sich an [Keep a Changelog](https://keepachangelog.com/de/1.1.0/),
und dieses Projekt folgt sinngemäß [Semantic Versioning](https://semver.org/lang/de/).

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

[0.1.0]: https://github.com/MrRSEV/ioBroker-NewGen/releases/tag/dev-0.1.0
