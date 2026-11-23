# Claude instructions for ioBroker-NewGen

## Repository scope

This repository is the project-level ioBroker-NewGen workspace/repository. Keep
the separately versioned runtime, Admin UI, and Web Adapter repositories
separate. Do not move shared source files across repository boundaries without
updating the corresponding project documentation and build references.

## Working conventions

- Preserve existing user changes and inspect `git status` before editing.
- Use PowerShell on Windows and prefer `rg` for searches.
- Use `apply_patch` for source and documentation changes.
- Keep generated output, `bin`, and `obj` files out of commits unless the
  repository explicitly tracks them.
- When a change crosses repository boundaries, build and validate every affected
  repository.
- Document every change in the repository's `CHANGELOG.md`. If the change
  affects an API, workflow, architecture, configuration, or user-facing
  behavior, update the relevant documentation under `/Doku` or `doc/` as well.

## Architecture

- `ioBroker-NewGen.runtime` owns the core, adapter registry, matrix, security,
  legacy Node.js runtime, and bridge contracts.
- `Adapters/ioBroker-NewGen.admin` owns the native Admin UI adapter.
- `Adapters/ioBroker-NewGen.web` owns the HTTP/WebSocket listener.
- Native and legacy adapters register web endpoints through
  `IWebEndpointRegistry`; adapters must not create their own web server.

## Validation

Use the affected repository's documented `dotnet build` command. For changes to
runtime contracts, rebuild the runtime first, refresh dependent `/lib` DLLs,
then rebuild dependent adapters.
