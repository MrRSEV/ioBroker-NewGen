using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using ioBroker_NewGen.Core.Adapters.Configuration;
using System.Threading;
using System.Threading.Tasks;
using ioBroker_NewGen.Core.Adapters;
using ioBroker_NewGen.Controllers;
using ioBroker.Core.Security;
using ioBroker_NewGen.Core.Bus;
using ioBroker_NewGen.Core.Matrix;
using ioBroker_NewGen.Core.Registry;
using ioBroker_NewGen.Core.Routing;
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
        private Task? _startTask;
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

            if (_authenticationRequired && SecurityOf(Context) is null)
            {
                Context.RuntimeContext.Logger.LogWarning("[AdminUI] Core.Security ist nicht initialisiert. Anmeldung nur über den optionalen Automations-Token (admin.adminToken) möglich.");
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
            Context.ApiHost.Register(new LoginEndpoint(Context));
            Context.ApiHost.Register(new AuthStatusEndpoint(Context));
            Context.ApiHost.Register(new FirstRunSetupEndpoint(Context));
            Context.ApiHost.Register(new LogoutEndpoint(Context));
            Context.ApiHost.Register(new MeEndpoint(Context));
            Context.ApiHost.Register(new ChangePasswordEndpoint(Context));
            foreach (var i18nMode in new[] { "languages", "translations", "translate", "extend" })
            {
                Context.ApiHost.Register(new I18nEndpoint(i18nMode, Context));
            }
            Context.ApiHost.Register(new StatesEndpoint(Context));
            Context.ApiHost.Register(new AdaptersEndpoint(Context));
            Context.ApiHost.Register(new AdapterConfigEndpoint(Context));
            Context.ApiHost.Register(new AdapterOAuthEndpoint(Context));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/start", Context, "start"));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/stop", Context, "stop"));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/restart", Context, "restart"));
            Context.ApiHost.Register(new AutostartEndpoint(Context));
            Context.ApiHost.Register(new InstanceEndpoint(Context));
            Context.ApiHost.Register(new LegacyAdapterEndpoint(Context));
            Context.ApiHost.Register(new TasksEndpoint(Context));
            Context.ApiHost.Register(new AdapterInstallEndpoint(Context));
            Context.ApiHost.Register(new RemoteAdapterInstallEndpoint(Context));
            Context.ApiHost.Register(new ConfigurationEndpoint(Context));
            Context.ApiHost.Register(new LogsEndpoint(Context));
            Context.ApiHost.Register(new OverviewEndpoint(Context));
            Context.ApiHost.Register(new PermissionEndpoint(Context));
            Context.ApiHost.Register(new RoleEndpoint(Context));
            Context.ApiHost.Register(new UserRoleEndpoint(Context));
            Context.ApiHost.Register(new SecurityUsersEndpoint(Context));
            Context.ApiHost.Register(new SecurityRolesEndpoint(Context));
            _registered = true;

            var startTask = Context.ApiHost.StartAsync(port, cancellationToken);
            _startTask = startTask;
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

        protected override async Task OnStopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;
            if (Context.ApiHost.IsRunning)
            {
                Context.ApiHost.Stop();
            }

            // Auf das Ende der Accept-Schleife warten, damit ein Neustart nicht vom späten "Gestoppt" des alten Laufs überholt wird.
            var pending = _startTask;
            if (pending is not null)
            {
                try
                {
                    await Task.WhenAny(pending, Task.Delay(3000, CancellationToken.None)).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            _startTask = null;
            _registered = false;
        }

        /// <summary>Admin-UI-Task-Manager: führt lange Aktionen parallel im Hintergrund aus und hält ein abrufbares Fortschrittslog vor.</summary>
        private sealed class AdminTask
        {
            private readonly object _gate = new();
            private readonly List<string> _log = new();

            public AdminTask(string id, string title)
            {
                Id = id;
                Title = title;
                Started = DateTimeOffset.UtcNow;
            }

            public string Id { get; }
            public string Title { get; }
            public DateTimeOffset Started { get; }
            public string Status { get; private set; } = "running";
            public string? Message { get; private set; }

            public void Log(string line)
            {
                lock (_gate)
                {
                    _log.Add($"{DateTime.Now:HH:mm:ss} {line}");
                }
            }

            public void Complete(bool success, string message)
            {
                lock (_gate)
                {
                    _log.Add($"{DateTime.Now:HH:mm:ss} {(success ? "✔" : "✖")} {message}");
                    Message = message;
                    Status = success ? "done" : "error";
                }
            }

            public object Snapshot(int since)
            {
                lock (_gate)
                {
                    var from = Math.Clamp(since, 0, _log.Count);
                    return new { id = Id, title = Title, status = Status, message = Message, started = Started, total = _log.Count, log = _log.Skip(from).ToArray() };
                }
            }
        }

        private static class AdminTasks
        {
            private const int MaxFinished = 30;
            private static readonly object Gate = new();
            private static readonly List<AdminTask> Tasks = new();

            public static AdminTask Start(string title, Func<AdminTask, Task<string>> work)
            {
                var task = new AdminTask(Guid.NewGuid().ToString("N"), title);
                lock (Gate)
                {
                    Tasks.Add(task);
                    var finished = Tasks.Where(t => t.Status != "running").ToList();
                    foreach (var old in finished.Take(Math.Max(0, finished.Count - MaxFinished)))
                    {
                        Tasks.Remove(old);
                    }
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        task.Complete(true, await work(task));
                    }
                    catch (Exception ex)
                    {
                        task.Complete(false, ex.Message);
                    }
                });
                return task;
            }

            public static IReadOnlyList<AdminTask> All()
            {
                lock (Gate)
                {
                    return Tasks.ToList();
                }
            }

            public static AdminTask? Find(string id)
            {
                lock (Gate)
                {
                    return Tasks.FirstOrDefault(t => t.Id == id);
                }
            }

            public static bool Dismiss(string id)
            {
                lock (Gate)
                {
                    var task = Tasks.FirstOrDefault(t => t.Id == id);
                    return task is not null && task.Status != "running" && Tasks.Remove(task);
                }
            }
        }

        private sealed class TasksEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public TasksEndpoint(IAdapterContext context) : base("/api/tasks")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                const string json = "application/json; charset=utf-8";
                var body = request.Body ?? Array.Empty<byte>();
                if (body.Length == 0)
                {
                    return Task.FromResult(Response(200, JsonSerializer.Serialize(AdminTasks.All().Select(t => t.Snapshot(int.MaxValue))), json));
                }

                try
                {
                    using var document = JsonDocument.Parse(body);
                    var root = document.RootElement;
                    var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
                    var since = root.TryGetProperty("since", out var sinceElement) && sinceElement.TryGetInt32(out var s) ? s : 0;
                    var action = root.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : "get";
                    if (action == "dismiss")
                    {
                        return Task.FromResult(Response(200, JsonSerializer.Serialize(new { dismissed = AdminTasks.Dismiss(id) }), json));
                    }

                    var task = AdminTasks.Find(id);
                    return Task.FromResult(task is null
                        ? Response(404, "{\"error\":\"Task nicht gefunden\"}", json)
                        : Response(200, JsonSerializer.Serialize(task.Snapshot(since)), json));
                }
                catch (JsonException)
                {
                    return Task.FromResult(Response(400, "{\"error\":\"Ungültige Anfrage\"}", json));
                }
            }
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
                    <style>
                    :root{--primary:#3399cc;--primary-text:#fff;--bg:#fafafa;--paper:#fff;--text:#1a1a1a;--text2:#00000080;--divider:#0000001f;--hover:#3399cc1a;--th:#f0f3f7;--prop:#fafbfc;--ok:#3bb143;--err:#a90000;--drawer:180px}
                    html.dark{--primary:#4dabf5;--primary-text:#000;--bg:#1a1a1a;--paper:#272727;--text:#fafafa;--text2:#ffffff80;--divider:#ffffff1f;--hover:#4dabf526;--th:#333;--prop:#2e2e2e;--ok:#388e3c;--err:#ff6b6b}
                    *{box-sizing:border-box}
                    html,body{height:100%}
                    body{margin:0;font-family:'Segoe UI',Tahoma,Arial,sans-serif;font-size:12px;background:var(--bg);color:var(--text);overflow:hidden}
                    .hidden{display:none!important}
                    #taskDock{position:fixed;left:.5rem;bottom:.5rem;z-index:50;display:flex;gap:.4rem;flex-wrap:wrap;max-width:calc(100vw - 1rem)}
                    #taskDock .chip{display:flex;align-items:center;gap:.4rem;background:var(--paper);border:1px solid var(--divider);border-left:4px solid var(--primary);border-radius:4px;padding:.3rem .6rem;cursor:pointer;box-shadow:0 2px 6px #0003;max-width:22rem}
                    #taskDock .chip.done{border-left-color:var(--ok)}#taskDock .chip.error{border-left-color:var(--err)}
                    #taskDock .chip span.t{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
                    .spin{width:.9em;height:.9em;border:2px solid var(--divider);border-top-color:var(--primary);border-radius:50%;animation:spin 1s linear infinite;flex:none}
                    @keyframes spin{to{transform:rotate(360deg)}}
                    #taskWin{position:fixed;left:50%;top:50%;transform:translate(-50%,-50%);width:min(48rem,95vw);height:min(30rem,80vh);z-index:60;background:var(--paper);border-radius:4px;box-shadow:0 8px 32px #0006;display:flex;flex-direction:column}
                    #taskWin header{display:flex;align-items:center;gap:.5rem;padding:.5rem .75rem;background:var(--primary);color:var(--primary-text);border-radius:4px 4px 0 0}
                    #taskWin header h3{margin:0;flex:1;font-weight:400;font-size:1.2em}
                    #taskWin header button{background:transparent;border:0;color:inherit;cursor:pointer;font-size:1.3em;line-height:1;padding:.1rem .4rem}
                    #taskBar{height:4px;background:var(--divider);overflow:hidden;flex:none}
                    #taskBar i{display:block;height:100%;width:35%;background:var(--primary);animation:slide 1.2s ease-in-out infinite}
                    #taskBar.done i{animation:none;width:100%;background:var(--ok)}#taskBar.error i{animation:none;width:100%;background:var(--err)}
                    @keyframes slide{0%{margin-left:-35%}100%{margin-left:100%}}
                    #taskLog{flex:1;margin:0;padding:.6rem .75rem;overflow:auto;background:var(--prop);font:11px/1.5 Consolas,monospace;white-space:pre-wrap;word-break:break-word}
                    #taskFoot{padding:.5rem .75rem;display:flex;gap:.5rem;align-items:center;border-top:1px solid var(--divider)}#taskFoot #taskMsg{flex:1}
                    .error{color:var(--err)}
                    button{font:inherit;color:inherit}
                    select,input{font:inherit;color:var(--text);background:var(--paper);border:1px solid var(--divider);border-radius:4px;padding:.3rem .5rem}
                    #login,#change,#setup,#firstrun{height:100%;align-items:center;justify-content:center;background:var(--bg);display:flex}
                    #setup{overflow:auto}.setupcard{width:30rem;max-width:95vw;max-height:95vh;overflow:auto}.setupcard label{display:block;margin-bottom:.5rem}.setupcard input:not([type=checkbox]),.setupcard select{width:100%}
                    #setup.hidden{display:none}
                    .setupcard .btn2{background:transparent;border:1px solid var(--divider);border-radius:4px;padding:.45rem 1rem;cursor:pointer}.setupcard .dlgbtn{margin-top:.75rem}.setupcard>form>button{margin-top:.5rem}
                    .card{background:var(--paper);width:22rem;padding:1.5rem;border-radius:4px;box-shadow:0 2px 10px #0003}
                    .card h1,.card h2{margin:0 0 1rem;color:var(--primary);font-weight:400}
                    .card input{width:100%;margin:.25rem 0 .75rem;display:block}
                    .card button[type=submit],.btn{background:var(--primary);color:var(--primary-text);border:0;border-radius:4px;padding:.45rem 1rem;cursor:pointer;text-transform:uppercase;letter-spacing:.04em}
                    .card button:disabled,.btn:disabled{opacity:.5}
                    #app{display:flex;flex-direction:column;height:100%}
                    .appbar{height:48px;flex:none;display:flex;align-items:center;gap:.75rem;padding:0 .75rem;background:var(--primary);color:var(--primary-text);box-shadow:0 2px 4px #0003;z-index:2}
                    .appbar .title{font-size:1.5em;font-weight:400;flex:1}
                    .appbar .iconbtn{background:none;border:0;cursor:pointer;font-size:1.6em;line-height:1;padding:.2rem .4rem;border-radius:50%;color:inherit}
                    .appbar .iconbtn:hover{background:#ffffff33}
                    .appbar select{background:transparent;color:inherit;border-color:#ffffff80}
                    .appbar select option{color:#000}
                    .layout{display:flex;flex:1;min-height:0}
                    .drawer{width:var(--drawer);flex:none;background:var(--paper);border-right:1px solid var(--divider);display:flex;flex-direction:column;transition:width .2s;overflow:hidden}
                    body.compact .drawer{width:50px}
                    body.compact .drawer .item span.lbl{display:none}
                    .drawer .items{flex:1;overflow:auto;padding:.4rem .3rem}
                    .drawer .item{display:flex;align-items:center;gap:.6rem;height:40px;padding:0 .6rem;margin:2px 0;border-radius:20px;cursor:pointer;white-space:nowrap;user-select:none}
                    .drawer .item:hover{background:var(--hover)}
                    .drawer .item.sel{background:var(--primary);color:var(--primary-text)}
                    .drawer .item .ico{width:24px;text-align:center;font-size:1.5em;flex:none}
                    .drawer .foot{border-top:1px solid var(--divider);padding:.5rem .75rem;color:var(--text2);font-size:.9em;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
                    body.compact .drawer .foot{display:none}
                    .content{flex:1;overflow:auto;padding:0;min-width:0}
                    .panel{display:flex;flex-direction:column;min-height:100%}
                    .toolbar{position:sticky;top:0;z-index:1;display:flex;align-items:center;gap:.75rem;min-height:48px;padding:.25rem 1rem;background:var(--paper);border-bottom:1px solid var(--divider)}
                    .toolbar h2{margin:0;font-size:1.4em;font-weight:400;flex:1}
                    .panel .body{padding:1rem}
                    pre{white-space:pre-wrap;margin:0;background:var(--paper);padding:1rem;border-radius:4px;overflow:auto;font-family:Consolas,monospace}
                    .tabs{display:inline-flex}
                    .tabs button{border:1px solid var(--primary);background:transparent;color:var(--primary);padding:.3rem .9rem;cursor:pointer;text-transform:uppercase;font-size:.9em}
                    .tabs button:first-child{border-radius:4px 0 0 4px}
                    .tabs button:last-child{border-radius:0 4px 4px 0;border-left:0}
                    .tabs button.active{background:var(--primary);color:var(--primary-text)}
                    table{border-collapse:collapse;width:100%;background:var(--paper)}
                    th,td{border-bottom:1px solid var(--divider);padding:.35rem .6rem;text-align:left;font-size:1em}
                    th{background:var(--th);font-weight:600;position:sticky;top:0}
                    table.tree tr:hover td{background:var(--hover)}
                    .arrow{display:inline-block;width:1.1rem;font-size:.7rem;color:var(--text2)}
                    .folder{font-weight:600}
                    .muted{color:var(--text2)}
                    .prop td{background:var(--prop);font-size:.95em}
                    .prop-name{color:var(--text2)}
                    .st-Running,.st-Connected{color:var(--ok);font-weight:600}
                    .st-Faulted{color:var(--err);font-weight:600}
                    .overlay{position:fixed;inset:0;background:#0007;display:flex;align-items:center;justify-content:center;z-index:10}
                    .dlg{width:30rem;max-width:95vw;max-height:90vh;overflow:auto}
                    .dlg label{display:block}
                    .chk{display:block;margin:.2rem 0}
                    .chk input,.dlg input[type=checkbox]{width:auto;display:inline;margin:0 .3rem 0 0}
                    fieldset.checks{border:1px solid var(--divider);border-radius:4px;margin:.25rem 0 .75rem;padding:.4rem .75rem}
                    .dlgbtn{display:flex;gap:.5rem;justify-content:flex-end;margin-top:.75rem}
                    .cfgview{max-width:60rem}.cfgview h3{margin:0 0 .75rem;font-weight:400;color:var(--primary)}
                    .cfgview .btn2{background:transparent;border:1px solid var(--divider);border-radius:4px;padding:.45rem 1rem;cursor:pointer}
                    .oauth{margin:1rem 0;padding:.6rem .8rem;border:1px solid var(--divider);border-radius:4px}.oauth small{display:block;margin-top:.3rem}
                    .dlg:has(.cfgform){width:44rem}
                    .cfggrid{display:grid;grid-template-columns:repeat(auto-fit,minmax(14rem,1fr));gap:.25rem .75rem}
                    .cfgitem{margin:.25rem 0}.cfgitem h3{margin:.5rem 0;font-weight:400;color:var(--primary)}
                    .cfgitem input:not([type=checkbox]),.cfgitem select,.cfgitem textarea{width:100%;font:inherit;color:var(--text);background:var(--paper);border:1px solid var(--divider);border-radius:4px;padding:.3rem .5rem}
                    .cfgitem small{display:block}
                    .cfgtabs{display:flex;gap:.25rem;border-bottom:1px solid var(--divider);margin-bottom:.5rem}.cfgtabs button{background:none;border:0;padding:.4rem .8rem;cursor:pointer}.cfgtabs button.on{border-bottom:2px solid var(--primary);color:var(--primary)}
                    .dlg .btn2{background:transparent;border:1px solid var(--divider);border-radius:4px;padding:.45rem 1rem;cursor:pointer;text-transform:uppercase}
                    .mgr{margin-bottom:1.5rem}
                    .ovcards{display:grid;grid-template-columns:repeat(auto-fit,minmax(14rem,1fr));gap:1rem;margin-bottom:1rem}
                    .ovcard,.ovpanel{background:var(--paper);border:1px solid var(--divider);border-radius:8px;padding:1rem 1.2rem}
                    .ovcard .lbl2{color:var(--text2);font-size:.9em}.ovcard .big{font-size:2.2em;font-weight:700;line-height:1.1}.ovcard .sub{color:var(--text2);font-size:.85em;margin-top:.6rem}
                    .ovcard .big.ok{color:var(--ok)}.ovcard .big.warn{color:var(--err)}
                    .ovpanel{margin-bottom:1rem}.ovpanel h3{margin:0 0 .75rem;font-size:1.1em}
                    .ovpanel .hd{display:flex;justify-content:space-between;align-items:center}.ovpanel .hd a{color:var(--primary);cursor:pointer;font-size:.85em}
                    .ovrow{display:flex;justify-content:space-between;align-items:center;gap:1rem;padding:.4rem 0;border-bottom:1px solid var(--divider)}.ovrow:last-child{border-bottom:0}
                    .ovbar{flex:0 0 9rem;height:6px;background:var(--divider);border-radius:3px;overflow:hidden;margin-right:auto;margin-left:1rem}.ovbar i{display:block;height:100%;background:var(--primary)}
                    .ovtwo{display:grid;grid-template-columns:repeat(auto-fit,minmax(24rem,1fr));gap:1rem}
                    .ovlog{font-family:monospace;font-size:.8em;white-space:pre-wrap;word-break:break-all}
                    .badge{font-size:.75em;border:1px solid var(--ok);color:var(--ok);border-radius:3px;padding:0 .4rem}.badge.bad{border-color:var(--err);color:var(--err)}
                    .mgr h3{display:flex;align-items:center;justify-content:space-between;margin:0 0 .5rem;font-weight:400;font-size:1.3em}
                    .mgr .btn{font-size:.85em}
                    td button{background:transparent;border:1px solid var(--primary);color:var(--primary);border-radius:4px;padding:.15rem .6rem;cursor:pointer}
                    td button:disabled{opacity:.4;cursor:default}
                    </style>
                    </head><body>
                    <section id="login" class="hidden"><div class="card"><h1>ioBroker-NewGen</h1><form id="loginForm"><label><span data-i18n="user.username">Benutzername</span><input id="user" autocomplete="username" required/></label><label><span data-i18n="user.password">Passwort</span><input id="pass" type="password" autocomplete="current-password" required/></label><button type="submit" data-i18n="user.login">Anmelden</button> <span id="loginError" class="error"></span> <select class="langsel" title="Language"></select></form></div></section>
                    <section id="setup" class="hidden"><div class="card setupcard"><h2 data-i18n="setup.title">Wichtige Haupteinstellungen</h2><p class="muted" data-i18n="setup.hint">Diese Angaben werden in der globalen Konfiguration gespeichert und können später unter „Konfiguration“ geändert werden.</p><form id="setupForm"></form></div></section>
                    <section id="firstrun" class="hidden"><div class="card"><h2 data-i18n="user.firstRunTitle">Administrator-Passwort festlegen</h2><p data-i18n="user.firstRunHint">Willkommen! Bitte legen Sie ein Passwort für den Benutzer „admin“ fest.</p><form id="firstForm"><label><span data-i18n="user.newPassword">Neues Passwort (min. 8 Zeichen)</span><input id="firstPass" type="password" autocomplete="new-password" required/></label><label><span data-i18n="user.confirmPassword">Passwort wiederholen</span><input id="firstPass2" type="password" autocomplete="new-password" required/></label><button type="submit" data-i18n="user.setPassword">Passwort setzen</button> <span id="firstError" class="error"></span></form></div></section>
                    <section id="change" class="hidden"><div class="card"><h2 data-i18n="user.changePassword">Passwort ändern</h2><p data-i18n="user.changePasswordHint">Das Passwort muss vor der weiteren Nutzung geändert werden.</p><form id="changeForm"><label><span data-i18n="user.currentPassword">Aktuelles Passwort</span><input id="oldPass" type="password" autocomplete="current-password" required/></label><label><span data-i18n="user.newPassword">Neues Passwort (min. 8 Zeichen)</span><input id="newPass" type="password" autocomplete="new-password" required/></label><button type="submit" data-i18n="user.change">Ändern</button> <span id="changeError" class="error"></span></form></div></section>
                    <div id="app" class="hidden">
                    <header class="appbar"><button id="menuBtn" class="iconbtn" type="button" title="Menu">&#9776;</button><span class="title">ioBroker-NewGen</span><span id="status"></span><select class="langsel" title="Language"></select><button id="themeBtn" class="iconbtn" type="button" title="Theme">&#9680;</button><span id="who"></span><button id="logout" class="iconbtn" type="button" title="Logout">&#x23FB;</button></header>
                    <div class="layout">
                    <nav class="drawer"><div class="items">
                    <div class="item sel" data-panel="overview"><span class="ico">&#9638;</span><span class="lbl" data-i18n="menu.overview">Übersicht</span></div>
                    <div class="item" data-panel="adapters"><span class="ico">&#9638;</span><span class="lbl" data-i18n="menu.adapters">Adapter</span></div>
                    <div class="item" data-panel="instances"><span class="ico">&#9998;</span><span class="lbl" data-i18n="menu.instances">Instanzen</span></div>
                    <div class="item" data-panel="objects"><span class="ico">&#9776;</span><span class="lbl" data-i18n="menu.objects">Objekte</span></div>
                    <div class="item" data-panel="config"><span class="ico">&#9881;</span><span class="lbl" data-i18n="menu.settings">Konfiguration</span></div>
                    <div class="item" data-panel="roles"><span class="ico">&#9919;</span><span class="lbl" data-i18n="menu.roles">Rollen</span></div>
                    <div class="item" data-panel="users"><span class="ico">&#9786;</span><span class="lbl" data-i18n="menu.users">Benutzer/Rollen</span></div>
                    </div><div class="foot">ioBroker-NewGen</div></nav>
                    <main class="content">
                    <section class="panel" data-panel="overview"><div class="toolbar"><h2 data-i18n="menu.overview">Übersicht</h2><span class="muted" data-i18n="ov.subtitle">Systemüberblick und Status</span></div><div class="body"><div id="ovBox">Lade...</div></div></section>
                    <section class="panel hidden" data-panel="adapters"><div class="toolbar"><h2 data-i18n="menu.adapters">Adapter</h2><div class="tabs" data-for="adapters"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div><button id="legacyUpBtn" type="button" class="btn hidden" onclick="$('legacyFile').click()" data-i18n="action.installLegacy">+ Legacy-Adapter (ZIP)</button><input id="legacyFile" type="file" accept=".zip,application/zip" class="hidden"></div><div class="body"><div id="legacyList"></div><div id="adaptersTable">Lade...</div><pre id="adapters" class="hidden">Lade...</pre></div></section>
                    <section class="panel hidden" data-panel="instances"><div class="toolbar"><h2 data-i18n="menu.instances">Instanzen</h2><button id="addInstBtn" type="button" class="btn hidden" onclick="instDialog()">+ Instanz hinzufügen</button></div><div class="body"><div id="instList">Lade...</div><div id="cfgView" class="hidden cfgview"></div></div></section>
                    <section class="panel hidden" data-panel="objects"><div class="toolbar"><h2 data-i18n="menu.objects">Objekte</h2><div class="tabs" data-for="states"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div></div><div class="body"><div id="statesTable">Lade...</div><pre id="states" class="hidden">Lade...</pre></div></section>
                    <section class="panel hidden" data-panel="config"><div class="toolbar"><h2 data-i18n="menu.settings">Konfiguration</h2><div class="tabs" data-for="config"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div></div><div class="body"><div id="configTable">Lade...</div><pre id="config" class="hidden">Lade...</pre></div></section>
                    <section class="panel hidden" data-panel="roles"><div class="toolbar"><h2 data-i18n="menu.roles">Rollen</h2><div class="tabs" data-for="roles"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div></div><div class="body"><div id="rolesTable">Lade...</div><pre id="roles" class="hidden">Lade...</pre></div></section>
                    <section class="panel hidden" data-panel="users"><div class="toolbar"><h2 data-i18n="menu.users">Benutzer/Rollen</h2><div class="tabs" data-for="users"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div></div><div class="body"><div id="manage" class="hidden"><div id="userMgr"></div><div id="roleMgr"></div></div><div id="usersTable">Lade...</div><pre id="users" class="hidden">Lade...</pre></div></section>
                    </main></div></div>
                    <div id="taskDock"></div>
                    <div id="taskWin" class="hidden" role="dialog" aria-live="polite"><header><div id="taskSpin" class="spin"></div><h3 id="taskTitle"></h3><button id="taskMin" type="button" title="Minimieren">&#8211;</button></header><div id="taskBar"><i></i></div><pre id="taskLog"></pre><div id="taskFoot"><span id="taskMsg"></span><button id="taskClose" type="button" class="btn hidden">Schließen</button></div></div>
                    <script>
                    const $=id=>document.getElementById(id);let timer=null;
                    function nav(name){document.querySelectorAll('.panel').forEach(p=>p.classList.toggle('hidden',p.dataset.panel!==name));document.querySelectorAll('.drawer .item').forEach(i=>i.classList.toggle('sel',i.dataset.panel===name));localStorage.setItem('ngPanel',name);history.replaceState(null,'','#tab-'+name);}
                    document.querySelectorAll('.drawer .item').forEach(i=>i.onclick=()=>nav(i.dataset.panel));
                    nav(location.hash.startsWith('#tab-')&&document.querySelector('.drawer .item[data-panel="'+location.hash.slice(5)+'"]')?location.hash.slice(5):document.querySelector('.drawer .item[data-panel="'+localStorage.getItem('ngPanel')+'"]')?localStorage.getItem('ngPanel'):'overview');
                    function setCompact(on){document.body.classList.toggle('compact',on);localStorage.setItem('ngCompact',on?'1':'');}
                    setCompact(!!localStorage.getItem('ngCompact'));
                    $('menuBtn').onclick=()=>setCompact(!document.body.classList.contains('compact'));
                    function setDark(on){document.documentElement.classList.toggle('dark',on);localStorage.setItem('ngTheme',on?'dark':'light');}
                    setDark((localStorage.getItem('ngTheme')||(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'))==='dark');
                    $('themeBtn').onclick=()=>setDark(!document.documentElement.classList.contains('dark'));
                    let i18n={};const t=(k,d)=>i18n[k]||d||k;
                    function applyI18n(){document.querySelectorAll('[data-i18n]').forEach(e=>{e.textContent=t(e.dataset.i18n,e.textContent);});document.querySelectorAll('.tabs button').forEach(b=>{b.textContent=t('view.'+b.dataset.view,b.textContent);});document.documentElement.lang=(i18n['language.code']||curLang||'de').slice(0,2);}
                    let curLang=localStorage.getItem('ngLang')||'';
                    async function loadI18n(lang){try{const r=await fetch('/api/i18n/translations',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(lang?{language:lang}:{})});const d=await r.json();i18n=d.translations||{};curLang=d.language||curLang;}catch(e){}applyI18n();if(typeof refresh==='function'&&!$('app').classList.contains('hidden'))refresh();}
                    async function initLanguages(){try{const r=await fetch('/api/i18n/languages');const d=await r.json();if(!curLang)curLang=d.language;document.querySelectorAll('.langsel').forEach(s=>{s.replaceChildren(...d.languages.map(l=>{const o=document.createElement('option');o.value=l.code;o.textContent=l.name+' ('+l.code+')';return o;}));s.value=curLang;s.onchange=()=>{curLang=s.value;localStorage.setItem('ngLang',curLang);document.querySelectorAll('.langsel').forEach(x=>x.value=curLang);loadI18n(curLang);};});}catch(e){}await loadI18n(curLang);}
                    initLanguages();
                    function show(name){for(const id of ['login','change','setup','app','firstrun'])$(id).classList.toggle('hidden',id!==name);if(name!=='app'&&timer){clearInterval(timer);timer=null;}}
                    document.querySelectorAll('.tabs').forEach(tabs=>tabs.querySelectorAll('button').forEach(button=>{button.onclick=()=>{const id=tabs.dataset.for;$(id+'Table').classList.toggle('hidden',button.dataset.view!=='table');$(id).classList.toggle('hidden',button.dataset.view!=='json');tabs.querySelectorAll('button').forEach(x=>x.classList.toggle('active',x===button));};}));
                    const treeData={};
                    function expandKeys(v){if(Array.isArray(v))return v.map(expandKeys);if(v&&typeof v==='object'){const out={};for(const [k,val] of Object.entries(v)){const e=expandKeys(val);const parts=k.split('.').filter(p=>p!=='');if(parts.length<2){out[k]=e;continue;}let cur=out,ok=true;for(let i=0;i<parts.length-1;i++){const p=parts[i];if(!(p in cur))cur[p]={};else if(cur[p]===null||typeof cur[p]!=='object'||Array.isArray(cur[p])){ok=false;break;}cur=cur[p];}const last=parts[parts.length-1];if(ok&&!(last in cur))cur[last]=e;else out[k]=e;}return out;}return v;}
                    function itemLabel(x,i){if(x&&typeof x==='object'){for(const n of ['name','username','id','Name','Id'])if(typeof x[n]==='string'&&x[n])return x[n];}return String(i);}
                    function renderTree(boxId,data){treeData[boxId]=data;const box=$(boxId+'Table');
                    if(data&&!Array.isArray(data)&&typeof data==='object'&&typeof data.error==='string'){box.textContent=data.error;return;}
                    if(data==null||(typeof data==='object'&&Object.keys(data).length===0)){                    box.textContent=t('ui.noData','Keine Daten');return;}
                    const table=document.createElement('table');table.className='tree';const head=table.createTHead().insertRow();for(const title of [t('table.key','Schlüssel'),t('table.value','Wert')]){const th=document.createElement('th');th.textContent=title;head.appendChild(th);}
                    const body=table.createTBody();
                    function entriesOf(v){return Array.isArray(v)?v.map((x,i)=>[itemLabel(x,i),x]):Object.entries(v);}
                    function addRows(value,label,depth,key){const container=value!==null&&typeof value==='object';const entries=container?entriesOf(value):[];const expandable=entries.length>0;const stateKey=boxId+':'+key;const open=expandable&&(treeToggled.has(stateKey)?depth>=1:depth<1);
                    const row=body.insertRow();const cell=row.insertCell();cell.className='tree-id';cell.style.paddingLeft=(0.6+depth*1.2)+'rem';const arrow=document.createElement('span');arrow.className='arrow';arrow.textContent=expandable?(open?'▼':'▶'):'';cell.appendChild(arrow);const text=document.createElement('span');text.textContent=label;if(container)text.className='folder';cell.appendChild(text);
                    if(expandable){row.style.cursor='pointer';row.onclick=()=>{if(treeToggled.has(stateKey))treeToggled.delete(stateKey);else treeToggled.add(stateKey);renderTree(boxId,treeData[boxId]);};}
                    const valueCell=row.insertCell();if(container){valueCell.className='muted';valueCell.textContent=Array.isArray(value)?'['+value.length+']':'{'+entries.length+'}';}else{valueCell.textContent=value===null?'null':String(value);}
                    if(open)for(const [k,x] of entries)addRows(x,k,depth+1,key+'/'+k);}
                    const top=expandKeys(data);if(top!==null&&typeof top==='object'){for(const [k,x] of entriesOf(top))addRows(x,k,0,k);}else{addRows(top,'Wert',0,'value');}
                    box.replaceChildren(table);}
                    const treeToggled=new Set();
                    function fmtDate(v){if(!v)return '';const d=new Date(v);return isNaN(d)?String(v):d.toLocaleString('de-DE');}
                    const perms=new Set();
                    function fmtDur(s){s=Math.max(0,Math.floor(Number(s)||0));const d=Math.floor(s/86400),h=Math.floor(s%86400/3600),m=Math.floor(s%3600/60),x=s%60;return (d?d+'d ':'')+(d||h?h+'h ':'')+(d||h||m?m+'m ':'')+x+'s';}
                    async function adapterAction(id,action,button){button.disabled=true;try{await api('/api/adapters/'+action,{adapterId:id});}catch(error){alert(error.message);}refresh();}
                    function cfgLabel(v,d){if(v==null)return d||'';if(typeof v==='string')return t(v,v);const l=(curLang||'en').slice(0,2);return v[curLang]||v[l]||v.en||Object.values(v)[0]||d||'';}
                    function cfgCond(src,data){if(src===true||src===false)return src;if(typeof src!=='string'||!src.trim())return false;
                    const tok=src.match(/'[^']*'|\x22[^\x22]*\x22|===|!==|==|!=|<=|>=|&&|\|\||[()!<>]|[\w.]+/g)||[];let i=0;
                    const get=p=>{const parts=p.split('.');if(parts[0]!=='data'&&parts[0]!=='globalData')return undefined;let v=data;for(const k of parts.slice(1)){if(v==null)return undefined;v=v[k];}return v;};
                    const prim=()=>{const x=tok[i++];if(x==='('){const v=or();i++;return v;}if(x==='!')return !prim();if(x===undefined)return undefined;if(/^['\x22]/.test(x))return x.slice(1,-1);if(x==='true')return true;if(x==='false')return false;if(x==='null')return null;if(x==='undefined')return undefined;if(!isNaN(Number(x)))return Number(x);return get(x);};
                    const cmp=()=>{let a=prim();while(['===','!==','==','!=','<','>','<=','>='].includes(tok[i])){const o=tok[i++];const b=prim();a=o==='==='?a===b:o==='!=='?a!==b:o==='=='?a==b:o==='!='?a!=b:o==='<'?a<b:o==='>'?a>b:o==='<='?a<=b:a>=b;}return a;};
                    const and=()=>{let a=cmp();while(tok[i]==='&&'){i++;const b=cmp();a=a&&b;}return a;};
                    const or=()=>{let a=and();while(tok[i]==='||'){i++;const b=and();a=a||b;}return a;};
                    try{return !!or();}catch{return false;}}
                    function cfgItems(node){const it=node.items;if(!it)return[];if(Array.isArray(it))return it.map(x=>[x.attr,x]);return Object.entries(it);}
                    function cfgBuild(parent,node,key,data,ctx){const type=node.type||'text';const attr=node.attr||key;const box=el('div',{className:'cfgitem'});parent.append(box);
                    const label=cfgLabel(node.label,attr);const help=cfgLabel(node.help,'');
                    const refresh2=()=>{box.classList.toggle('hidden',!!node.hidden&&cfgCond(node.hidden,data));const dis=!!node.disabled&&cfgCond(node.disabled,data);box.querySelectorAll('input,select,textarea').forEach(x=>x.disabled=dis);};ctx.updates.push(refresh2);
                    const done=()=>{if(help&&type!=='panel'&&type!=='tabs')box.append(el('small',{className:'muted',textContent:help}));ctx.updates.forEach(f=>f());};
                    if(type==='panel'){if(label&&node.label)box.append(el('h3',{textContent:label}));const grid=el('div',{className:'cfggrid'});box.append(grid);for(const [k,child] of cfgItems(node))cfgBuild(grid,child,k,data,ctx);return;}
                    if(type==='tabs'){const bar=el('div',{className:'cfgtabs'});const body=el('div');box.append(bar,body);const tabs=cfgItems(node);const panes=tabs.map(([k,child])=>{const pane=el('div',{className:'hidden'});cfgBuild(pane,{...child,type:'panel',label:undefined},k,data,ctx);body.append(pane);const b=el('button',{type:'button',textContent:cfgLabel(child.label,k)});bar.append(b);return[b,pane];});
                    const sel=n=>panes.forEach(([b,p],j)=>{b.classList.toggle('on',j===n);p.classList.toggle('hidden',j!==n);});sel(0);panes.forEach(([b],j)=>b.onclick=()=>sel(j));return;}
                    if(type==='header'||type==='staticText'){box.append(el(type==='header'?'h3':'p',{textContent:cfgLabel(node.text||node.label,'')}));return;}
                    if(type==='divider'){box.append(el('hr'));return;}
                    if(type==='checkbox'){const cb=el('input',{type:'checkbox',checked:!!data[attr]});cb.onchange=()=>{data[attr]=cb.checked;ctx.updates.forEach(f=>f());};box.append(el('label',{className:'chk'},cb,document.createTextNode(' '+label)));done();return;}
                    let input;
                    if(type==='select'){input=el('select');for(const o of node.options||[])input.append(el('option',{value:String(o.value),textContent:cfgLabel(o.label,String(o.value))}));input.value=String(data[attr]??'');input.onchange=()=>{const o=(node.options||[]).find(x=>String(x.value)===input.value);data[attr]=o?o.value:input.value;ctx.updates.forEach(f=>f());};}
                    else if(type==='textarea'){input=el('textarea',{rows:node.minRows||4,value:data[attr]==null?'':String(data[attr])});input.oninput=()=>{data[attr]=input.value;ctx.updates.forEach(f=>f());};}
                    else{const num=type==='number';input=el('input',{type:num?'number':type==='password'?'password':'text',value:data[attr]==null?'':String(data[attr])});if(num){if(node.min!=null)input.min=node.min;if(node.max!=null)input.max=node.max;}if(type==='password')input.autocomplete='new-password';input.oninput=()=>{data[attr]=num?(input.value===''?'':Number(input.value)):input.value;ctx.updates.forEach(f=>f());};}
                    field(box,label,input);done();}
                    function cfgFallback(values){const items={};for(const [k,v] of Object.entries(values))items[k]={type:typeof v==='boolean'?'checkbox':typeof v==='number'?'number':'text',label:k};return{type:'panel',items};}
                    let cfgOpen=null,lastAdapters=[],legacyPkgs=[];
                    function closeConfig(){cfgOpen=null;$('cfgView').classList.add('hidden');$('cfgView').replaceChildren();$('instList').classList.remove('hidden');renderInstances(lastAdapters);}
                    function instDialog(){const names=[...new Map(lastAdapters.filter(a=>a.Type==='Custom'&&a.AutoStartEditable).map(a=>[a.Name,a])).values()];const ov=el('div',{className:'overlay'});const dlg=el('div',{className:'card dlg'});const sel=el('select');for(const a of names)sel.append(el('option',{value:a.Id,textContent:a.Name}));for(const p of legacyPkgs)sel.append(el('option',{value:p.Name,textContent:p.Name+' ('+p.Version+', Legacy)'}));const err=el('div',{className:'error'});
                    const ok=el('button',{type:'button',textContent:t('action.add','Hinzufügen')});const cancel=el('button',{type:'button',className:'btn2',textContent:t('ui.cancel','Abbrechen'),onclick:()=>ov.remove()});
                    ok.onclick=async()=>{ok.disabled=true;err.textContent='';try{await api('/api/adapters/instances',{action:'add',adapterId:sel.value});ov.remove();refresh();}catch(x){err.textContent=x.message;ok.disabled=false;}};
                    dlg.append(el('h2',{textContent:t('action.addInstance','Instanz hinzufügen')}),field(dlg,t('table.id','Adapter'),sel),err,el('div',{className:'dlgbtn'},cancel,ok));ov.append(dlg);document.body.append(ov);}
                    function renderInstances(list){lastAdapters=Array.isArray(list)?list:[];$('addInstBtn').classList.toggle('hidden',!perms.has('SystemConfigure'));if(cfgOpen)return;const box=$('instList');const rows=lastAdapters.filter(a=>a.AutoStart||a.Status==='Connected'||a.Status==='Connecting').sort((x,y)=>String(x.Id).localeCompare(String(y.Id),undefined,{numeric:true}));if(!rows.length){box.textContent=t('ui.noInstances','Keine aktiven Instanzen. Mit „Instanz hinzufügen“ kann eine angelegt werden.');return;}
                    const table=document.createElement('table');table.className='tree';const head=table.createTHead().insertRow();for(const h of [t('table.id','Name'),t('table.type','Typ'),t('table.status','Status'),t('metric.uptime','Laufzeit'),t('table.actions','Aktionen')]){const th=document.createElement('th');th.textContent=h;head.appendChild(th);}
                    const body=table.createTBody();for(const a of rows){const r=body.insertRow();r.insertCell().textContent=a.Id;r.insertCell().textContent=a.Type;const sc=r.insertCell();sc.textContent=a.Status;sc.className='st-'+a.Status;r.insertCell().textContent=a.Status==='Connected'&&a.Metrics?fmtDur(a.Metrics.UptimeSeconds):'–';
                    actionCell(r,a,true);const ac=r.cells[r.cells.length-1];
                    if(perms.has('SystemConfigure')&&(a.AutoStartEditable||a.LegacyInstance)){const extra=a.LegacyInstance||!/\.0$/.test(a.Id);const rm=el('button',{type:'button',textContent:t(extra?'action.delete':'action.removeInstance',extra?'Löschen':'Instanz entfernen'),onclick:async()=>{if(!confirm(t('confirm.removeInstance','Instanz wirklich entfernen?')+' '+a.Id))return;rm.disabled=true;try{if(extra)await api('/api/adapters/instances',{action:'remove',adapterId:a.Id});else{await api('/api/adapters/autostart',{adapterId:a.Id,enabled:false});if(a.Status==='Connected'||a.Status==='Connecting')await api('/api/adapters/stop',{adapterId:a.Id});}}catch(error){alert(error.message);}refresh();}});ac.append(rm);}}box.replaceChildren(table);}
                    function oauthRow(a,o,data,err){const st=el('span',{className:'muted',textContent:o.connected?t('cfg.oauthConnected','Verbunden'):t('cfg.oauthNone','Nicht angemeldet')});const btn=el('button',{type:'button',textContent:t('cfg.oauthLogin','Anmelden')+' – '+o.label});const cancel=el('button',{type:'button',className:'btn2 hidden',textContent:t('ui.cancel','Abbrechen')});const link=el('div');let timer=0,sid='';
                    const stop=()=>{clearInterval(timer);timer=0;btn.disabled=false;cancel.classList.add('hidden');};
                    btn.onclick=async()=>{err.textContent='';link.replaceChildren();btn.disabled=true;try{await api('/api/adapters/config',{adapterId:a.Id,action:'save',values:data,restart:false});const s=await api('/api/adapters/oauth',{action:'start',adapterId:a.Id,provider:o.id});sid=s.sessionId;st.textContent=t('cfg.oauthWait','Warte auf Anmeldung im Browserfenster…');cancel.classList.remove('hidden');
                    if(!s.browserLaunched&&/^https:\/\//.test(s.authorizeUrl||''))link.append(el('small',{className:'muted',textContent:t('cfg.oauthManual','Kein isolierter Browser gefunden. Link manuell öffnen (nicht isoliert): ')}),el('a',{href:s.authorizeUrl,target:'_blank',rel:'noopener noreferrer',textContent:o.label}));
                    timer=setInterval(async()=>{if(cfgOpen!==a.Id){stop();return;}try{const r=await api('/api/adapters/oauth',{action:'status',sessionId:sid});if(r.state==='pending')return;stop();if(r.state==='success')openConfig(a,t('cfg.oauthOk','Anmeldung erfolgreich, Token gespeichert.'));else{st.textContent=t('cfg.oauthNone','Nicht angemeldet');err.textContent=r.message||r.state;}}catch(x){stop();err.textContent=x.message;}},1500);}catch(x){err.textContent=x.message;btn.disabled=false;}};
                    cancel.onclick=async()=>{try{await api('/api/adapters/oauth',{action:'cancel',sessionId:sid});}catch{}};
                    return el('div',{className:'oauth'},el('strong',{textContent:o.label+': '}),st,document.createTextNode(' '),btn,cancel,link,el('small',{className:'muted',textContent:t('cfg.redirect','Redirect-URI beim Anbieter registrieren: ')+o.redirectUri}));}
                    async function openConfig(a,note){let info;try{info=await api('/api/adapters/config',{adapterId:a.Id,action:'get'});}catch(e){alert(e.message);return;}
                    const data=JSON.parse(JSON.stringify(info.values||{}));const ctx={updates:[]};
                    cfgOpen=a.Id;const view=$('cfgView');const form=el('form',{className:'cfgform'});const err=el('div',{className:'error'});
                    cfgBuild(form,info.jsonConfig||cfgFallback(data),'',data,ctx);
                    const legacyNote=t('cfg.restartLegacy','Änderungen werden nach einem Neustart des Adapters wirksam.');
                    const restartNote=info.running?el('small',{className:'muted',textContent:info.autoRestart?t('cfg.restartNote','Der Adapter wird nach dem Speichern neu gestartet.'):legacyNote}):'';
                    const oauthBox=el('div');for(const o of info.oauth||[])oauthBox.append(oauthRow(a,o,data,err));
                    const doSave=async close=>{err.textContent='';try{const r=await api('/api/adapters/config',{adapterId:a.Id,action:'save',values:data});if(close)closeConfig();else openConfig(a,t('cfg.saved','Gespeichert.')+(r&&r.restartRequired?' '+legacyNote:''));}catch(x){err.textContent=x.message;}};
                    form.onsubmit=e=>{e.preventDefault();doSave(false);};
                    view.replaceChildren(el('h3',{textContent:a.Id}),form,oauthBox,restartNote,el('small',{className:'muted',textContent:note||''}),err,el('div',{className:'dlgbtn'},el('button',{type:'button',className:'btn2',textContent:t('cfg.close','Schließen'),onclick:closeConfig}),el('button',{type:'button',textContent:t('ui.save','Speichern'),onclick:()=>doSave(false)}),el('button',{type:'button',textContent:t('cfg.saveClose','Speichern und schließen'),onclick:()=>doSave(true)})));
                    $('instList').classList.add('hidden');view.classList.remove('hidden');nav('instances');}
                    function addInstance(a,btn){btn.disabled=true;(async()=>{try{const r=await api('/api/adapters/instances',{action:'add',adapterId:a.Id});alert(t('ui.instanceAdded','Instanz angelegt: ')+r.adapterId);}catch(error){alert(error.message);}refresh();})();}
                    async function loadLegacy(){try{const l=await api('/api/adapters/legacy');legacyPkgs=Array.isArray(l)?l:[];}catch{legacyPkgs=[];}renderLegacy();}
                    function renderLegacy(){const box=$('legacyList');$('legacyUpBtn').classList.toggle('hidden',!perms.has('AdapterInstall'));if(!legacyPkgs.length){box.replaceChildren();return;}
                    const table=document.createElement('table');table.className='tree';const head=table.createTHead().insertRow();for(const h of [t('table.id','Name'),t('table.version','Version'),t('table.title','Titel'),t('menu.instances','Instanzen'),t('table.actions','Aktionen')]){const th=document.createElement('th');th.textContent=h;head.appendChild(th);}
                    const body=table.createTBody();for(const p of legacyPkgs){const r=body.insertRow();r.insertCell().textContent=p.Name+' (Legacy)';r.insertCell().textContent=p.Version;const tc=r.insertCell();tc.textContent=p.Title;tc.title=p.Description||'';r.insertCell().textContent=(p.Instances||[]).join(', ')||'–';const ac=r.insertCell();
                    if(perms.has('SystemConfigure'))ac.append(el('button',{type:'button',textContent:'+ '+t('action.addInstance','Instanz hinzufügen'),onclick:async e=>{e.stopPropagation();e.target.disabled=true;try{const x=await api('/api/adapters/instances',{action:'add',adapterId:p.Name});alert(t('ui.instanceAdded','Instanz angelegt: ')+x.adapterId);}catch(err){alert(err.message);}refresh();}}),' ');
                    if(perms.has('AdapterInstall'))ac.append(el('button',{type:'button',textContent:t('action.uninstall','Deinstallieren'),onclick:async e=>{if(!confirm(t('confirm.uninstall','Adapter samt aller Instanzen und Einstellungen deinstallieren?')+' '+p.Name))return;e.target.disabled=true;try{await api('/api/adapters/legacy',{action:'uninstall',name:p.Name});}catch(err){alert(err.message);}refresh();}}));}
                    box.replaceChildren(el('h3',{textContent:t('ui.legacyInstalled','Installierte Legacy-Adapter')}),table);}
                    const tasks=new Map();let openTask=null,taskTimer=null;
                    function taskDone(x){return x.status!=='running';}
                    function renderDock(){const dock=$('taskDock');dock.replaceChildren();for(const x of tasks.values()){const chip=el('div',{className:'chip '+x.status,title:x.title,onclick:()=>showTask(x.id)});if(!taskDone(x))chip.append(el('div',{className:'spin'}));chip.append(el('span',{className:'t',textContent:(x.status==='done'?'✔ ':x.status==='error'?'✖ ':'')+x.title}));dock.append(chip);}}
                    function renderTaskWin(){const x=tasks.get(openTask);$('taskWin').classList.toggle('hidden',!x);if(!x)return;$('taskTitle').textContent=x.title;$('taskSpin').classList.toggle('hidden',taskDone(x));$('taskBar').className=x.status;const log=$('taskLog');const atEnd=log.scrollTop+log.clientHeight>=log.scrollHeight-8;log.textContent=x.log.join('\n');if(atEnd)log.scrollTop=log.scrollHeight;$('taskMsg').textContent=x.message||'';$('taskMsg').className=x.status==='error'?'error':'';$('taskClose').classList.toggle('hidden',!taskDone(x));}
                    function showTask(id){openTask=id;renderTaskWin();}
                    function minimizeTask(){openTask=null;renderTaskWin();}
                    function trackTask(id,open){if(!tasks.has(id))tasks.set(id,{id,title:'…',status:'running',log:[],total:0,message:null});if(open)openTask=id;renderDock();renderTaskWin();if(!taskTimer)taskTimer=setInterval(pollTasks,700);pollTasks();}
                    async function pollTasks(){let finished=false;for(const x of [...tasks.values()]){if(x.polling||taskDone(x)&&x.synced)continue;x.polling=true;try{const s=await api('/api/tasks',{id:x.id,since:x.log.length});const was=x.status;x.title=s.title;x.status=s.status;x.message=s.message;x.log.push(...s.log);if(taskDone(x)){x.synced=true;if(was==='running'&&!x.recalled)finished=true;}}catch(err){if(/nicht gefunden/.test(err.message)){tasks.delete(x.id);if(openTask===x.id)openTask=null;}}finally{x.polling=false;}}
                    renderDock();renderTaskWin();if(finished)refresh();if(![...tasks.values()].some(x=>!taskDone(x))&&taskTimer){clearInterval(taskTimer);taskTimer=null;}}
                    async function loadTasks(){try{const all=await api('/api/tasks');for(const s of all){if(tasks.has(s.id))continue;tasks.set(s.id,{id:s.id,title:s.title,status:s.status,message:s.message,log:s.log,total:s.total,recalled:true,synced:s.status!=='running'});}renderDock();if(all.some(s=>s.status==='running')&&!taskTimer)taskTimer=setInterval(pollTasks,700);}catch{}}
                    $('taskMin').onclick=minimizeTask;
                    $('taskClose').onclick=async()=>{const id=openTask;minimizeTask();if(!id)return;try{await api('/api/tasks',{action:'dismiss',id});}catch{}tasks.delete(id);renderDock();};
                    $('legacyFile').onchange=async e=>{const file=e.target.files[0];e.target.value='';if(!file)return;const btn=$('legacyUpBtn');btn.disabled=true;
                    try{const r=await fetch('/api/adapters/legacy',{method:'POST',credentials:'same-origin',headers:{'X-Requested-With':'ngadmin','Content-Type':'application/zip'},body:file});if(r.status===401){show('login');throw new Error('Nicht angemeldet');}const text=await r.text();let d=null;try{d=JSON.parse(text);}catch{}if(!r.ok)throw new Error((d&&d.error)||('HTTP '+r.status));
                    trackTask(d.taskId,true);}catch(err){alert(err.message);}btn.disabled=false;};
                                        function actionCell(row,a,inst){const cell=row.insertCell();const life=a.Type==='Custom'||a.LegacyInstance;if(!inst&&(a.Type==='Custom'&&a.AutoStartEditable||a.LegacyInstance)&&perms.has('SystemConfigure')){const add=el('button',{type:'button',textContent:'+ '+t('action.addInstance','Instanz hinzufügen'),onclick:e=>{e.stopPropagation();addInstance(a,add);}});cell.append(add,' ');}const canCfg=a.HasConfig&&perms.has('SystemConfigure');if(!life&&!canCfg)return;if(canCfg){const c=document.createElement('button');c.type='button';c.textContent=t('action.configure','Konfiguration');c.onclick=e=>{e.stopPropagation();openConfig(a);};cell.appendChild(c);cell.appendChild(document.createTextNode(' '));}if(!life)return;const status=a.Status;const defs=[['start','AdapterStart',status!=='Connected'&&status!=='Connecting'],['stop','AdapterStop',status==='Connected'],['restart','AdapterRestart',status==='Connected']];for(const [action,perm,enabled] of defs){if(!perms.has(perm))continue;const b=document.createElement('button');b.type='button';b.textContent=t('action.'+action,action);b.disabled=!enabled;b.onclick=e=>{e.stopPropagation();if((action==='stop'||action==='restart')&&/^admin(\.|$)/i.test(String(a.Id))&&!confirm(t(action==='stop'?'confirm.stopAdmin':'confirm.restartAdmin',action==='stop'?'Achtung: Damit wird das komplette Admin-UI gestoppt und Sie verlieren den Zugriff, bis es neu gestartet wird. Wirklich stoppen?':'Achtung: Das komplette Admin-UI wird neu gestartet und ist kurzzeitig nicht erreichbar. Wirklich neu starten?')))return;adapterAction(a.Id,action,b);};cell.appendChild(b);cell.appendChild(document.createTextNode(' '));}}
                    function autoCell(cell,a){if(!a.AutoStartEditable)return;const cb=document.createElement('input');cb.type='checkbox';cb.checked=!!a.AutoStart;cb.disabled=true;cb.style.pointerEvents='none';cell.style.cursor='pointer';cell.title=t('table.autostart','Autostart');cell.appendChild(cb);
                    const lock=()=>{cb.disabled=true;cb.style.pointerEvents='none';document.removeEventListener('click',outside,true);};
                    const outside=e=>{if(!cell.isConnected||!cell.contains(e.target))lock();};
                    cell.onclick=e=>{e.stopPropagation();if(!perms.has('SystemConfigure')||!cb.disabled)return;cb.disabled=false;cb.style.pointerEvents='auto';document.addEventListener('click',outside,true);};
                    cb.onchange=async()=>{const wanted=cb.checked;cb.disabled=true;try{await api('/api/adapters/autostart',{adapterId:a.Id,enabled:wanted});a.AutoStart=wanted;}catch(error){cb.checked=!wanted;alert(error.message);}cb.disabled=false;};}
                    const showActions=()=>['AdapterStart','AdapterStop','AdapterRestart','SystemConfigure'].some(p=>perms.has(p));
                    function renderAdapters(list){const box=$('adaptersTable');if(!Array.isArray(list)){box.textContent=(list&&list.error)||'Keine Daten';return;}if(!list.length){box.textContent=t('ui.noAdapters','Keine Adapter registriert.');return;}
                    const cols=[[t('table.id','Name'),a=>a.Name],[t('table.type','Typ'),a=>a.Type],[t('table.status','Status'),a=>a.Status],[t('table.tcpEndpoint','TCP-Endpoint'),a=>a.TcpEndpoint==null?'':(typeof a.TcpEndpoint==='object'?JSON.stringify(a.TcpEndpoint):a.TcpEndpoint)],[t('table.registered','Registriert'),a=>fmtDate(a.RegisteredAt)],[t('table.lastSeen','Zuletzt gesehen'),a=>fmtDate(a.LastSeenAt)],[t('table.autostart','Autostart'),a=>'']];
                    const root={name:'',path:'',children:new Map(),item:null};
                    for(const a of list){let node=root;let path='';for(const part of String(a.Id).split('.')){path=path?path+'.'+part:part;if(!node.children.has(part))node.children.set(part,{name:part,path,children:new Map(),item:null});node=node.children.get(part);}node.item=a;}
                    const table=document.createElement('table');table.className='tree';const head=table.createTHead().insertRow();for(const title of ['ID',...cols.map(c=>c[0]),...(showActions()?[t('table.actions','Aktionen')]:[])]){const th=document.createElement('th');th.textContent=title;head.appendChild(th);}
                    const body=table.createTBody();
                    const isOpen=(key,def)=>treeToggled.has(key)?!def:def;
                    function toggle(key){if(treeToggled.has(key))treeToggled.delete(key);else treeToggled.add(key);renderAdapters(list);}
                    function labelCell(row,text,depth,expandable,open,key,cls){const cell=row.insertCell();cell.className='tree-id';cell.style.paddingLeft=(0.6+depth*1.2)+'rem';const arrow=document.createElement('span');arrow.className='arrow';arrow.textContent=expandable?(open?'▼':'▶'):'';if(expandable){arrow.style.cursor='pointer';row.style.cursor='pointer';row.onclick=()=>toggle(key);}cell.appendChild(arrow);const label=document.createElement('span');label.textContent=text;if(cls)label.className=cls;cell.appendChild(label);}
                    function addProps(item,depth,key){const meta=item.Metadata&&typeof item.Metadata==='object'?Object.entries(item.Metadata):[];const m=item.Metrics;if(m){const mv=[['metric.uptime','Laufzeit',item.Status==='Connected'?fmtDur(m.UptimeSeconds):'–'],['metric.startedAt','Gestartet',fmtDate(m.StartedAt)],['metric.stoppedAt','Gestoppt',fmtDate(m.StoppedAt)],['metric.lastChange','Letzter Statuswechsel',fmtDate(m.LastStatusChangeAt)],['metric.starts','Starts',m.StartCount],['metric.restarts','Neustarts',m.RestartCount],['metric.faults','Fehler',m.FaultCount],['metric.lastError','Letzter Fehler',m.LastError?m.LastError+' ('+fmtDate(m.LastErrorAt)+')':'']];for(const [k,d,v] of mv){const row=body.insertRow();row.className='prop';labelCell(row,t(k,d),depth,false,false,key+'#'+k,'prop-name');const cell=row.insertCell();cell.colSpan=cols.length+(showActions()?1:0);cell.textContent=String(v);}}for(const [k,v] of meta){const row=body.insertRow();row.className='prop';labelCell(row,k,depth,false,false,key+'#'+k,'prop-name');const cell=row.insertCell();cell.colSpan=cols.length+(showActions()?1:0);cell.textContent=typeof v==='object'?JSON.stringify(v):String(v);}}
                    function addNode(node,depth){const kids=[...node.children.values()].sort((x,y)=>x.name.localeCompare(y.name,undefined,{numeric:true}));const hasProps=!!(node.item&&((node.item.Metadata&&Object.keys(node.item.Metadata).length)||node.item.Metrics));const expandable=kids.length>0||hasProps;const open=isOpen(node.path,!node.item);
                    const row=body.insertRow();labelCell(row,node.name,depth,expandable,open,node.path,node.item?'':'folder');
                    if(node.item){cols.forEach(([title,get],i)=>{const cell=row.insertCell();if(i===cols.length-1){autoCell(cell,node.item);return;}cell.textContent=get(node.item);if(i===2)cell.className='st-'+node.item.Status;});if(showActions())actionCell(row,node.item);}
                    else{const cell=row.insertCell();cell.colSpan=cols.length+(showActions()?1:0);cell.className='muted';cell.textContent=kids.length+' Einträge';}
                    if(open){if(hasProps)addProps(node.item,depth+1,node.path);for(const kid of kids)addNode(kid,depth+1);}}
                    for(const kid of [...root.children.values()].sort((x,y)=>x.name.localeCompare(y.name,undefined,{numeric:true})))addNode(kid,0);
                    box.replaceChildren(table);}
                    async function api(path,body){const options={credentials:'same-origin',headers:{'X-Requested-With':'ngadmin'}};if(body!==undefined){options.method='POST';options.headers['Content-Type']='application/json';options.body=JSON.stringify(body);}const response=await fetch(path,options);if(response.status===401){show('login');throw new Error('Nicht angemeldet');}const text=await response.text();let data=null;try{data=text?JSON.parse(text):null;}catch{}if(!response.ok)throw new Error((data&&data.error)||('HTTP '+response.status));return data;}
                    let lastUsers=[],lastRoles=[];
                    function el(tag,props,...kids){const e=document.createElement(tag);Object.assign(e,props||{});for(const k of kids)e.append(k);return e;}
                    function field(form,label,input){form.append(el('label',{},el('span',{textContent:label}),input));return input;}
                    function flag(form,label,val){const cb=el('input',{type:'checkbox',checked:val});form.append(el('label',{className:'chk'},cb,document.createTextNode(' '+label)));return cb;}
                    function checks(form,label,names,selected){const wrap=el('fieldset',{className:'checks'},el('legend',{textContent:label}));if(!names.length)wrap.append(el('span',{className:'muted',textContent:'–'}));for(const n of names)wrap.append(el('label',{className:'chk'},el('input',{type:'checkbox',value:n,checked:selected.includes(n)}),document.createTextNode(n)));form.append(wrap);return()=>[...wrap.querySelectorAll('input:checked')].map(x=>x.value);}
                    function dialog(title,build,save){const err=el('div',{className:'error'});const form=el('form');build(form);const ok=el('button',{type:'submit',textContent:t('ui.save','Speichern')});const cancel=el('button',{type:'button',className:'btn2',textContent:t('ui.cancel','Abbrechen')});form.append(err,el('div',{className:'dlgbtn'},cancel,ok));const ov=el('div',{className:'overlay'},el('div',{className:'card dlg'},el('h2',{textContent:title}),form));cancel.onclick=()=>ov.remove();form.onsubmit=async e=>{e.preventDefault();err.textContent='';ok.disabled=true;try{await save();ov.remove();refresh();}catch(x){err.textContent=x.message;ok.disabled=false;}};document.body.append(ov);}
                    function userDialog(u){const isNew=!u;const f={};const names=Array.isArray(lastRoles)?lastRoles.map(r=>r.name):[];
                    dialog(isNew?t('ui.newUser','Neuer Benutzer'):t('ui.editUser','Benutzer bearbeiten')+': '+u.username,form=>{
                    f.name=field(form,t('user.username','Benutzername'),el('input',{value:u?u.username:'',disabled:!isNew,required:true,autocomplete:'off'}));
                    f.disp=field(form,t('user.displayName','Anzeigename'),el('input',{value:u&&u.displayName||''}));
                    f.pass=field(form,isNew?t('user.password','Passwort'):t('user.newPasswordOptional','Neues Passwort (leer = unverändert)'),el('input',{type:'password',autocomplete:'new-password',required:isNew}));
                    f.must=flag(form,t('user.mustChange','Passwortänderung bei nächster Anmeldung'),isNew||!!(u&&u.mustChangePassword));
                    f.en=flag(form,t('user.enabled','Aktiv'),isNew||u.isEnabled);
                    f.roles=checks(form,t('menu.roles','Rollen'),names,u?u.roles||[]:[]);},
                    ()=>api('/api/security/users',{action:isNew?'create':'update',username:f.name.value.trim(),displayName:f.disp.value,password:f.pass.value,mustChangePassword:f.must.checked,enabled:f.en.checked,roles:f.roles()}));}
                    async function roleDialog(r){let all=[];try{all=await api('/api/security/roles',{action:'permissions'});}catch(e){alert(e.message);return;}const isNew=!r;const f={};
                    dialog(isNew?t('ui.newRole','Neue Gruppe'):t('ui.editRole','Gruppe bearbeiten')+': '+r.name,form=>{
                    f.name=field(form,t('table.name','Name'),el('input',{value:r?r.name:'',disabled:!isNew,required:true,autocomplete:'off'}));
                    f.desc=field(form,t('table.description','Beschreibung'),el('input',{value:r&&r.description||''}));
                    f.perms=checks(form,t('table.permissions','Berechtigungen'),all,r?r.permissions||[]:[]);},
                    ()=>api('/api/security/roles',{action:'save',name:f.name.value.trim(),description:f.desc.value,permissions:f.perms()}));}
                    async function removeEntry(path,body,question){if(!confirm(question))return;try{await api(path,body);}catch(e){alert(e.message);}refresh();}
                    function mgrTable(headers,rows){const table=el('table');const hr=table.createTHead().insertRow();for(const h of headers)hr.append(el('th',{textContent:h}));const tb=table.createTBody();for(const cells of rows){const tr=tb.insertRow();for(const c of cells)tr.insertCell().append(c);}return table;}
                    function mgrBtn(text,fn,disabled){return el('button',{type:'button',textContent:text,disabled:!!disabled,onclick:fn});}
                    function renderManage(){const canU=perms.has('UserManage'),canR=perms.has('RoleManage');$('manage').classList.toggle('hidden',!canU&&!canR);const um=$('userMgr'),rm=$('roleMgr');um.replaceChildren();rm.replaceChildren();
                    if(canU&&Array.isArray(lastUsers)){um.className='mgr';um.append(el('h3',{},el('span',{textContent:t('menu.users','Benutzer')}),el('button',{type:'button',className:'btn',textContent:'+ '+t('ui.newUser','Neuer Benutzer'),onclick:()=>userDialog(null)})));
                    um.append(mgrTable([t('user.username','Benutzername'),t('user.displayName','Anzeigename'),t('user.enabled','Aktiv'),t('menu.roles','Rollen'),t('table.actions','Aktionen')],lastUsers.map(u=>[u.username,u.displayName||'',u.isEnabled?'✓':'✗',(u.roles||[]).join(', '),el('span',{},mgrBtn(t('action.edit','Bearbeiten'),()=>userDialog(u)),' ',mgrBtn(t('action.delete','Löschen'),()=>removeEntry('/api/security/users',{action:'delete',username:u.username},t('confirm.deleteUser','Benutzer wirklich löschen?')+' '+u.username)))])));}
                    if(canR&&Array.isArray(lastRoles)){rm.className='mgr';rm.append(el('h3',{},el('span',{textContent:t('ui.groups','Gruppen')}),el('button',{type:'button',className:'btn',textContent:'+ '+t('ui.newRole','Neue Gruppe'),onclick:()=>roleDialog(null)})));
                    rm.append(mgrTable([t('table.name','Name'),t('table.description','Beschreibung'),t('table.permissions','Berechtigungen'),t('table.actions','Aktionen')],lastRoles.map(r=>[r.name,r.description||'',(r.permissions||[]).join(', '),r.builtin?el('span',{className:'muted',textContent:t('ui.builtin','vordefiniert')}):el('span',{},mgrBtn(t('action.edit','Bearbeiten'),()=>roleDialog(r)),' ',mgrBtn(t('action.delete','Löschen'),()=>removeEntry('/api/security/roles',{action:'delete',name:r.name},t('confirm.deleteRole','Gruppe wirklich löschen?')+' '+r.name))) ])));}}
                    async function loadJson(path){try{return await api(path);}catch(error){if(error.message==='Nicht angemeldet')throw error;return {error:error.message};}}
                    async function refresh(){try{const [states,adapters,config,roles,users]=await Promise.all([loadJson('/api/states'),loadJson('/api/adapters'),loadJson('/api/config'),loadJson('/api/roles'),loadJson('/api/users')]);$('states').textContent=JSON.stringify(states,null,2);renderTree('states',states);$('adapters').textContent=JSON.stringify(adapters,null,2);renderAdapters(adapters);refreshOverview(adapters);renderInstances(adapters);loadLegacy();$('config').textContent=JSON.stringify(config,null,2);renderTree('config',config);$('roles').textContent=JSON.stringify(roles,null,2);renderTree('roles',roles);$('users').textContent=JSON.stringify(users,null,2);renderTree('users',users);lastUsers=users;lastRoles=roles;renderManage();$('status').textContent=' Verbunden';}catch(error){$('status').textContent=' '+error.message;}}
                    const setupOptions={temperatureUnit:['°C','°F'],dateFormat:['DD.MM.YYYY','DD/MM/YYYY','MM/DD/YYYY','YYYY-MM-DD'],isFloatComma:[[true,'Komma - 3,14'],[false,'Punkt - 3.14']],firstDayOfWeek:[['monday','Montag'],['sunday','Sonntag'],['saturday','Samstag']]};
                    function setupSelect(form,label,key,opts,val){const s=el('select');for(const o of opts){const [v,l]=Array.isArray(o)?o:[o,o];s.append(el('option',{value:String(v),textContent:l}));}s.value=String(val);return field(form,label,s);}
                    function showSetup(cfg){const form=$('setupForm');form.replaceChildren();const g=k=>cfg['system.'+k];const err=el('div',{className:'error'});
                    const unit=setupSelect(form,t('setup.temperature','Temperatureinheit'),'temperatureUnit',setupOptions.temperatureUnit,g('temperatureUnit'));
                    const cur=field(form,t('setup.currency','Währung'),el('input',{type:'text',maxLength:10,value:g('currency')||''}));
                    const date=setupSelect(form,t('setup.dateFormat','Datumsformat'),'dateFormat',setupOptions.dateFormat,g('dateFormat'));
                    const comma=setupSelect(form,t('setup.separator','Trennzeichen in Zahlen'),'isFloatComma',setupOptions.isFloatComma,g('isFloatComma'));
                    const week=setupSelect(form,t('setup.weekStart','Die Woche beginnt mit'),'firstDayOfWeek',setupOptions.firstDayOfWeek,g('firstDayOfWeek'));
                    const country=field(form,t('setup.country','Land'),el('input',{type:'text',maxLength:100,value:g('country')||''}));
                    const city=field(form,t('setup.city','Stadt'),el('input',{type:'text',maxLength:100,value:g('city')||''}));
                    const addr=field(form,t('setup.address','Adresse'),el('input',{type:'text',maxLength:200,value:g('address')||''}));
                    form.append(el('small',{className:'muted',textContent:t('setup.addressHint','Wird nur zur Positionsberechnung verwendet.')}));
                    const lat=field(form,t('setup.latitude','Breitengrad'),el('input',{type:'text',inputMode:'decimal',value:g('latitude')||''}));
                    const lon=field(form,t('setup.longitude','Längengrad'),el('input',{type:'text',inputMode:'decimal',value:g('longitude')||''}));
                    const loc=el('button',{type:'button',className:'btn2',textContent:t('setup.locate','Position des Browsers verwenden')});
                    loc.onclick=()=>{if(!navigator.geolocation){err.textContent='Geolocation nicht verfügbar';return;}navigator.geolocation.getCurrentPosition(p=>{lat.value=p.coords.latitude.toFixed(6);lon.value=p.coords.longitude.toFixed(6);},e=>{err.textContent=e.message;});};
                    const skip=el('button',{type:'button',className:'btn2',textContent:t('setup.skip','Überspringen')});
                    const ok=el('button',{type:'submit',textContent:t('setup.save','Speichern und weiter')});
                    form.append(loc,err,el('div',{className:'dlgbtn'},skip,ok));
                    const finish=async extra=>{err.textContent='';ok.disabled=skip.disabled=true;try{await api('/api/config',{...extra,'system.setupDone':true});await start();}catch(x){err.textContent=x.message;ok.disabled=skip.disabled=false;}};
                    skip.onclick=()=>finish({});
                    form.onsubmit=e=>{e.preventDefault();finish({'system.temperatureUnit':unit.value,'system.currency':cur.value,'system.dateFormat':date.value,'system.isFloatComma':comma.value==='true','system.firstDayOfWeek':week.value,'system.country':country.value,'system.city':city.value,'system.address':addr.value,'system.latitude':lat.value,'system.longitude':lon.value});};
                    show('setup');}
                    const fmtBytes=b=>{b=Number(b)||0;return b>=1073741824?(b/1073741824).toFixed(1)+' GB':(b/1048576).toFixed(0)+' MB';};
                    function ovCard(label,big,sub,cls){return el('div',{className:'ovcard'},el('div',{className:'lbl2',textContent:label}),el('div',{className:'big '+(cls||''),textContent:String(big)}),el('div',{className:'sub',textContent:sub}));}
                    function ovRow(label,value,pct){const r=el('div',{className:'ovrow'},el('span',{textContent:label}));if(pct!=null)r.append(el('span',{className:'ovbar'},el('i',{style:'width:'+Math.max(0,Math.min(100,pct))+'%'})));r.append(el('span',{textContent:value}));return r;}
                    function ovPanel(title,link,target,...rows){const hd=el('div',{className:'hd'},el('h3',{textContent:title}));if(link){hd.append(el('a',{textContent:link,onclick:()=>nav(target)}));}return el('div',{className:'ovpanel'},hd,...rows);}
                    async function refreshOverview(adapters){const box=$('ovBox');if(document.querySelector('.panel[data-panel="overview"]').classList.contains('hidden'))return;try{const [o,logs]=await Promise.all([loadJson('/api/overview'),loadJson('/api/logs')]);if(o.error){box.textContent=o.error;return;}
                    const s=o.System,a=o.Adapters;const memPct=s.MemoryTotalBytes?s.MemoryUsedBytes/s.MemoryTotalBytes*100:0;
                    const cards=el('div',{className:'ovcards'},ovCard(t('ov.status','Systemstatus'),o.Status,o.Status==='OK'?t('ov.statusOk','System läuft stabil'):t('ov.statusWarn','Adapter mit Fehlern: ')+a.Faulted,o.Status==='OK'?'ok':'warn'),
                    ovCard(t('menu.adapters','Adapter'),a.Total,a.Active+' '+t('ov.active','aktiv')+' / '+a.Inactive+' '+t('ov.inactive','inaktiv')),
                    ovCard(t('menu.instances','Instanzen'),a.Instances,a.Active+' '+t('ov.active','aktiv')+' / '+Math.max(0,a.Instances-a.Active)+' '+t('ov.inactive','inaktiv')),
                    ovCard(t('ov.objects','Objekte'),o.Objects,o.States+' '+t('ov.states','Zustände')));
                    const info=ovPanel(t('ov.sysinfo','Systeminformationen'),null,null,ovRow(t('ov.platform','Plattform'),s.Platform),ovRow(t('ov.arch','Architektur'),s.Architecture),ovRow(t('ov.runtime','Laufzeit'),s.Runtime),ovRow(t('ov.host','Host'),s.Host+' ('+s.Cores+' '+t('ov.cores','Kerne')+')'),ovRow(t('ov.sysUptime','System-Betriebszeit'),fmtDur(s.SystemUptimeSeconds)),ovRow(t('ov.procUptime','Laufzeit des Prozesses'),fmtDur(s.ProcessUptimeSeconds)),ovRow('RAM',fmtBytes(s.MemoryUsedBytes)+' / '+fmtBytes(s.MemoryTotalBytes)+' ('+Math.round(memPct)+'%)',memPct),ovRow('CPU',s.CpuPercent+'%',s.CpuPercent));
                    const list=Array.isArray(adapters)?adapters:[];const act=ovPanel(t('ov.activeAdapters','Aktive Adapter'),t('ov.showAll','Alle anzeigen'),'adapters');
                    for(const x of list.filter(x=>x.Status==='Connected'||x.Status==='Running').slice(0,8)){const v=x.Metadata&&typeof x.Metadata==='object'&&x.Metadata.version?'v'+x.Metadata.version+' ':'';act.append(el('div',{className:'ovrow'},el('span',{textContent:x.Name}),el('span',{},v,el('span',{className:'badge',textContent:t('ov.active','aktiv')}))));}
                    if(!act.querySelector('.ovrow'))act.append(el('div',{className:'muted',textContent:t('ov.noActive','Keine aktiven Adapter')}));
                    const lines=(Array.isArray(logs)?logs:[]).flatMap(f=>f.lines||[]).slice(-8);const log=ovPanel(t('ov.syslog','Systemprotokoll'),null,null);
                    for(const l of lines)log.append(el('div',{className:'ovrow'},el('span',{className:'ovlog',textContent:l})));if(logs&&logs.error)log.append(el('div',{className:'muted',textContent:logs.error}));else if(!lines.length)log.append(el('div',{className:'muted',textContent:t('ov.noLog','Keine Protokolleinträge')}));
                    box.replaceChildren(cards,info,el('div',{className:'ovtwo'},act,log));}catch(error){box.textContent=error.message;}}
                    async function start(){try{const me=await api('/api/auth/me');if(me.mustChangePassword){show('change');return;}perms.clear();(me.permissions||[]).forEach(p=>perms.add(p));$('who').textContent='Angemeldet als '+(me.displayName||me.username)+' ('+me.roles.join(', ')+')';
                    if(perms.has('SystemConfigure')&&perms.has('SystemRead')){let cfg=null;try{cfg=await api('/api/config');}catch{}if(cfg&&cfg['system.setupDone']!==true){showSetup(cfg);return;}}
                    show('app');refresh();loadTasks();if(!timer)timer=setInterval(refresh,2000);}catch{let first=false;try{first=(await api('/api/auth/status')).firstRun===true;}catch{}show(first?'firstrun':'login');}}
                    $('firstForm').onsubmit=async e=>{e.preventDefault();$('firstError').textContent='';if($('firstPass').value!==$('firstPass2').value){$('firstError').textContent=t('user.passwordMismatch','Die Passwörter stimmen nicht überein');return;}try{await api('/api/auth/setup',{newPassword:$('firstPass').value});$('firstPass').value='';$('firstPass2').value='';start();}catch(error){$('firstError').textContent=error.message;}};
                    $('loginForm').onsubmit=async e=>{e.preventDefault();$('loginError').textContent='';try{await api('/api/auth/login',{username:$('user').value,password:$('pass').value});$('pass').value='';start();}catch(error){$('loginError').textContent=error.message;}};
                    $('changeForm').onsubmit=async e=>{e.preventDefault();$('changeError').textContent='';try{await api('/api/auth/change-password',{currentPassword:$('oldPass').value,newPassword:$('newPass').value});$('oldPass').value='';$('newPass').value='';start();}catch(error){$('changeError').textContent=error.message;}};
                    $('logout').onclick=async()=>{try{await api('/api/auth/logout',{});}catch{}tasks.clear();openTask=null;renderDock();renderTaskWin();show('login');};
                    start();
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

        private static IReadOnlyList<string>? ReadEnabledAdapters(IAdapterContext context)
        {
            try
            {
                var configured = context.Config.Get<string[]>("adapter.enabledAdapters");
                return configured is null
                    ? Array.Empty<string>()
                    : configured.Where(entry => !string.IsNullOrWhiteSpace(entry)).ToArray();
            }
            catch
            {
                // Ohne Filter in der Konfiguration starten alle geladenen Adapter.
                return null;
            }
        }

        private static bool IsAutoStart(IReadOnlyList<string>? enabled, AdapterRegistration adapter)
        {
            var primary = adapter.Id.EndsWith(".0", StringComparison.Ordinal);
            return enabled is null || enabled.Any(entry =>
                string.Equals(entry, adapter.Id, StringComparison.OrdinalIgnoreCase) ||
                (primary && string.Equals(entry, adapter.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private sealed class InstanceEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public InstanceEndpoint(IAdapterContext context) : base("/api/adapters/instances")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                string action;
                string adapterId;
                try
                {
                    using var document = JsonDocument.Parse(request.Body is { Length: > 0 } ? request.Body : Array.Empty<byte>());
                    action = document.RootElement.GetProperty("action").GetString() ?? string.Empty;
                    adapterId = document.RootElement.GetProperty("adapterId").GetString() ?? string.Empty;
                }
                catch (Exception)
                {
                    return Response(400, "{\"error\":\"Erwartet {action, adapterId}\"}", "application/json; charset=utf-8");
                }

                var registration = _context.AdapterRegistry.Get(adapterId);
                var legacy = _context.Legacy;
                if (legacy is not null && (legacy.IsInstalled(adapterId) || legacy.IsInstance(adapterId) ||
                                           registration?.Type == AdapterType.IoBroker))
                {
                    if (action == "add")
                    {
                        var legacyId = await _context.AddInstanceAsync(adapterId);
                        if (legacyId is null)
                        {
                            return Response(400, "{\"error\":\"Instanz konnte nicht angelegt werden\"}", "application/json; charset=utf-8");
                        }

                        await _context.StartAdapterAsync(legacyId);
                        return Response(200, JsonSerializer.Serialize(new { adapterId = legacyId }), "application/json; charset=utf-8");
                    }

                    if (action == "remove")
                    {
                        return await _context.RemoveInstanceAsync(adapterId)
                            ? Response(200, JsonSerializer.Serialize(new { adapterId }), "application/json; charset=utf-8")
                            : Response(400, "{\"error\":\"Instanz nicht gefunden\"}", "application/json; charset=utf-8");
                    }

                    return Response(400, "{\"error\":\"Unbekannte Aktion\"}", "application/json; charset=utf-8");
                }

                if (registration is null || registration.Type != AdapterType.Custom ||
                    string.Equals(registration.Name, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    return Response(400, "{\"error\":\"Für diesen Adapter sind keine weiteren Instanzen möglich\"}", "application/json; charset=utf-8");
                }

                if (action == "add")
                {
                    var id = await _context.AddInstanceAsync(adapterId);
                    if (id is null)
                    {
                        return Response(400, "{\"error\":\"Instanz konnte nicht angelegt werden\"}", "application/json; charset=utf-8");
                    }

                    SetAutoStart(_context, _context.AdapterRegistry.Get(id)!, true);
                    await _context.StartAdapterAsync(id);
                    return Response(200, JsonSerializer.Serialize(new { adapterId = id }), "application/json; charset=utf-8");
                }

                if (action == "remove")
                {
                    SetAutoStart(_context, registration, false);
                    if (!await _context.RemoveInstanceAsync(adapterId))
                    {
                        return Response(400, "{\"error\":\"Die erste Instanz (.0) kann nicht gelöscht werden\"}", "application/json; charset=utf-8");
                    }

                    return Response(200, JsonSerializer.Serialize(new { adapterId }), "application/json; charset=utf-8");
                }

                return Response(400, "{\"error\":\"Unbekannte Aktion\"}", "application/json; charset=utf-8");
            }
        }

        private static string[] SetAutoStart(IAdapterContext context, AdapterRegistration adapter, bool enabled)
        {
            var primary = adapter.Id.EndsWith(".0", StringComparison.Ordinal);
            var current = ReadEnabledAdapters(context)
                ?? context.AdapterRegistry.GetAll().Where(a => a.Type == AdapterType.Custom).Select(a => a.Id).ToArray();
            var updated = current
                .Where(entry => !string.Equals(entry, adapter.Id, StringComparison.OrdinalIgnoreCase) &&
                                !(primary && string.Equals(entry, adapter.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (enabled)
            {
                updated.Add(adapter.Id);
            }

            var array = updated.ToArray();
            context.Config.Set("adapter.enabledAdapters", array);
            ioBroker_NewGen.Startup.ConfigFileWriter.Update(
                Path.Combine(AppContext.BaseDirectory, "config.toml"),
                new Dictionary<string, object?> { ["adapter.enabledAdapters"] = array });
            return array;
        }

        private sealed class AutostartEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public AutostartEndpoint(IAdapterContext context) : base("/api/adapters/autostart")
            {
                _context = context;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                string adapterId;
                bool enabled;
                try
                {
                    using var document = JsonDocument.Parse(request.Body is { Length: > 0 } ? request.Body : Array.Empty<byte>());
                    adapterId = document.RootElement.GetProperty("adapterId").GetString() ?? string.Empty;
                    enabled = document.RootElement.GetProperty("enabled").GetBoolean();
                }
                catch (Exception)
                {
                    return Task.FromResult(Response(400, "{\"error\":\"Erwartet {adapterId, enabled}\"}", "application/json; charset=utf-8"));
                }

                var adapter = _context.AdapterRegistry.Get(adapterId);
                if (adapter is null)
                {
                    return Task.FromResult(Response(404, "{\"error\":\"Adapter nicht gefunden\"}", "application/json; charset=utf-8"));
                }

                if (adapter.Type != AdapterType.Custom ||
                    string.Equals(adapter.Name, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(Response(400, "{\"error\":\"Autostart ist für diesen Adapter nicht änderbar\"}", "application/json; charset=utf-8"));
                }

                SetAutoStart(_context, adapter, enabled);
                return Task.FromResult(Response(200, JsonSerializer.Serialize(new { adapterId = adapter.Id, autoStart = enabled }), "application/json; charset=utf-8"));
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

                var enabledAdapters = ReadEnabledAdapters(_context);
                var adapters = _context.AdapterRegistry.GetAll()
                    .OrderBy(adapter => adapter.Id, StringComparer.Ordinal)
                    .Select(adapter => new
                    {
                        adapter.Name,
                        adapter.Id,
                        Type = adapter.Type.ToString(),
                        Status = adapter.Status.ToString(),
                        AutoStart = adapter.Type == AdapterType.IoBroker
                            ? _context.Legacy?.IsInstance(adapter.Id) == true
                            : IsAutoStart(enabledAdapters, adapter),
                        LegacyInstance = _context.Legacy?.IsInstance(adapter.Id) == true,
                        HasConfig = _context.AdapterRegistry.Configs.HasConfig(adapter.Name),
                        AutoStartEditable = adapter.Type == AdapterType.Custom &&
                            !string.Equals(adapter.Name, "admin", StringComparison.OrdinalIgnoreCase),
                        adapter.Metadata,
                        adapter.TcpEndpoint,
                        adapter.RegisteredAt,
                        adapter.LastSeenAt,
                        Metrics = new
                        {
                            adapter.StartedAt,
                            adapter.StoppedAt,
                            adapter.LastStatusChangeAt,
                            UptimeSeconds = adapter.Status == AdapterStatus.Connected && adapter.StartedAt is { } startedAt
                                ? (long)(DateTime.UtcNow - startedAt).TotalSeconds
                                : 0L,
                            adapter.StartCount,
                            adapter.RestartCount,
                            adapter.FaultCount,
                            adapter.LastError,
                            adapter.LastErrorAt
                        }
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
                foreach (var (key, fallback) in SystemSettings.Defaults)
                {
                    values[key] = ReadConfig(key, fallback);
                }

                values["system.setupDone"] = ReadConfig("system.setupDone", false);

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
                    var toPersist = new Dictionary<string, object?>();
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!values.ContainsKey(property.Name) ||
                            string.Equals(property.Name, "admin.adminToken", StringComparison.OrdinalIgnoreCase))
                        {
                            return Response(400, JsonSerializer.Serialize(new { error = $"Konfigurationsschlüssel nicht editierbar: {property.Name}" }), "application/json; charset=utf-8");
                        }

                        var converted = SystemSettings.IsSystemKey(property.Name)
                            ? SystemSettings.Validate(property.Name, property.Value)
                            : ToConfigValue(property.Value);
                        _context.Config.Set(property.Name, converted);
                        toPersist[property.Name] = converted;
                        changed.Add(property.Name);
                    }

                    var configPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
                    ioBroker_NewGen.Startup.ConfigFileWriter.Update(configPath, toPersist);
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

        private static class SystemSettings
        {
            private static readonly string[] TemperatureUnits = { "°C", "°F" };
            private static readonly string[] DateFormats = { "DD.MM.YYYY", "DD/MM/YYYY", "MM/DD/YYYY", "YYYY-MM-DD" };
            private static readonly string[] Weekdays = { "monday", "sunday", "saturday" };

            public static readonly IReadOnlyDictionary<string, object> Defaults = new Dictionary<string, object>
            {
                ["system.temperatureUnit"] = "°C",
                ["system.currency"] = "€",
                ["system.dateFormat"] = "DD.MM.YYYY",
                ["system.isFloatComma"] = true,
                ["system.firstDayOfWeek"] = "monday",
                ["system.country"] = string.Empty,
                ["system.city"] = string.Empty,
                ["system.address"] = string.Empty,
                ["system.latitude"] = string.Empty,
                ["system.longitude"] = string.Empty
            };

            public static bool IsSystemKey(string key)
                => key == "system.setupDone" || Defaults.ContainsKey(key);

            public static object Validate(string key, JsonElement value)
            {
                switch (key)
                {
                    case "system.setupDone":
                    case "system.isFloatComma":
                        return value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => throw new InvalidDataException($"{key} muss ein boolescher Wert sein.")
                        };
                    case "system.temperatureUnit":
                        return OneOf(key, value, TemperatureUnits);
                    case "system.dateFormat":
                        return OneOf(key, value, DateFormats);
                    case "system.firstDayOfWeek":
                        return OneOf(key, value, Weekdays);
                    case "system.latitude":
                        return Coordinate(key, value, 90);
                    case "system.longitude":
                        return Coordinate(key, value, 180);
                    default:
                    {
                        var text = (value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                            ?? throw new InvalidDataException($"{key} muss ein Text sein.");
                        text = text.Trim();
                        if (text.Length > 200 || text.Any(char.IsControl))
                        {
                            throw new InvalidDataException($"{key} ist ungültig.");
                        }

                        return text;
                    }
                }
            }

            private static string OneOf(string key, JsonElement value, string[] allowed)
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                return text is not null && allowed.Contains(text)
                    ? text
                    : throw new InvalidDataException($"{key} hat einen ungültigen Wert.");
            }

            private static string Coordinate(string key, JsonElement value, double limit)
            {
                var text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString()?.Trim().Replace(',', '.') ?? string.Empty,
                    JsonValueKind.Number => value.GetRawText(),
                    _ => throw new InvalidDataException($"{key} muss eine Zahl sein.")
                };
                if (text.Length == 0)
                {
                    return string.Empty;
                }

                if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
                    || double.IsNaN(number) || Math.Abs(number) > limit)
                {
                    throw new InvalidDataException($"{key} liegt außerhalb des gültigen Bereichs (±{limit}).");
                }

                return number.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private sealed class AdapterConfigEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public AdapterConfigEndpoint(IAdapterContext context) : base("/api/adapters/config")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                JsonObject? body;
                try
                {
                    body = request.Body is { Length: > 0 } ? JsonNode.Parse(request.Body) as JsonObject : null;
                }
                catch (JsonException)
                {
                    body = null;
                }

                var adapterId = body?["adapterId"]?.GetValue<string>();
                var action = body?["action"]?.GetValue<string>() ?? "get";
                if (string.IsNullOrWhiteSpace(adapterId))
                {
                    return Error(400, "adapterId fehlt");
                }

                var registration = _context.AdapterRegistry.Get(adapterId);
                var configs = _context.AdapterRegistry.Configs;
                if (registration is null || !configs.HasConfig(registration.Name))
                {
                    return Error(404, "Adapter hat keine Einstellungen");
                }

                try
                {
                    var schema = configs.GetSchema(registration.Name)!;
                    var running = registration.Status == AdapterStatus.Connected;
                    var managed = registration.Type == AdapterType.Custom || _context.Legacy?.IsInstance(registration.Id) == true;
                    if (action == "save")
                    {
                        if (body!["values"] is not JsonObject values)
                        {
                            return Error(400, "values fehlt");
                        }

                        configs.Save(registration.Id, registration.Name, values);
                        var restarted = false;
                        var allowRestart = body["restart"] is not JsonValue flag || !flag.TryGetValue<bool>(out var wanted) || wanted;
                        if (managed && running && allowRestart)
                        {
                            restarted = await _context.RestartAdapterAsync(registration.Id);
                        }
                        else if (!managed)
                        {
                            configs.EnsureFile(registration.Id, registration.Name);
                        }

                        return Ok(new JsonObject
                        {
                            ["saved"] = true,
                            ["restarted"] = restarted,
                            ["restartRequired"] = !managed && running
                        });
                    }

                    var current = configs.GetValues(registration.Id, registration.Name);
                    var oauth = new JsonArray();
                    foreach (var provider in schema.OAuthProviders)
                    {
                        oauth.Add(new JsonObject
                        {
                            ["id"] = provider.Id,
                            ["label"] = provider.Label,
                            ["redirectUri"] = provider.RedirectUri,
                            ["connected"] = !string.IsNullOrEmpty(current[provider.TokenKey]?.ToString())
                        });
                    }

                    return Ok(new JsonObject
                    {
                        ["adapterId"] = registration.Id,
                        ["name"] = registration.Name,
                        ["type"] = registration.Type.ToString(),
                        ["autoRestart"] = managed,
                        ["running"] = running,
                        ["oauth"] = oauth,
                        ["jsonConfig"] = schema.JsonConfig is null ? null : JsonNode.Parse(schema.JsonConfig),
                        ["values"] = configs.Mask(registration.Name, current),
                        ["secrets"] = new JsonArray(configs.SecretKeys(registration.Name).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray())
                    });
                }
                catch (AdapterConfigException ex)
                {
                    return Error(400, ex.Message);
                }
            }

            private static EndpointResponse Ok(JsonObject payload)
                => Response(200, payload.ToJsonString(), "application/json; charset=utf-8");

            private static EndpointResponse Error(int status, string message)
                => Response(status, new JsonObject { ["error"] = message }.ToJsonString(), "application/json; charset=utf-8");
        }

        private sealed class AdapterOAuthEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public AdapterOAuthEndpoint(IAdapterContext context) : base("/api/adapters/oauth")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                JsonObject? body;
                try
                {
                    body = request.Body is { Length: > 0 } ? JsonNode.Parse(request.Body) as JsonObject : null;
                }
                catch (JsonException)
                {
                    body = null;
                }

                var action = body?["action"]?.ToString();
                var oauth = _context.AdapterRegistry.OAuth;
                try
                {
                    switch (action)
                    {
                        case "start":
                        {
                            var registration = _context.AdapterRegistry.Get(body?["adapterId"]?.ToString() ?? string.Empty);
                            if (registration is null)
                            {
                                return Error(404, "Adapter nicht gefunden");
                            }

                            var session = oauth.Start(registration.Id, registration.Name, body?["provider"]?.ToString() ?? string.Empty);
                            return Ok(Describe(session));
                        }
                        case "status":
                        {
                            var session = oauth.Get(body?["sessionId"]?.ToString() ?? string.Empty);
                            if (session is null)
                            {
                                return Error(404, "Sitzung nicht gefunden");
                            }

                            var restarted = false;
                            if (session.State == "success" && !session.Handled)
                            {
                                session.Handled = true;
                                var registration = _context.AdapterRegistry.Get(session.InstanceId);
                                if (registration is { Type: AdapterType.Custom, Status: AdapterStatus.Connected })
                                {
                                    restarted = await _context.RestartAdapterAsync(registration.Id);
                                }
                            }

                            var result = Describe(session);
                            result["restarted"] = restarted;
                            return Ok(result);
                        }
                        case "cancel":
                            return Ok(new JsonObject { ["cancelled"] = oauth.Cancel(body?["sessionId"]?.ToString() ?? string.Empty) });
                        default:
                            return Error(400, "Unbekannte Aktion");
                    }
                }
                catch (AdapterConfigException ex)
                {
                    return Error(400, ex.Message);
                }
            }

            private static JsonObject Describe(OAuthSession session) => new()
            {
                ["sessionId"] = session.Id,
                ["state"] = session.State,
                ["message"] = session.Message,
                ["browserLaunched"] = session.BrowserLaunched,
                ["authorizeUrl"] = session.State == "pending" ? session.AuthorizeUrl : null
            };

            private static EndpointResponse Ok(JsonObject payload)
                => Response(200, payload.ToJsonString(), "application/json; charset=utf-8");

            private static EndpointResponse Error(int status, string message)
                => Response(status, new JsonObject { ["error"] = message }.ToJsonString(), "application/json; charset=utf-8");
        }

        private sealed class AdapterCommandEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;
            private readonly string _action;

            public AdapterCommandEndpoint(string route, IAdapterContext context, string action) : base(route)
            {
                _context = context;
                _action = action;
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

                var success = _action switch
                {
                    "start" => await _context.StartAdapterAsync(adapterId),
                    "restart" => await _context.RestartAdapterAsync(adapterId),
                    _ => await _context.StopAdapterAsync(adapterId)
                };
                return Response(success ? 200 : 404, JsonSerializer.Serialize(new { adapterId, success }), "application/json; charset=utf-8");
            }
        }

        private sealed class LegacyAdapterEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;

            public LegacyAdapterEndpoint(IAdapterContext context) : base("/api/adapters/legacy")
            {
                _context = context;
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Unauthorized();
                }

                var legacy = _context.Legacy;
                if (legacy is null)
                {
                    return Response(503, "{\"error\":\"Legacy-Adapter-Runtime ist nicht initialisiert\"}", "application/json; charset=utf-8");
                }

                var body = request.Body ?? Array.Empty<byte>();
                if (body.Length == 0)
                {
                    return Response(200, JsonSerializer.Serialize(legacy.GetInstalled()), "application/json; charset=utf-8");
                }

                // ZIP-Upload (Magic "PK") oder JSON-Aktion.
                if (body.Length > 4 && body[0] == (byte)'P' && body[1] == (byte)'K')
                {
                    var upload = body;
                    var task = AdminTasks.Start("Legacy-Adapter (ZIP) installieren", async t =>
                    {
                        t.Log($"ZIP empfangen ({upload.Length / 1024} KB).");
                        using var stream = new MemoryStream(upload, writable: false);
                        var info = await legacy.InstallAsync(stream, default, t.Log);
                        return $"Adapter installiert: {info.Name} {info.Version}" + (info.Warnings?.Count > 0 ? " (Warnungen: " + string.Join(" ", info.Warnings) + ")" : string.Empty);
                    });
                    return Response(202, JsonSerializer.Serialize(new { taskId = task.Id }), "application/json; charset=utf-8");
                }

                try
                {
                    using var document = JsonDocument.Parse(body);
                    var action = document.RootElement.GetProperty("action").GetString();
                    var name = document.RootElement.GetProperty("name").GetString() ?? string.Empty;
                    if (action == "uninstall")
                    {
                        return await legacy.UninstallAsync(name)
                            ? Response(200, JsonSerializer.Serialize(new { name }), "application/json; charset=utf-8")
                            : Response(404, "{\"error\":\"Adapter nicht installiert\"}", "application/json; charset=utf-8");
                    }
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    return Response(400, "{\"error\":\"Erwartet ZIP-Datei oder {action, name}\"}", "application/json; charset=utf-8");
                }
                catch (IOException ex)
                {
                    return Response(400, JsonSerializer.Serialize(new { error = ex.Message }), "application/json; charset=utf-8");
                }

                return Response(400, "{\"error\":\"Unbekannte Aktion\"}", "application/json; charset=utf-8");
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

        private sealed class OverviewEndpoint : BaseRuntimeEndpoint
        {
            private readonly IAdapterContext _context;
            private readonly object _cpuLock = new();
            private TimeSpan _lastCpu;
            private DateTime _lastCpuAt = DateTime.UtcNow;
            private double _cpuPercent;

            public OverviewEndpoint(IAdapterContext context) : base("/api/overview")
            {
                _context = context;
                _lastCpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(_context, request))
                {
                    return Task.FromResult(Unauthorized());
                }

                var adapters = _context.AdapterRegistry.GetAll();
                var active = adapters.Count(a => a.Status == AdapterStatus.Connected);
                var faulted = adapters.Count(a => a.Status == AdapterStatus.Faulted);
                var enabledList = ReadEnabledAdapters(_context);
                var instances = adapters.Count(a => a.Status == AdapterStatus.Connected || IsAutoStart(enabledList, a));

                int objects = 0, states = 0;
                try
                {
                    using var document = JsonDocument.Parse(_context.Matrix.DisplayAsJson());
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        objects++;
                        if (item.TryGetProperty("State", out var state) && state.ValueKind == JsonValueKind.Object)
                        {
                            states++;
                        }
                    }
                }
                catch
                {
                    // Zähler bleiben 0, wenn der Objektbaum nicht lesbar ist.
                }

                var process = System.Diagnostics.Process.GetCurrentProcess();
                double cpu;
                lock (_cpuLock)
                {
                    var now = DateTime.UtcNow;
                    var elapsed = (now - _lastCpuAt).TotalMilliseconds;
                    if (elapsed >= 1000)
                    {
                        var used = process.TotalProcessorTime;
                        _cpuPercent = Math.Clamp((used - _lastCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
                        _lastCpu = used;
                        _lastCpuAt = now;
                    }

                    cpu = _cpuPercent;
                }

                var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                var payload = new
                {
                    Status = faulted == 0 ? "OK" : "WARN",
                    Adapters = new { Total = adapters.Count, Active = active, Inactive = adapters.Count - active, Faulted = faulted, Instances = instances },
                    Objects = objects,
                    States = states,
                    System = new
                    {
                        Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                        Host = Environment.MachineName,
                        Cores = Environment.ProcessorCount,
                        SystemUptimeSeconds = Environment.TickCount64 / 1000,
                        ProcessUptimeSeconds = (long)(DateTime.Now - process.StartTime).TotalSeconds,
                        MemoryUsedBytes = process.WorkingSet64,
                        MemoryTotalBytes = totalMemory,
                        CpuPercent = Math.Round(cpu, 1)
                    }
                };
                return Task.FromResult(Response(200, JsonSerializer.Serialize(payload), "application/json; charset=utf-8"));
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

                var latestLog = Path.Combine(AppContext.BaseDirectory, "log", "latest.log");
                var logs = File.Exists(latestLog)
                    ? new[]
                    {
                        new
                        {
                            file = Path.GetFileName(latestLog),
                            lines = ReadTail(latestLog, 100)
                        }
                    }
                    : Array.Empty<object>();
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

                if (request.Body is not { Length: > 0 } && SecurityOf(_context) is { } securityService)
                {
                    return SecurityListings.RolesAsync(securityService);
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

                if (request.Body is not { Length: > 0 } && SecurityOf(_context) is { } securityService)
                {
                    return SecurityListings.UsersAsync(securityService);
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

        private sealed class SecurityUsersEndpoint : AuthEndpointBase
        {
            public SecurityUsersEndpoint(IAdapterContext context) : base("/api/security/users", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(Context, request))
                {
                    return Unauthorized();
                }

                var current = await ResolveSessionAsync(Context, request);
                var security = SecurityOf(Context);
                using var body = ParseBody(request);
                if (security is null || body is null)
                {
                    return Json(400, new { error = "Ungültige Anfrage" });
                }

                var root = body.RootElement;
                var action = Str(root, "action");
                var username = Str(root, "username")?.Trim() ?? string.Empty;
                var self = current is { } c && string.Equals(c.User.Username, username, StringComparison.OrdinalIgnoreCase);

                try
                {
                    switch (action)
                    {
                        case "create":
                        {
                            var roles = StringList(root, "roles");
                            await ValidateRolesAsync(security, roles);
                            await security.Users.CreateAsync(username, Str(root, "password") ?? string.Empty, Str(root, "displayName"), roles);
                            if (root.TryGetProperty("mustChangePassword", out var must) && must.ValueKind == JsonValueKind.True)
                            {
                                await security.Users.SetPasswordAsync(username, Str(root, "password") ?? string.Empty, true);
                            }

                            return Json(200, new { username, created = true });
                        }
                        case "update":
                        {
                            if (await security.Users.FindAsync(username) is null)
                            {
                                return Json(404, new { error = $"Benutzer '{username}' existiert nicht." });
                            }

                            var enabled = !root.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.False;
                            if (self && !enabled)
                            {
                                return Json(400, new { error = "Das eigene Konto kann nicht deaktiviert werden." });
                            }

                            var wanted = StringList(root, "roles");
                            await ValidateRolesAsync(security, wanted);
                            if (self && !wanted.Contains(SecurityRoles.Administrator, StringComparer.OrdinalIgnoreCase))
                            {
                                var currentRoles = await security.Users.GetRolesAsync(username);
                                if (currentRoles.Contains(SecurityRoles.Administrator, StringComparer.OrdinalIgnoreCase))
                                {
                                    return Json(400, new { error = "Dem eigenen Konto kann die Rolle Administrator nicht entzogen werden." });
                                }
                            }

                            await security.Users.SetDisplayNameAsync(username, Str(root, "displayName"));
                            await security.Users.SetEnabledAsync(username, enabled);
                            var existing = await security.Users.GetRolesAsync(username);
                            foreach (var role in existing.Where(r => !wanted.Contains(r, StringComparer.OrdinalIgnoreCase)))
                            {
                                await security.Roles.RemoveAsync(username, role);
                            }

                            foreach (var role in wanted.Where(r => !existing.Contains(r, StringComparer.OrdinalIgnoreCase)))
                            {
                                await security.Roles.AssignAsync(username, role);
                            }

                            var password = Str(root, "password");
                            if (!string.IsNullOrEmpty(password))
                            {
                                var must = root.TryGetProperty("mustChangePassword", out var m) && m.ValueKind == JsonValueKind.True;
                                await security.Users.SetPasswordAsync(username, password, must);
                            }

                            return Json(200, new { username, updated = true });
                        }
                        case "delete":
                            if (self)
                            {
                                return Json(400, new { error = "Das eigene Konto kann nicht gelöscht werden." });
                            }

                            return await security.Users.DeleteAsync(username)
                                ? Json(200, new { username, deleted = true })
                                : Json(404, new { error = $"Benutzer '{username}' existiert nicht." });
                        default:
                            return Json(400, new { error = "Unbekannte Aktion" });
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return Json(400, new { error = ex.Message });
                }
            }

            private static async Task ValidateRolesAsync(SecurityService security, IReadOnlyList<string> roles)
            {
                foreach (var role in roles)
                {
                    if (!await security.Roles.ExistsAsync(role))
                    {
                        throw new InvalidOperationException($"Rolle '{role}' existiert nicht.");
                    }
                }
            }
        }

        private sealed class SecurityRolesEndpoint : AuthEndpointBase
        {
            public SecurityRolesEndpoint(IAdapterContext context) : base("/api/security/roles", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!IsAuthorized(Context, request))
                {
                    return Unauthorized();
                }

                var security = SecurityOf(Context);
                using var body = ParseBody(request);
                if (security is null || body is null)
                {
                    return Json(400, new { error = "Ungültige Anfrage" });
                }

                var root = body.RootElement;
                var name = Str(root, "name")?.Trim() ?? string.Empty;
                var builtin = SecurityRoles.Defaults.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

                try
                {
                    switch (Str(root, "action"))
                    {
                        case "save":
                            if (builtin)
                            {
                                return Json(400, new { error = "Vordefinierte Rollen können nicht geändert werden." });
                            }

                            await security.Roles.SaveAsync(name, Str(root, "description"), StringList(root, "permissions"));
                            return Json(200, new { name, saved = true });
                        case "delete":
                            if (builtin)
                            {
                                return Json(400, new { error = "Vordefinierte Rollen können nicht gelöscht werden." });
                            }

                            return await security.Roles.DeleteAsync(name)
                                ? Json(200, new { name, deleted = true })
                                : Json(404, new { error = $"Rolle '{name}' existiert nicht." });
                        case "permissions":
                            return Json(200, SecurityPermissions.All);
                        default:
                            return Json(400, new { error = "Unbekannte Aktion" });
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return Json(400, new { error = ex.Message });
                }
            }
        }

        private static IReadOnlyList<string> StringList(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray()
                : Array.Empty<string>();
        }

        private static class SecurityListings
        {
            public static async Task<EndpointResponse> RolesAsync(SecurityService security)
            {
                var roles = await security.Roles.ListAsync();
                var payload = new List<object>();
                foreach (var role in roles)
                {
                    payload.Add(new
                    {
                        name = role.Name,
                        description = role.Description,
                        builtin = SecurityRoles.Defaults.Any(d => string.Equals(d.Name, role.Name, StringComparison.OrdinalIgnoreCase)),
                        permissions = await security.Roles.GetPermissionsAsync(role.Name)
                    });
                }

                return Response(200, JsonSerializer.Serialize(payload), "application/json; charset=utf-8");
            }

            public static async Task<EndpointResponse> UsersAsync(SecurityService security)
            {
                var users = await security.Users.ListAsync();
                var payload = new List<object>();
                foreach (var user in users)
                {
                    payload.Add(new
                    {
                        username = user.Username,
                        displayName = user.DisplayName,
                        isEnabled = user.IsEnabled,
                        mustChangePassword = user.MustChangePassword,
                        roles = await security.Users.GetRolesAsync(user.Username),
                        createdUtc = user.CreatedUtc,
                        lastLoginUtc = user.LastLoginUtc
                    });
                }

                return Response(200, JsonSerializer.Serialize(payload), "application/json; charset=utf-8");
            }
        }

        private const string SessionCookie = "ngsession";
        private static readonly AsyncLocal<bool> Forbidden = new();

        private static SecurityService? SecurityOf(IAdapterContext context)
        {
            return (context.RuntimeContext as MainRuntimeContext)?.Security;
        }

        private static string? GetHeader(EndpointRequest request, string name)
        {
            return request.Headers
                .FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                .Value;
        }

        private static string? ReadSessionToken(EndpointRequest request)
        {
            var cookies = GetHeader(request, "Cookie");
            if (string.IsNullOrWhiteSpace(cookies))
            {
                return null;
            }

            foreach (var part in cookies.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = part.IndexOf('=');
                if (separator > 0 && string.Equals(part[..separator], SessionCookie, StringComparison.Ordinal))
                {
                    var value = part[(separator + 1)..];
                    return value.Length > 0 ? value : null;
                }
            }

            return null;
        }

        private static string BuildSessionCookie(string value, int maxAgeSeconds)
        {
            return $"{SessionCookie}={value}; Path=/; Max-Age={maxAgeSeconds}; HttpOnly; SameSite=Strict";
        }

        private static bool HasCsrfHeader(EndpointRequest request)
        {
            return !string.IsNullOrWhiteSpace(GetHeader(request, "X-Requested-With"));
        }

        private static string? RequiredPermission(string route, bool write)
        {
            return route switch
            {
                "/api/states" => SecurityPermissions.StateRead,
                "/api/adapters" => SecurityPermissions.AdapterRead,
                "/api/adapters/start" => SecurityPermissions.AdapterStart,
                "/api/adapters/stop" => SecurityPermissions.AdapterStop,
                "/api/adapters/restart" => SecurityPermissions.AdapterRestart,
                "/api/adapters/autostart" or "/api/adapters/instances" or "/api/adapters/config" or "/api/adapters/oauth" => SecurityPermissions.SystemConfigure,
                "/api/adapters/install" or "/api/adapters/install-remote" => SecurityPermissions.AdapterInstall,
                "/api/adapters/legacy" => write ? SecurityPermissions.AdapterInstall : SecurityPermissions.AdapterRead,
                "/api/config" => write ? SecurityPermissions.SystemConfigure : SecurityPermissions.SystemRead,
                "/api/logs" or "/api/overview" => SecurityPermissions.SystemRead,
                "/api/i18n/extend" => SecurityPermissions.SystemConfigure,
                "/api/users" => write ? SecurityPermissions.UserManage : SecurityPermissions.UserRead,
                "/api/security/users" => SecurityPermissions.UserManage,
                "/api/security/roles" => SecurityPermissions.RoleManage,
                "/api/roles" or "/api/permissions" => write ? SecurityPermissions.RoleManage : SecurityPermissions.RoleRead,
                _ => SecurityPermissions.SystemAdministration
            };
        }

        private static bool HasValidLegacyToken(IAdapterContext context, EndpointRequest request)
        {
            var expectedToken = ReadConfig(context, "admin.adminToken", string.Empty);
            if (string.IsNullOrWhiteSpace(expectedToken))
            {
                return false;
            }

            var suppliedToken = GetHeader(request, "X-Admin-Token");
            if (string.IsNullOrWhiteSpace(suppliedToken))
            {
                suppliedToken = GetHeader(request, "Authorization");
                if (suppliedToken?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                {
                    suppliedToken = suppliedToken[7..].Trim();
                }
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedToken),
                Encoding.UTF8.GetBytes(suppliedToken ?? string.Empty));
        }

        private static bool IsAuthorized(IAdapterContext context, EndpointRequest request)
        {
            Forbidden.Value = false;
            if (!ReadConfig(context, "admin.adminRequireAuthentication", false))
            {
                return true;
            }

            // Optionaler Automations-Token (Header), nur aktiv wenn admin.adminToken gesetzt ist.
            if (HasValidLegacyToken(context, request))
            {
                return true;
            }

            var security = SecurityOf(context);
            var token = ReadSessionToken(request);
            if (security is null || token is null)
            {
                return false;
            }

            var write = request.Body is { Length: > 0 };
            if (write && !HasCsrfHeader(request))
            {
                Forbidden.Value = true;
                return false;
            }

            var session = security.Authentication.ValidateSessionAsync(token).GetAwaiter().GetResult();
            if (session is null)
            {
                return false;
            }

            var user = security.Users.FindAsync(session.Username).GetAwaiter().GetResult();
            if (user is null || !user.IsEnabled || user.MustChangePassword)
            {
                Forbidden.Value = true;
                return false;
            }

            var permission = RequiredPermission(request.Route, write);
            var allowed = permission is not null &&
                security.Authorization.HasPermissionAsync(user.Id, permission).GetAwaiter().GetResult();
            Forbidden.Value = !allowed;
            return allowed;
        }

        private static async Task<(SecurityService Security, SecuritySession Session, SecurityUser User)?> ResolveSessionAsync(
            IAdapterContext context,
            EndpointRequest request)
        {
            var security = SecurityOf(context);
            var token = ReadSessionToken(request);
            if (security is null || token is null)
            {
                return null;
            }

            var session = await security.Authentication.ValidateSessionAsync(token);
            if (session is null)
            {
                return null;
            }

            var user = await security.Users.FindAsync(session.Username);
            return user is { IsEnabled: true } ? (security, session, user) : null;
        }

        private abstract class AuthEndpointBase : BaseRuntimeEndpoint
        {
            protected readonly IAdapterContext Context;

            protected AuthEndpointBase(string route, IAdapterContext context) : base(route)
            {
                Context = context;
            }

            protected static EndpointResponse Json(int status, object payload, string? cookie = null)
            {
                var response = Response(status, JsonSerializer.Serialize(payload), "application/json; charset=utf-8");
                response.Headers["Cache-Control"] = "no-store";
                if (cookie is not null)
                {
                    response.Headers["Set-Cookie"] = cookie;
                }

                return response;
            }

            protected static string? Str(JsonElement root, string name)
            {
                return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : null;
            }

            protected static JsonDocument? ParseBody(EndpointRequest request)
            {
                try
                {
                    return request.Body is { Length: > 0 } ? JsonDocument.Parse(request.Body) : null;
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        private sealed class I18nEndpoint : AuthEndpointBase
        {
            private readonly string _mode;

            public I18nEndpoint(string mode, IAdapterContext context) : base("/api/i18n/" + mode, context)
            {
                _mode = mode;
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                var i18n = Context.Localization;
                if (i18n is null)
                {
                    return Task.FromResult(Json(503, new { error = "Lokalisierung nicht verfügbar" }));
                }

                using var document = ParseBody(request);
                var root = document?.RootElement ?? default;
                var hasBody = document is not null && root.ValueKind == JsonValueKind.Object;
                var language = hasBody ? Str(root, "language") : null;

                switch (_mode)
                {
                    case "languages":
                        return Task.FromResult(Json(200, new { language = i18n.Language, fallback = i18n.FallbackLanguage, languages = i18n.AvailableLanguages.Select(l => new { code = l.Code, name = l.Name }).ToArray() }));
                    case "translations":
                        var resolved = i18n.ResolveLanguage(language) ?? i18n.Language;
                        return Task.FromResult(Json(200, new { language = resolved, fallback = i18n.FallbackLanguage, translations = i18n.GetAll(resolved) }));
                    case "translate":
                        var key = hasBody ? Str(root, "key") : null;
                        if (string.IsNullOrEmpty(key))
                        {
                            return Task.FromResult(Json(400, new { error = "key erforderlich" }));
                        }

                        var text = i18n.Translate(key, language);
                        if (hasBody && root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                text = string.Format(i18n.Culture, text, args.EnumerateArray().Select(a => (object?)a.ToString()).ToArray());
                            }
                            catch (FormatException)
                            {
                            }
                        }

                        return Task.FromResult(Json(200, new { key, text }));
                    case "extend":
                        if (!IsAuthorized(Context, request))
                        {
                            return Task.FromResult(Unauthorized());
                        }

                        if (!hasBody || language is null || !root.TryGetProperty("translations", out var texts) || texts.ValueKind != JsonValueKind.Object)
                        {
                            return Task.FromResult(Json(400, new { error = "language und translations erforderlich" }));
                        }

                        return Task.FromResult(Json(200, new { added = i18n.ExtendFromJson(language, texts.GetRawText()) }));
                    default:
                        return Task.FromResult(Json(404, new { error = "Unbekannt" }));
                }
            }
        }

        private sealed class AuthStatusEndpoint : AuthEndpointBase
        {
            public AuthStatusEndpoint(IAdapterContext context) : base("/api/auth/status", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                var security = SecurityOf(Context);
                var firstRun = security is not null && await IsFirstRunAsync(security);
                return Json(200, new { firstRun, username = firstRun ? security!.Options.DefaultUsername : null });
            }
        }

        private sealed class FirstRunSetupEndpoint : AuthEndpointBase
        {
            private static readonly SemaphoreSlim Gate = new(1, 1);

            public FirstRunSetupEndpoint(IAdapterContext context) : base("/api/auth/setup", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!HasCsrfHeader(request))
                {
                    return Json(403, new { error = "Ungültige Anfrage" });
                }

                var security = SecurityOf(Context);
                if (security is null)
                {
                    return Json(503, new { error = "Anmeldung ist nicht verfügbar (Core.Security nicht initialisiert)" });
                }

                using var document = ParseBody(request);
                var password = document is null ? null : Str(document.RootElement, "newPassword");
                if (string.IsNullOrEmpty(password))
                {
                    return Json(400, new { error = "Passwort erforderlich" });
                }

                await Gate.WaitAsync();
                try
                {
                    if (!await IsFirstRunAsync(security))
                    {
                        return Json(409, new { error = "Das Administrator-Passwort wurde bereits gesetzt" });
                    }

                    var username = security.Options.DefaultUsername;
                    try
                    {
                        await security.Users.SetPasswordAsync(username, password, mustChangePassword: false);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        return Json(400, new { error = ex.Message });
                    }

                    var login = await security.Authentication.LoginAsync(username, password);
                    if (!login.Success || login.Token is null)
                    {
                        return Json(200, new { success = true, login = false });
                    }

                    var maxAge = Math.Max(60, security.Options.SessionTimeoutMinutes * 60);
                    return Json(200, new { success = true }, BuildSessionCookie(login.Token, maxAge));
                }
                finally
                {
                    Gate.Release();
                }
            }
        }

        // Erststart: Standardadministrator wurde angelegt, hat sich aber noch nie angemeldet.
        private static async Task<bool> IsFirstRunAsync(SecurityService security)
        {
            if (!security.Options.CreateDefaultAdmin || !security.Options.ForcePasswordChange)
            {
                return false;
            }

            var user = await security.Users.FindAsync(security.Options.DefaultUsername);
            return user is { IsEnabled: true, MustChangePassword: true, LastLoginUtc: null };
        }

        private sealed class LoginEndpoint : AuthEndpointBase
        {
            public LoginEndpoint(IAdapterContext context) : base("/api/auth/login", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!HasCsrfHeader(request))
                {
                    return Json(403, new { error = "Ungültige Anfrage" });
                }

                var security = SecurityOf(Context);
                if (security is null)
                {
                    return Json(503, new { error = "Anmeldung ist nicht verfügbar (Core.Security nicht initialisiert)" });
                }

                using var document = ParseBody(request);
                var username = document is null ? null : Str(document.RootElement, "username");
                var password = document is null ? null : Str(document.RootElement, "password");
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                {
                    return Json(400, new { error = "Benutzername und Passwort erforderlich" });
                }

                var result = await security.Authentication.LoginAsync(username, password);
                if (!result.Success || result.Token is null)
                {
                    await Task.Delay(500);
                    return Json(401, new { error = result.Error ?? "Anmeldung fehlgeschlagen" });
                }

                var maxAge = Math.Max(60, security.Options.SessionTimeoutMinutes * 60);
                return Json(
                    200,
                    new { username = result.User?.Username, mustChangePassword = result.MustChangePassword },
                    BuildSessionCookie(result.Token, maxAge));
            }
        }

        private sealed class LogoutEndpoint : AuthEndpointBase
        {
            public LogoutEndpoint(IAdapterContext context) : base("/api/auth/logout", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!HasCsrfHeader(request))
                {
                    return Json(403, new { error = "Ungültige Anfrage" });
                }

                var token = ReadSessionToken(request);
                var security = SecurityOf(Context);
                if (security is not null && token is not null)
                {
                    await security.Authentication.LogoutAsync(token);
                }

                return Json(200, new { success = true }, BuildSessionCookie(string.Empty, 0));
            }
        }

        private sealed class MeEndpoint : AuthEndpointBase
        {
            public MeEndpoint(IAdapterContext context) : base("/api/auth/me", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                var resolved = await ResolveSessionAsync(Context, request);
                if (resolved is null)
                {
                    return Json(401, new { error = "Authentifizierung erforderlich" });
                }

                var (security, _, user) = resolved.Value;
                var roles = await security.Users.GetRolesAsync(user.Username);
                var permissions = new List<string>();
                foreach (var permission in SecurityPermissions.All)
                {
                    if (await security.Authorization.HasPermissionAsync(user.Id, permission))
                    {
                        permissions.Add(permission);
                    }
                }

                return Json(200, new { username = user.Username, displayName = user.DisplayName, mustChangePassword = user.MustChangePassword, roles, permissions });
            }
        }

        private sealed class ChangePasswordEndpoint : AuthEndpointBase
        {
            public ChangePasswordEndpoint(IAdapterContext context) : base("/api/auth/change-password", context)
            {
            }

            protected override async Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                if (!HasCsrfHeader(request))
                {
                    return Json(403, new { error = "Ungültige Anfrage" });
                }

                var resolved = await ResolveSessionAsync(Context, request);
                if (resolved is null)
                {
                    return Json(401, new { error = "Authentifizierung erforderlich" });
                }

                using var document = ParseBody(request);
                var current = document is null ? null : Str(document.RootElement, "currentPassword");
                var next = document is null ? null : Str(document.RootElement, "newPassword");
                if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
                {
                    return Json(400, new { error = "Aktuelles und neues Passwort erforderlich" });
                }

                var (security, _, user) = resolved.Value;
                var verification = await security.Authentication.LoginAsync(user.Username, current);
                if (!verification.Success || verification.Token is null)
                {
                    await Task.Delay(500);
                    return Json(400, new { error = "Aktuelles Passwort ist falsch" });
                }

                await security.Authentication.LogoutAsync(verification.Token);

                try
                {
                    await security.Users.SetPasswordAsync(user.Username, next, mustChangePassword: false);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return Json(400, new { error = ex.Message });
                }

                // SetPassword widerruft alle Sitzungen, daher direkt neu anmelden und Cookie ersetzen.
                var relogin = await security.Authentication.LoginAsync(user.Username, next);
                if (!relogin.Success || relogin.Token is null)
                {
                    return Json(200, new { success = true, relogin = false }, BuildSessionCookie(string.Empty, 0));
                }

                var maxAge = Math.Max(60, security.Options.SessionTimeoutMinutes * 60);
                return Json(200, new { success = true }, BuildSessionCookie(relogin.Token, maxAge));
            }
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
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>(maxLines);
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == maxLines)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }

            return lines.ToArray();
        }

        private static EndpointResponse Unauthorized()
        {
            if (Forbidden.Value)
            {
                return Response(403, "{\"error\":\"Keine Berechtigung\"}", "application/json; charset=utf-8");
            }

            return Response(401, "{\"error\":\"Authentifizierung erforderlich\"}", "application/json; charset=utf-8");
        }

        private static EndpointResponse Response(int statusCode, string body, string contentType)
        {
            return EndpointResponse.FromText(statusCode, body, contentType);
        }
    }
}
