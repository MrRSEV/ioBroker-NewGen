# Admin UI HTTP API

The API is registered by `AdminAdapter` through the host's runtime API host.
The dashboard is served at `/`, `/index.html`, and `/admin`. The configured
port defaults to `8080` (`admin.adminPort`).

Protected API calls use the host's authentication/session and permission
services. Request methods, payload validation, and permissions are enforced by
the individual endpoint handlers in `AdminAdapter.cs`; consult those handlers
when changing or integrating a route.

## Routes

| Route | Purpose |
| --- | --- |
| `/api/auth/login` | Sign in. |
| `/api/auth/logout` | Sign out. |
| `/api/auth/status` | Read authentication/setup status. |
| `/api/auth/setup` | Complete first-run setup. |
| `/api/auth/me` | Read the current user/session. |
| `/api/auth/change-password` | Change the current user's password. |
| `/api/i18n/languages` | List available languages. |
| `/api/i18n/translations` | Read translations. |
| `/api/i18n/translate` | Translate a text value. |
| `/api/i18n/extend` | Extend localization data. |
| `/api/states` | Read state/object data. |
| `/api/adapters` | List adapter registrations and statuses. |
| `/api/adapters/start` | Start an adapter instance. |
| `/api/adapters/stop` | Stop an adapter instance. |
| `/api/adapters/restart` | Restart an adapter instance. |
| `/api/adapters/autostart` | Read or update the autostart selection. |
| `/api/adapters/instances` | Add or remove adapter instances. |
| `/api/adapters/config` | Read or save per-instance adapter configuration. |
| `/api/adapters/oauth` | Start or complete adapter OAuth flows. |
| `/api/adapters/legacy` | Manage installed legacy adapters. |
| `/api/adapters/install` | Install a local adapter package. |
| `/api/adapters/install-remote` | Install an adapter package from a remote repository. |
| `/api/tasks` | Inspect background task status and progress. |
| `/api/config` | Read or update host configuration. |
| `/api/logs` | Read recent system log entries. |
| `/api/overview` | Read dashboard summary and system information. |
| `/api/permissions` | Read or update permission assignments. |
| `/api/roles` | Read or update roles. |
| `/api/users` | Read or update user-role assignments. |
| `/api/security/users` | Manage security users. |
| `/api/security/roles` | Manage security roles. |

The route list reflects registrations in the current adapter source. It is not
a promise that every HTTP method is supported by every route; use the endpoint
implementation as the authoritative method and payload contract.
