# ioBroker-NewGen

> Preview of an emerging project called **ioBroker-NewGen**.  
> Current status: **In development**.

## Short Description

**ioBroker-NewGen** is a modernized, stable, and object‑oriented C# smart home matrix designed as a long‑term evolution of the ioBroker ecosystem.  
The goal is a clear separation between core logic, adapter runtime, routing, and user interface.

## Goals

### Primary Goals
- Replace the historical ioBroker architecture with a modern C# state matrix  
- Isolated Node.js runtimes for legacy adapters  
- Unified adapter API for all adapter types  
- Stable, object‑oriented real‑time state‑sync engine  
- New admin UI in C#  
- Long‑term migration of popular ioBroker adapters to C#

### Secondary Goals
- Compatibility with existing ioBroker adapters, up to 10 simultaneously  
- Minimal hardware requirements for Raspberry Pi 5  
- Clean separation between internal and external routing  
- Modern, modular, and extensible system

## Architecture

### Core Components
- **StateMatrix**: object‑oriented, thread‑safe single source of truth  
- **SystemBus**: neutral data bus without knowledge of adapter type  
- **AdapterRouter**: decides between internal transport and TCP bridge  
- **AdminUI**: C#‑based interface for object tree, live state, adapter management, and logs

### Adapter Model
- **IAdapter** as common interface with `Name`, `Id`, `Type`, `SendPayload`, and `ReceivePayload`  
- **CustomAdapter**: C# assembly, host‑internal, direct SystemBus usage  
- **IoBrokerAdapter**: isolated Node.js process, connected via TCP bridge

### State Layer
- Direct access to the matrix  
- Validated writes into the matrix  
- Metadata from adapter constructors such as `min`, `max`, `type`, `role`, `unit`  
- Optional subscriptions via event bus for macro runtimes

### Object Tree
- Object tree is derived from the matrix  
- Can be represented as JSON  
- Deterministic, stable, and complete including metadata

### Rules Engine
- Macro runtimes based on JavaScript and TypeScript  
- Event‑driven execution via the event bus  
- Sandbox with time limit, memory limit, access restrictions, and crash recovery

### Additional Modules
- **History**: adapter‑side history processing via `OnStateChanged`  
- **Config System**: loading, saving, and optional validation per adapter  
- **User/Role/Permission System**: auth, roles, rights, tokens, and sessions  
- **Backup System**: snapshots, dumps, and optional cloud backups

## Hardware Target

### Minimal
- 4 cores ARM or x86  
- 8 GB RAM  
- 64–128 GB SSD  
- Pi‑5 compatible with up to 10 ioBroker adapters

### Recommended
- 4–6 x86 cores  
- 16 GB RAM  
- 128–256 GB SSD

### High-End
- 8–12 cores  
- 32–64 GB RAM  
- 256–512 GB NVMe

## Migration Strategy

1. Migrate critical adapters  
2. Migrate medium adapters  
3. Migrate small adapters  
4. Offer ioBroker adapters only as a legacy feature

## Vision

A modern, stable, object‑oriented smart home system that replaces ioBroker in the long term while remaining compatible.

Not a competitor to Home Assistant, but a modern alternative for developers and power users.

## Core Classes

- **Matrix**: central storage structure for datapoints, device objects, and metadata  
- **BusFrame**: unified frame format for all adapters  
- **EventBus**: central event routing  
- **StateEngine**: API layer on top of the matrix  
- **ObjectTree**: derived hierarchical representation from the matrix  
- **MacroRuntime**: execution of JS/TS macros  
- **AdapterLoader**: loading and instantiation of adapter assemblies  
- **AdapterSupervisor**: monitoring and stabilizing adapters  
- **AuthEngine**: user, role, and permission management  
- **BackupEngine**: system backup and restore

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
