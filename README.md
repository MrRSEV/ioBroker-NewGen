# ioBroker-NewGen

> Preview of an emerging project called **ioBroker-NewGen**.  
> Current status: **In development**.

## Short Description

**ioBroker-NewGen** is a modern, stable, and object‑oriented C# smart home system built around a **neuronal storage** — a semantic vector matrix without any intelligence.  
Objects, states, history samples, and metadata are *materialized views* extracted from this vector space.

The goal is a clean separation between core logic, adapter runtime, routing, and user interface.

---

# Goals

### Primary Goals
- Replace the historical ioBroker architecture with a modern C# state matrix  
- Isolated Node.js runtimes for legacy adapters  
- Unified adapter API for all adapter types  
- Stable, object‑oriented real‑time state‑sync engine  
- New admin UI in C#  
- Long‑term migration of popular ioBroker adapters to C#

### Secondary Goals
- Compatibility with existing ioBroker adapters (up to 10 simultaneously)  
- Minimal hardware requirements for Raspberry Pi 5  
- Clean separation between internal and external routing  
- Modular, extensible, future‑proof system design

---

# Neuronal Storage (Semantic Vector Matrix)

The **Matrix** is not an object.  
It is a **semantic vector space** — a neuronal storage layer that holds embeddings for objects, states, and history samples.

All visible structures (ObjectTree, StateNodes, History) are **projections** or **materialized views** derived from this vector space.

## Matrix

### Description
Central semantic vector matrix for objects, states, embeddings, history, and persistence.  
It acts as a high‑dimensional storage layer without any intelligence.

### Components
- **objects** — materialized object views  
- **states** — materialized state views  
- **subscriptions** — callbacks for state changes  
- **roleService** — permission validation  
- **embeddingService** — text/payload embedding generator  
- **semanticIndex** — vector index (e.g., HNSW)  
- **snapshotPath** — path to JSONL snapshot file

### Lifecycle
- **OnStart**  
  Loads snapshot, reconstructs history samples, rebuilds embeddings, repopulates semantic index.
- **OnStop**  
  Writes a complete snapshot of all history samples.
- **CronSnapshot**  
  Periodically writes incremental snapshots.

### API
- **RegisterObject(MatrixObject) -> bool**  
  Generates object embedding, inserts into semantic index, creates a StateNode view.

- **GetObject(string id) -> MatrixObject**  
  Returns the materialized object view.

- **GetState(string id) -> StateNode**  
  Returns the materialized state view.

- **SetState(string id, object value, string user) -> bool**  
  Validates permissions, generates payload embedding, updates state view, appends history sample, updates semantic index, triggers subscriptions.

- **Subscribe(string id, Action<StateNode>)**  
  Registers a callback for state changes.

- **SemanticQuery(string text, int k) -> List<StateNode>**  
  Encodes text into an embedding and performs k‑NN search.

- **GetHistory(string id, DateTime? from, DateTime? to)**  
  Returns history samples for the history.0 adapter.

- **DisplayAsJson() -> string**  
  Outputs the materialized object tree as JSON.

- **WriteSnapshot()**  
  Persists all history samples as JSONL.

---

# Core Data Structures

## MatrixObject

### Description
Represents a device, channel, or datapoint.  
It is a **materialized projection** of embeddings stored in the Matrix.

### Fields
- **Id** — string  
- **Name** — string  
- **Type** — string  
- **Meta** — dictionary of metadata

### Embedding
- **source**: `Id + Name + Type + Meta`  
- **usage**: semantic identity of the object

---

## StateNode

### Description
Materialized view of the current state of an object.

### Fields
- **Id** — string  
- **Value** — object  
- **Timestamp** — DateTime  
- **Embedding** — float[]  
- **Meta** — metadata dictionary  
- **History** — queue of history samples

### Responsibilities
- Holds current value  
- Holds semantic payload embedding  
- Holds history samples  
- Is indexed in the vector index

---

## HistorySample

### Description
Represents a historical state value with timestamp and embedding.

### Fields
- **Id** — string  
- **Timestamp** — DateTime  
- **Value** — object  
- **Embedding** — float[]

### Usage
- Persistent time series  
- Semantic analysis over time  
- Reconstruction of historical states  
- Clustering, trends, anomaly detection

---

## VectorIndex

### Description
Semantic index for embeddings (e.g., HNSW).

### API
- **Add** — adds a state node embedding  
- **Search** — k‑NN search

---

## IEmbeddingService

### Description
Generates embeddings from text or payload.

### API
- **Encode(string) -> float[]**

---

## IRoleService

### Description
Role and permission validation.

### API
- **CanWrite(user, stateId) -> bool**  
- **CanRead(user, stateId) -> bool**

---

# Snapshot Format

### Description
JSONL file containing history samples.

### Example
```json { "Id": "temp.livingroom", "Timestamp": "2026-09-29T12:57:00Z", "Value": 22.5, "Embedding": [0.12, 0.88, ...] } ```

## MIT License

The MIT License (MIT)

Copyright (c) 2014–2026 bluefox <dogafox@gmail.com>,  
Copyright (c) 2014 hobbyquaker  
Copyright (c) 2026 MrRSEV – Richard Schumacher

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
