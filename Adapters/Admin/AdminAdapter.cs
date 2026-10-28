using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ioBroker_NewGen.Core.Adapters;
using ioBroker_NewGen.Core.Bus;
using ioBroker_NewGen.Core.Matrix;
using ioBroker_NewGen.Core.Registry;
using RSEV.Utilities.ApiBuilder;

namespace ioBroker_NewGen.Admin
{
    /// <summary>
    /// Admin ist ein normaler CustomAdapter in einer eigenen Assembly. Andere
    /// Adapter können ihn über den IAdapterContext als Dependency adressieren.
    /// </summary>
    public sealed class AdminAdapter : DeviceAdapterBase
    {
        private bool _enabled = true;
        private bool _registered;
        private volatile bool _stopping;
        private bool _authenticationRequired;

        public override string Name => "admin";

        protected override Task OnStartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _enabled = Config.Get<bool>("admin.adminEnabled");
                _authenticationRequired = Config.Get<bool>("admin.adminRequireAuthentication");
            }
            catch
            {
            }

            if (!_enabled || _registered)
            {
                return Task.CompletedTask;
            }

            if (_authenticationRequired && string.IsNullOrWhiteSpace(ReadConfig("admin.adminToken", string.Empty)))
            {
                var generatedToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
                Config.Set("admin.adminToken", generatedToken);
                Context.RuntimeContext.Logger.LogWarning($"[AdminUI] Kein Admin-Token konfiguriert. Temporäres Token erzeugt: {generatedToken}");
            }

            _stopping = false;

            var port = 8080;
            try
            {
                port = Config.Get<int>("admin.adminPort");
            }
            catch
            {
            }

            foreach (var route in new[] { "/", "/index.html", "/admin" })
            {
                Context.ApiHost.Register(new DashboardEndpoint(route));
            }
            Context.ApiHost.Register(new StatesEndpoint(Context));
            Context.ApiHost.Register(new AdaptersEndpoint(Context));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/start", Context, start: true));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/stop", Context, start: false));
            Context.ApiHost.Register(new AdapterInstallEndpoint(Context));
            Context.ApiHost.Register(new RemoteAdapterInstallEndpoint(Context));
            Context.ApiHost.Register(new ConfigurationEndpoint(Context));
            Context.ApiHost.Register(new LogsEndpoint(Context));
            Context.ApiHost.Register(new PermissionEndpoint(Context));
            Context.ApiHost.Register(new RoleEndpoint(Context));
            Context.ApiHost.Register(new UserRoleEndpoint(Context));
            _registered = true;

            var startTask = Context.ApiHost.StartAsync(port, cancellationToken);
            _ = startTask.ContinueWith(
                task =>
                {
                    if (!_stopping && task.IsFaulted && task.Exception is not null)
                    {
                        Context.RuntimeContext.Logger.LogError($"[AdminUI] Start fehlgeschlagen: {task.Exception.GetBaseException()}");
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            Context.RuntimeContext.Logger.LogInfo($"[AdminUI] Admin-Dependency-Adapter auf Port {port} gestartet.");
            return Task.CompletedTask;
        }

        protected override Task OnStopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;
            if (Context.ApiHost.IsRunning)
            {
                Context.ApiHost.Stop();
            }

            return Task.CompletedTask;
        }

        private sealed class DashboardEndpoint : BaseRuntimeEndpoint
        {
            public DashboardEndpoint(string route) : base(route)
            {
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                const string html = """
                    <!doctype html><html lang="de"><head><meta charset="utf-8"><title>ioBroker-NewGen Admin</title>
                    <style>body{font-family:system-ui;margin:2rem;background:#f5f7fa}section{background:white;padding:1rem;margin:1rem 0;border-radius:8px}pre{white-space:pre-wrap}input{min-width:24rem}</style>
                    </head><body><h1>ioBroker-NewGen</h1><section><label>Admin-Token <input id="token" type="password" autocomplete="current-password" placeholder="Token eingeben"/><button id="save" type="button">Speichern</button></label><span id="status"></span></section><section><h2>States</h2><pre id="states">Lade...</pre></section><section><h2>Adapter</h2><pre id="adapters">Lade...</pre></section><section><h2>Konfiguration</h2><pre id="config">Lade...</pre></section><section><h2>Rollen</h2><pre id="roles">Lade...</pre></section><section><h2>Benutzer/Rollen</h2><pre id="users">Lade...</pre></section>
                    <script>
                    const tokenInput=document.getElementById('token');const statusElement=document.getElementById('status');
                    tokenInput.value=localStorage.getItem('newgen-admin-token')||'';
                    document.getElementById('save').onclick=()=>{localStorage.setItem('newgen-admin-token',tokenInput.value);refresh();};
                    async function loadJson(path){const token=tokenInput.value.trim();const headers=token?{'X-Admin-Token':token}:{};const response=await fetch(path,{headers});if(!response.ok)throw new Error('HTTP '+response.status);return response.json();}
                    async function refresh(){try{const [states,adapters,config,roles,users]=await Promise.all([loadJson('/api/states'),loadJson('/api/adapters'),loadJson('/api/config'),loadJson('/api/roles'),loadJson('/api/users')]);document.getElementById('states').textContent=JSON.stringify(states,null,2);document.getElementById('adapters').textContent=JSON.stringify(adapters,null,2);document.getElementById('config').textContent=JSON.stringify(config,null,2);document.getElementById('roles').textContent=JSON.stringify(roles,null,2);document.getElementById('users').textContent=JSON.stringify(users,null,2);statusElement.textContent=' Verbunden';}catch(error){statusElement.textContent=' '+error.message;}}
                    refresh();setInterval(refresh,2000);
                    </script></body></html>
                    """;
                return Task.FromResult(Response(200, html, "text/html; charset=utf-8"));
            }
        }

        private sealed class StatesEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public StatesEndpoint(IAdapterContext context) : base("/api/states")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                return Task.FromResult(Response(200, _context.Matrix.DisplayAsJson(), "application/json; charset=utf-8"));
            }
        }

        private sealed class AdaptersEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public AdaptersEndpoint(IAdapterContext context) : base("/api/adapters")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                var adapters = _context.AdapterRegistry.GetAll()
                    .OrderBy(adapter => adapter.Id, StringComparer.Ordinal)
                    .Select(adapter => new
                    {
                        adapter.Name,
                        adapter.Id,
                        Type = adapter.Type.ToString(),
                        Status = adapter.Status.ToString(),
                        adapter.Metadata,
                        adapter.TcpEndpoint,
                        adapter.RegisteredAt,
                        adapter.LastSeenAt
                    });
                return Task.FromResult(Response(200, JsonSerializer.Serialize(adapters), "application/json; charset=utf-8"));
            }
        }

        private sealed class ConfigurationEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public ConfigurationEndpoint(IAdapterContext context) : base("/api/config")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                var values = new Dictionary<string, object?>
                {
                    ["node.nodeId"] = ReadConfig("node.nodeId", string.Empty),
                    ["node.instanceName"] = ReadConfig("node.instanceName", string.Empty),
                    ["ipc.ipcHost"] = ReadConfig("ipc.ipcHost", "127.0.0.1"),
                    ["ipc.ipcPort"] = ReadConfig("ipc.ipcPort", 5001),
                    ["adapter.customAdapterDirectory"] = ReadConfig("adapter.customAdapterDirectory", "Adapters/custom"),
                    ["admin.adminPort"] = ReadConfig("admin.adminPort", 8080)
                };

                if (request.Body is not { Length: > 0 })
                {
                    return Response(200, JsonSerializer.Serialize(values), "application/json; charset=utf-8");
                }

                try
                {
                    using var document = JsonDocument.Parse(request.Body);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return Response(400, "{\"error\":\"Konfiguration muss ein JSON-Objekt sein\"}", "application/json; charset=utf-8");
                    }

                    var changed = new List<string>();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!values.ContainsKey(property.Name) ||
                            string.Equals(property.Name, "admin.adminToken", StringComparison.OrdinalIgnoreCase))
                        {
                            return Response(400, JsonSerializer.Serialize(new { error = $"Konfigurationsschlüssel nicht editierbar: {property.Name}" }), "application/json; charset=utf-8");
                        }

                        _context.Config.Set(property.Name, ToConfigValue(property.Value));
                        changed.Add(property.Name);
                    }

                    var configPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
                    _context.Config.Save(configPath);
                    return Response(200, JsonSerializer.Serialize(new { updated = changed }), "application/json; charset=utf-8");
                }
                catch (Exception ex)
                {
                    return Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8");
                }
            }

            private T ReadConfig<T>(string key, T fallback)
            {
                try
                {
                    return _context.Config.Get<T>(key);
                }
                catch
                {
                    return fallback;
                }
            }

            private static object ToConfigValue(JsonElement value)
            {
                return value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number when value.TryGetInt32(out var integer) => integer,
                    JsonValueKind.Number when value.TryGetInt64(out var longInteger) => longInteger,
                    JsonValueKind.Number when value.TryGetDouble(out var number) => number,
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Array => value.EnumerateArray()
                        .Select(element => element.GetString() ?? string.Empty)
                        .ToArray(),
                    _ => throw new InvalidDataException("Nur boolesche, numerische, String- und String-Array-Werte sind zulässig.")
                };
            }
        }

        private sealed class AdapterCommandEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;
            private readonly bool _start;

            public AdapterCommandEndpoint(string route, IAdapterContext context, bool start) : base(route)
            {
                _context = context;
                _start = start;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                var body = request.Body is { Length: > 0 } ? Encoding.UTF8.GetString(request.Body) : string.Empty;
                var adapterId = body.Trim();
                if (body.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(body);
                        adapterId = document.RootElement.GetProperty("adapterId").GetString() ?? string.Empty;
                    }
                    catch (JsonException)
                    {
                        return Response(400, "{\"error\":\"Ungültiger JSON-Body\"}", "application/json; charset=utf-8");
                    }
                }

                if (string.IsNullOrWhiteSpace(adapterId))
                {
                    return Response(400, "{\"error\":\"adapterId fehlt\"}", "application/json; charset=utf-8");
                }

                var success = _start
                    ? await _context.StartAdapterAsync(adapterId)
                    : await _context.StopAdapterAsync(adapterId);
                return Response(success ? 200 : 404, JsonSerializer.Serialize(new { adapterId, success }), "application/json; charset=utf-8");
            }
        }

        private sealed class AdapterInstallEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public AdapterInstallEndpoint(IAdapterContext context) : base("/api/adapters/install")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                if (_context.AdapterPackageInstaller is null)
                {
                    return Response(503, "{\"error\":\"Adapter-Deployment ist nicht initialisiert\"}", "application/json; charset=utf-8");
                }

                var packageDirectory = request.Body is { Length: > 0 }
                    ? Encoding.UTF8.GetString(request.Body).Trim()
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(packageDirectory))
                {
                    return Response(400, "{\"error\":\"Paketverzeichnis fehlt\"}", "application/json; charset=utf-8");
                }

                try
                {
                    var manifest = await _context.AdapterPackageInstaller.InstallAsync(packageDirectory);
                    return Response(
                        200,
                        JsonSerializer.Serialize(new
                        {
                            manifest.Name,
                            manifest.Version,
                            manifest.EntryAssembly,
                            restartRequired = true
                        }),
                        "application/json; charset=utf-8");
                }
                catch (Exception ex)
                {
                    return Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8");
                }
            }
        }

        private sealed class RemoteAdapterInstallEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public RemoteAdapterInstallEndpoint(IAdapterContext context) : base("/api/adapters/install-remote")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                if (_context.AdapterRepositoryClient is null)
                {
                    return Response(503, "{\"error\":\"Repository-Client ist nicht initialisiert\"}", "application/json; charset=utf-8");
                }

                try
                {
                    using var document = JsonDocument.Parse(request.Body ?? Array.Empty<byte>());
                    var root = document.RootElement;
                    var indexUri = new Uri(root.GetProperty("indexUri").GetString() ?? string.Empty, UriKind.Absolute);
                    var name = root.GetProperty("name").GetString() ?? string.Empty;
                    var version = root.GetProperty("version").GetString() ?? string.Empty;
                    var staging = Path.Combine(AppContext.BaseDirectory, "adapter-staging");
                    var manifest = await _context.AdapterRepositoryClient.DownloadAndInstallAsync(indexUri, name, version, staging);
                    return Response(200, JsonSerializer.Serialize(new { manifest.Name, manifest.Version, restartRequired = true }), "application/json; charset=utf-8");
                }
                catch (Exception ex)
                {
                    return Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8");
                }
            }
        }

        private sealed class LogsEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public LogsEndpoint(IAdapterContext context) : base("/api/logs")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                var logs = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.log", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path => new
                    {
                        file = Path.GetFileName(path),
                        lines = ReadTail(path, 100)
                    });
                return Task.FromResult(Response(200, JsonSerializer.Serialize(logs), "application/json; charset=utf-8"));
            }
        }

        private sealed class PermissionEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public PermissionEndpoint(IAdapterContext context) : base("/api/permissions")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                if (request.Body is not { Length: > 0 })
                {
                    if (_context.RoleService is not IRoleManagementService roleManagementService)
                    {
                        return Task.FromResult(Response(503, "{\"error\":\"Rollenverwaltung ist nicht verfügbar\"}", "application/json; charset=utf-8"));
                    }

                    return Task.FromResult(Response(
                        200,
                        JsonSerializer.Serialize(new
                        {
                            userPermissions = roleManagementService.GetUserPermissions(),
                            roles = roleManagementService.GetRoles()
                        }),
                        "application/json; charset=utf-8"));
                }

                try
                {
                    using var document = JsonDocument.Parse(request.Body);
                    var root = document.RootElement;
                    var user = root.GetProperty("user").GetString() ?? string.Empty;
                    var stateId = root.GetProperty("stateId").GetString() ?? string.Empty;
                    var canRead = root.GetProperty("canRead").GetBoolean();
                    var canWrite = root.GetProperty("canWrite").GetBoolean();
                    var success = _context.Matrix.SetPermissions(user, stateId, canRead, canWrite);
                    return Task.FromResult(Response(success ? 200 : 400, JsonSerializer.Serialize(new { success }), "application/json; charset=utf-8"));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8"));
                }
            }
        }

        private sealed class RoleEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public RoleEndpoint(IAdapterContext context) : base("/api/roles")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                if (_context.RoleService is not IRoleManagementService roleManagementService)
                {
                    return Task.FromResult(Response(503, "{\"error\":\"Rollenverwaltung ist nicht verfügbar\"}", "application/json; charset=utf-8"));
                }

                if (request.Body is not { Length: > 0 })
                {
                    return Task.FromResult(Response(
                        200,
                        JsonSerializer.Serialize(roleManagementService.GetRoles()),
                        "application/json; charset=utf-8"));
                }

                try
                {
                    using var document = JsonDocument.Parse(request.Body);
                    var root = document.RootElement;
                    var name = root.GetProperty("name").GetString() ?? string.Empty;
                    var operation = root.TryGetProperty("operation", out var operationElement)
                        ? operationElement.GetString()
                        : "upsert";

                    if (string.Equals(operation, "remove", StringComparison.OrdinalIgnoreCase))
                    {
                        var removed = roleManagementService.RemoveRole(name);
                        return Task.FromResult(Response(
                            removed ? 200 : 404,
                            JsonSerializer.Serialize(new { name, removed }),
                            "application/json; charset=utf-8"));
                    }

                    var description = root.TryGetProperty("description", out var descriptionElement)
                        ? descriptionElement.GetString() ?? string.Empty
                        : string.Empty;
                    roleManagementService.UpsertRole(name, description);

                    if (root.TryGetProperty("stateId", out var stateElement) &&
                        !string.IsNullOrWhiteSpace(stateElement.GetString()))
                    {
                        var stateId = stateElement.GetString()!;
                        var canRead = root.TryGetProperty("canRead", out var readElement) && readElement.GetBoolean();
                        var canWrite = root.TryGetProperty("canWrite", out var writeElement) && writeElement.GetBoolean();
                        if (!roleManagementService.SetRolePermissions(name, stateId, canRead, canWrite))
                        {
                            return Task.FromResult(Response(400, "{\"error\":\"Rolle konnte nicht berechtigt werden\"}", "application/json; charset=utf-8"));
                        }
                    }

                    return Task.FromResult(Response(
                        200,
                        JsonSerializer.Serialize(new { name, updated = true }),
                        "application/json; charset=utf-8"));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8"));
                }
            }
        }

        private sealed class UserRoleEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public UserRoleEndpoint(IAdapterContext context) : base("/api/users")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                if (_context.RoleService is not IRoleManagementService roleManagementService)
                {
                    return Task.FromResult(Response(503, "{\"error\":\"Rollenverwaltung ist nicht verfügbar\"}", "application/json; charset=utf-8"));
                }

                if (request.Body is not { Length: > 0 })
                {
                    return Task.FromResult(Response(
                        200,
                        JsonSerializer.Serialize(roleManagementService.GetUserRoleAssignments()),
                        "application/json; charset=utf-8"));
                }

                try
                {
                    using var document = JsonDocument.Parse(request.Body);
                    var root = document.RootElement;
                    var user = root.GetProperty("user").GetString() ?? string.Empty;
                    var role = root.GetProperty("role").GetString() ?? string.Empty;
                    var operation = root.TryGetProperty("operation", out var operationElement)
                        ? operationElement.GetString()
                        : "assign";
                    var success = string.Equals(operation, "revoke", StringComparison.OrdinalIgnoreCase)
                        ? roleManagementService.RevokeRole(user, role)
                        : roleManagementService.AssignRole(user, role);

                    return Task.FromResult(Response(
                        success ? 200 : 400,
                        JsonSerializer.Serialize(new { user, role, operation, success }),
                        "application/json; charset=utf-8"));
                }
                catch (Exception ex)
                {
                    return Task.FromResult(Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8"));
                }
            }
        }

        private static bool IsAuthorized(IAdapterContext context, EndpointRequest request)
        {
            var required = ReadConfig(context, "admin.adminRequireAuthentication", false);
            if (!required)
            {
                return true;
            }

            var expectedToken = ReadConfig(context, "admin.adminToken", string.Empty);
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return false;
            }

            var suppliedToken = request.Headers
                .FirstOrDefault(pair => string.Equals(pair.Key, "X-Admin-Token", StringComparison.OrdinalIgnoreCase))
                .Value;
            if (string.IsNullOrWhiteSpace(suppliedToken))
            {
                suppliedToken = request.Headers
                    .FirstOrDefault(pair => string.Equals(pair.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    .Value;
                if (suppliedToken?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                {
                    suppliedToken = suppliedToken[7..].Trim();
                }
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedToken),
                Encoding.UTF8.GetBytes(suppliedToken ?? string.Empty));
        }

        private static T ReadConfig<T>(IAdapterContext context, string key, T fallback)
        {
            try
            {
                return context.Config.Get<T>(key);
            }
            catch
            {
                return fallback;
            }
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            return ReadConfig(Context, key, fallback);
        }

        private static string[] ReadTail(string path, int maxLines)
        {
            try
            {
                return File.ReadLines(path).TakeLast(maxLines).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static EndpointResponse Unauthorized()
        {
            var response = Response(401, "{\"error\":\"Authentifizierung erforderlich\"}", "application/json; charset=utf-8");
            response.Headers["WWW-Authenticate"] = "Bearer";
            return response;
        }

        private static EndpointResponse Response(int statusCode, string body, string contentType)
        {
            return EndpointResponse.FromText(statusCode, body, contentType);
        }
    }
}
