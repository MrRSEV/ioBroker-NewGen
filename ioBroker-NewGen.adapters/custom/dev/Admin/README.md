# ioBroker-NewGen Admin UI

The Admin UI is the `admin` custom adapter for ioBroker-NewGen. It is built as
a separate .NET 9 assembly and loaded by the host's custom-adapter runtime; it
is not part of the host executable.

## Responsibilities

- Serve the dashboard at `/`, `/index.html`, and `/admin`.
- Expose the HTTP API for authentication, states, adapters, configuration,
  logs, overview, localization, and user/role administration.
- Use the host's adapter context, API host, configuration, security services,
  adapter registry, and matrix instead of owning separate runtime services.

The dashboard markup and client-side code are currently embedded in
[`AdminAdapter.cs`](./AdminAdapter.cs). There is no separate frontend build.

## Build

Requirements:

- .NET SDK 9.
- `lib/ioBroker-NewGen.dll` matching the host API used by this adapter.
- `lib/RSEV.Utilities.dll` matching the runtime library used by the host.

From this directory:

```powershell
dotnet build AdminUI.csproj
```

The project writes `AdminUI.dll` to the sibling `dev/bin` directory, as
configured by `AdminUI.csproj`. Copy that assembly to the host repository's
`Adapters/custom/AdminUI.dll` before building or publishing the host. The host
copies files from `Adapters/custom` into its output and publish directories.

## Runtime configuration

The adapter reads the following keys from `config.toml`:

| Key | Default | Purpose |
| --- | --- | --- |
| `admin.adminEnabled` | `true` | Enables or disables the HTTP service. |
| `admin.adminPort` | `8080` | Port used by the host API server. |
| `admin.adminRequireAuthentication` | `true` in the default config | Requires the host security service for interactive login. |

The host must also load and enable the adapter, typically by including `admin`
in `adapter.enabledAdapters`. Authentication and endpoint permissions are
provided by the host's Core.Security services.

## Project documentation

- [Architecture and development](./docs/architecture.md)
- [HTTP API routes](./docs/api.md)

This directory is maintained separately from the `ioBroker-NewGen` host
repository. Keep host and Admin UI changes coordinated when changing adapter
contracts or runtime APIs.
