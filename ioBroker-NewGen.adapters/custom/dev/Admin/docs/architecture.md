# Admin UI architecture

## Runtime boundary

`AdminAdapter` derives from `DeviceAdapterBase` and identifies itself as
`admin`. It is an external custom-adapter assembly, not a second host process.
The host supplies the `IAdapterContext`, including configuration, logging,
the HTTP API host, security services, adapter registry, and state matrix.

The adapter registers its endpoints during `OnStartAsync` and starts the
provided API host on `admin.adminPort` (default `8080`). When stopped, it stops
the API host and clears its registration state so the adapter can be restarted.
`admin.adminEnabled` controls whether the service is started.

The dashboard document and client-side code are embedded in
`AdminAdapter.cs` in `DashboardEndpoint`; no separate static web application or
asset pipeline is required.

## Responsibilities and ownership

| Concern | Owner |
| --- | --- |
| Process lifecycle, adapter loading and configuration | NewGen host |
| Adapter API, registry and state matrix | NewGen host |
| Authentication, sessions, users, roles and permissions | Host Core.Security |
| Dashboard and Admin-specific HTTP endpoints | Admin UI adapter |

The adapter should use host contracts rather than introducing duplicate
registries, state stores, or security implementations. Changes to
`ioBroker-NewGen.dll` or `RSEV.Utilities.dll` must be coordinated with the host.

## Source and build layout

- `AdminAdapter.cs` contains adapter lifecycle, endpoint registration, endpoint
  handlers, and the embedded dashboard.
- `AdminUI.csproj` targets .NET 9 and references the host/runtime assemblies
  under `lib`.
- `lib/ioBroker-NewGen.dll` is the host API contract used at compile time.
- `lib/RSEV.Utilities.dll` is the shared runtime dependency.
- The project output is the sibling `dev/bin/AdminUI.dll`.
- The host loads the deployed assembly from `Adapters/custom`. The host project
  copies that directory to its build and publish output, preserving nested
  paths.

Build the adapter from this directory with `dotnet build AdminUI.csproj`.
Deploy the resulting DLL to the host's `Adapters/custom` directory before
building or publishing the host.

## Authentication and permissions

Interactive authentication is integrated with the host security service.
Endpoints use host authentication/session helpers and map protected operations
to host permissions. The Admin adapter does not own user credentials or
permission persistence. If authentication is required but Core.Security is
unavailable, the adapter logs a warning; the optional automation token is a
separate integration path.

For detailed routes, see the [API route reference](./api.md).
