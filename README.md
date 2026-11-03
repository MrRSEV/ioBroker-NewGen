# ioBroker-NewGen

> **Unofficial community project.** This project is not affiliated with or endorsed by the official ioBroker project.
>
> Preview of an emerging project called **ioBroker-NewGen**.  
> Current status: **v1.0.0-beta** (Core.Security, LocalizationService, AdminUI-Adapterverwaltung; Hardware-/Cloud-Abnahmen ausstehend).

## Vision

**ioBroker-NewGen** is a modern, stable, and object‑oriented C# smart home system built around a **neuronal storage** — a semantic vector matrix without any intelligence.  
Objects, states, history samples, and metadata are *materialized views* extracted from this vector space.

The goal is a clean separation between core logic, adapter runtime, routing, and user interface.

### Primary Goals
- Replace the historical ioBroker architecture with a modern C# state matrix  
- Isolated Node.js runtimes for legacy adapters  
- Drop-in `@iobroker/adapter-core` replacement so unmodified legacy adapters run without js-controller over the TCP bridge  
- Unified adapter API for all adapter types  
- Stable, object‑oriented real‑time state‑sync engine  
- New admin UI in C#  
- Long‑term migration of popular ioBroker adapters to C#

### Secondary Goals
- Compatibility with existing ioBroker adapters (up to 10 simultaneously)  
- Minimal hardware requirements for Raspberry Pi 5  
- Clean separation between internal and external routing  
- Modular, extensible, future‑proof system design

## Architecture

- **Neuronal storage (Matrix):** a semantic vector space holding embeddings for objects, states and history samples. ObjectTree, StateNodes and History are projections (materialized views) of it.
- **Core:** hosts the Matrix, state-sync engine, routing, security/roles, localization and the adapter runtime. It uses **RSEV.Utilities 3.0.0.0** for lifecycle, messaging, events, process and assembly loading.
- **Adapters:** AdminUI and all native adapters run as separate external assemblies, loaded by the core at runtime. Legacy ioBroker (Node.js) adapters run in isolated Node.js processes and talk to the core over a TCP bridge.
- **Admin UI:** served by the core on the configured `admin.adminPort` (default `8080`).

## Repository Structure

| Folder | Content |
|--------|---------|
| [ioBroker-NewGen.core](./ioBroker-NewGen.core) | Core application: Matrix, data structures, adapter configuration, legacy adapter runtime. See its [README](./ioBroker-NewGen.core/README.md). |
| [ioBroker-NewGen.adapters](./ioBroker-NewGen.adapters) | Native C# adapters (`custom`). `lagacy` holds local legacy ioBroker sources and is not tracked. |

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
