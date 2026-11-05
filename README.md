# ioBroker-NewGen

> **Unofficial community project.** This project is not affiliated with or endorsed by the official ioBroker project.

## Vision

**ioBroker-NewGen** is a modern, stable, object-oriented C# smart home system built around a **neuronal storage** — a semantic vector matrix without any intelligence. Objects, states, history samples, and metadata are materialized views extracted from this vector space.

The goal is a clean separation between core logic, adapter runtime, routing, and user interface.

### Primary goals

- Replace the historical ioBroker architecture with a modern C# state matrix.
- Run legacy Node.js adapters in isolated processes.
- Provide a drop-in `@iobroker/adapter-core` replacement so unmodified legacy adapters can run without js-controller over the TCP bridge.
- Provide a unified adapter API for all adapter types.
- Build a stable, object-oriented real-time state-sync engine.
- Provide a new Admin UI in C#.
- Support long-term migration of popular ioBroker adapters to C#.

### Secondary goals

- Support existing ioBroker adapters, with up to 10 running simultaneously.
- Keep hardware requirements minimal for Raspberry Pi 5.
- Separate internal and external routing.
- Keep the system modular, extensible, and future-proof.

## Architecture

- **Neuronal storage (Matrix):** a semantic vector space holding embeddings for objects, states, and history samples. ObjectTree, StateNodes, and History are projections (materialized views) of it.
- **Core:** hosts the Matrix, state-sync engine, routing, security and roles, localization, and adapter runtime. It uses **RSEV.Utilities 3.0.0.0** for lifecycle, messaging, events, process, and assembly loading.
- **Adapters:** native C# adapters run as external assemblies loaded by the core. Legacy ioBroker (Node.js) adapters run in isolated Node.js processes and communicate with the core over a TCP bridge.
- **Admin UI:** runs as an optional separate C# adapter. Its routes are served by the external `ioBroker-NewGen.web` adapter on `web.listenHost`/`web.port` (default `127.0.0.1:8080`).

The optional native Admin UI adapter can be downloaded by a developer from
[`MrRSEV/ioBroker-NewGen.admin`](https://github.com/MrRSEV/ioBroker-NewGen.admin)
and added manually as `AdminUI.dll`.

## Repositories

The project is split into local repositories. These links point to sibling folders in a local workspace; remote URLs can be added when they are provided.

- [ioBroker-NewGen.runtime](../ioBroker-NewGen.runtime/README.md) — the core application, including the Matrix, data structures, adapter configuration, and legacy adapter runtime.
- [ioBroker-NewGen.admin](../ioBroker-NewGen.admin/README.md) — the Admin UI custom adapter.


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
