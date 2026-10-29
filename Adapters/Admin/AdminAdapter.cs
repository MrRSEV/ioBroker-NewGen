using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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
            Context.ApiHost.Register(new LogoutEndpoint(Context));
            Context.ApiHost.Register(new MeEndpoint(Context));
            Context.ApiHost.Register(new ChangePasswordEndpoint(Context));
            foreach (var i18nMode in new[] { "languages", "translations", "translate", "extend" })
            {
                Context.ApiHost.Register(new I18nEndpoint(i18nMode, Context));
            }
            Context.ApiHost.Register(new StatesEndpoint(Context));
            Context.ApiHost.Register(new AdaptersEndpoint(Context));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/start", Context, "start"));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/stop", Context, "stop"));
            Context.ApiHost.Register(new AdapterCommandEndpoint("/api/adapters/restart", Context, "restart"));
            Context.ApiHost.Register(new AutostartEndpoint(Context));
            Context.ApiHost.Register(new AdapterInstallEndpoint(Context));
            Context.ApiHost.Register(new RemoteAdapterInstallEndpoint(Context));
            Context.ApiHost.Register(new ConfigurationEndpoint(Context));
            Context.ApiHost.Register(new LogsEndpoint(Context));
            Context.ApiHost.Register(new PermissionEndpoint(Context));
            Context.ApiHost.Register(new RoleEndpoint(Context));
            Context.ApiHost.Register(new UserRoleEndpoint(Context));
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

        private sealed class DashboardEndpoint : BaseRuntimeEndpoint
        {
            public DashboardEndpoint(string route) : base(route)
            {
            }

            protected override Task<EndpointResponse> OnHandleAsync(EndpointRequest request)
            {
                const string html = """
                    <!doctype html><html lang="de"><head><meta charset="utf-8"><title>ioBroker-NewGen Admin</title>
                    <style>body{font-family:system-ui;margin:2rem;background:#f5f7fa}section{background:white;padding:1rem;margin:1rem 0;border-radius:8px}pre{white-space:pre-wrap}input{min-width:16rem;margin:.25rem 0;display:block}.hidden{display:none}#login,#change{max-width:24rem;margin:4rem auto}.error{color:#b00020}.tabs{margin-bottom:.5rem}.tabs button{border:1px solid #c5ccd6;background:#eef1f5;padding:.35rem .8rem;cursor:pointer}.tabs button.active{background:#1f6feb;color:#fff;border-color:#1f6feb}table{border-collapse:collapse;width:100%}th,td{border:1px solid #dde2e9;padding:.35rem .6rem;text-align:left;font-size:.9rem}th{background:#f0f3f7}table.tree tr:hover td{background:#f5f9ff}.arrow{display:inline-block;width:1.1rem;font-size:.7rem;color:#57606a}.folder{font-weight:600}.muted{color:#8a929c}.prop td{background:#fafbfc;font-size:.85rem}.prop-name{color:#57606a}.st-Running{color:#1a7f37;font-weight:600}.st-Faulted{color:#b00020;font-weight:600}</style>
                    </head><body>
                    <section id="login" class="hidden"><h1>ioBroker-NewGen</h1><form id="loginForm"><label><span data-i18n="user.username">Benutzername</span><input id="user" autocomplete="username" required/></label><label><span data-i18n="user.password">Passwort</span><input id="pass" type="password" autocomplete="current-password" required/></label><button type="submit" data-i18n="user.login">Anmelden</button> <span id="loginError" class="error"></span><select class="langsel" title="Language"></select></form></section>
                    <section id="change" class="hidden"><h2 data-i18n="user.changePassword">Passwort ändern</h2><p data-i18n="user.changePasswordHint">Das Passwort muss vor der weiteren Nutzung geändert werden.</p><form id="changeForm"><label><span data-i18n="user.currentPassword">Aktuelles Passwort</span><input id="oldPass" type="password" autocomplete="current-password" required/></label><label><span data-i18n="user.newPassword">Neues Passwort (min. 8 Zeichen)</span><input id="newPass" type="password" autocomplete="new-password" required/></label><button type="submit" data-i18n="user.change">Ändern</button> <span id="changeError" class="error"></span></form></section>
                    <div id="app" class="hidden"><h1>ioBroker-NewGen</h1><section><span id="who"></span> <button id="logout" type="button" data-i18n="user.logout">Abmelden</button> <select class="langsel" title="Language"></select> <span id="status"></span></section><section><h2 data-i18n="menu.states">States</h2><div class="tabs" data-for="states"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div><div id="statesTable">Lade...</div><pre id="states" class="hidden">Lade...</pre></section><section><h2 data-i18n="menu.adapters">Adapter</h2><div class="tabs" data-for="adapters"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div><div id="adaptersTable">Lade...</div><pre id="adapters" class="hidden">Lade...</pre></section><section><h2 data-i18n="menu.settings">Konfiguration</h2><div class="tabs" data-for="config"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div><div id="configTable">Lade...</div><pre id="config" class="hidden">Lade...</pre></section><section><h2 data-i18n="menu.roles">Rollen</h2><div class="tabs" data-for="roles"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div><div id="rolesTable">Lade...</div><pre id="roles" class="hidden">Lade...</pre></section><section><h2 data-i18n="menu.users">Benutzer/Rollen</h2><div class="tabs" data-for="users"><button data-view="table" type="button" class="active">Display as Table</button><button data-view="json" type="button">Display as JSON</button></div><div id="usersTable">Lade...</div><pre id="users" class="hidden">Lade...</pre></section></div>
                    <script>
                    const $=id=>document.getElementById(id);let timer=null;
                    let i18n={};const t=(k,d)=>i18n[k]||d||k;
                    function applyI18n(){document.querySelectorAll('[data-i18n]').forEach(e=>{e.textContent=t(e.dataset.i18n,e.textContent);});document.querySelectorAll('.tabs button').forEach(b=>{b.textContent=t('view.'+b.dataset.view,b.textContent);});document.documentElement.lang=(i18n['language.code']||curLang||'de').slice(0,2);}
                    let curLang=localStorage.getItem('ngLang')||'';
                    async function loadI18n(lang){try{const r=await fetch('/api/i18n/translations',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(lang?{language:lang}:{})});const d=await r.json();i18n=d.translations||{};curLang=d.language||curLang;}catch(e){}applyI18n();if(typeof refresh==='function'&&!$('app').classList.contains('hidden'))refresh();}
                    async function initLanguages(){try{const r=await fetch('/api/i18n/languages');const d=await r.json();if(!curLang)curLang=d.language;document.querySelectorAll('.langsel').forEach(s=>{s.replaceChildren(...d.languages.map(l=>{const o=document.createElement('option');o.value=l.code;o.textContent=l.name+' ('+l.code+')';return o;}));s.value=curLang;s.onchange=()=>{curLang=s.value;localStorage.setItem('ngLang',curLang);document.querySelectorAll('.langsel').forEach(x=>x.value=curLang);loadI18n(curLang);};});}catch(e){}await loadI18n(curLang);}
                    initLanguages();
                    function show(name){for(const id of ['login','change','app'])$(id).classList.toggle('hidden',id!==name);if(name!=='app'&&timer){clearInterval(timer);timer=null;}}
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
                    function actionCell(row,a){const cell=row.insertCell();if(a.Type!=='Custom')return;const status=a.Status;const defs=[['start','AdapterStart',status!=='Connected'&&status!=='Connecting'],['stop','AdapterStop',status==='Connected'],['restart','AdapterRestart',status==='Connected']];for(const [action,perm,enabled] of defs){if(!perms.has(perm))continue;const b=document.createElement('button');b.type='button';b.textContent=t('action.'+action,action);b.disabled=!enabled;b.onclick=e=>{e.stopPropagation();if((action==='stop'||action==='restart')&&/^admin(\.|$)/i.test(String(a.Id))&&!confirm(t(action==='stop'?'confirm.stopAdmin':'confirm.restartAdmin',action==='stop'?'Achtung: Damit wird das komplette Admin-UI gestoppt und Sie verlieren den Zugriff, bis es neu gestartet wird. Wirklich stoppen?':'Achtung: Das komplette Admin-UI wird neu gestartet und ist kurzzeitig nicht erreichbar. Wirklich neu starten?')))return;adapterAction(a.Id,action,b);};cell.appendChild(b);cell.appendChild(document.createTextNode(' '));}}
                    function autoCell(cell,a){if(!a.AutoStartEditable)return;const cb=document.createElement('input');cb.type='checkbox';cb.checked=!!a.AutoStart;cb.disabled=true;cb.style.pointerEvents='none';cell.style.cursor='pointer';cell.title=t('table.autostart','Autostart');cell.appendChild(cb);
                    const lock=()=>{cb.disabled=true;cb.style.pointerEvents='none';document.removeEventListener('click',outside,true);};
                    const outside=e=>{if(!cell.isConnected||!cell.contains(e.target))lock();};
                    cell.onclick=e=>{e.stopPropagation();if(!perms.has('SystemConfigure')||!cb.disabled)return;cb.disabled=false;cb.style.pointerEvents='auto';document.addEventListener('click',outside,true);};
                    cb.onchange=async()=>{const wanted=cb.checked;cb.disabled=true;try{await api('/api/adapters/autostart',{adapterId:a.Id,enabled:wanted});a.AutoStart=wanted;}catch(error){cb.checked=!wanted;alert(error.message);}cb.disabled=false;};}
                    const showActions=()=>['AdapterStart','AdapterStop','AdapterRestart'].some(p=>perms.has(p));
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
                    async function loadJson(path){try{return await api(path);}catch(error){if(error.message==='Nicht angemeldet')throw error;return {error:error.message};}}
                    async function refresh(){try{const [states,adapters,config,roles,users]=await Promise.all([loadJson('/api/states'),loadJson('/api/adapters'),loadJson('/api/config'),loadJson('/api/roles'),loadJson('/api/users')]);$('states').textContent=JSON.stringify(states,null,2);renderTree('states',states);$('adapters').textContent=JSON.stringify(adapters,null,2);renderAdapters(adapters);$('config').textContent=JSON.stringify(config,null,2);renderTree('config',config);$('roles').textContent=JSON.stringify(roles,null,2);renderTree('roles',roles);$('users').textContent=JSON.stringify(users,null,2);renderTree('users',users);$('status').textContent=' Verbunden';}catch(error){$('status').textContent=' '+error.message;}}
                    async function start(){try{const me=await api('/api/auth/me');if(me.mustChangePassword){show('change');return;}perms.clear();(me.permissions||[]).forEach(p=>perms.add(p));$('who').textContent='Angemeldet als '+(me.displayName||me.username)+' ('+me.roles.join(', ')+')';show('app');refresh();if(!timer)timer=setInterval(refresh,2000);}catch{show('login');}}
                    $('loginForm').onsubmit=async e=>{e.preventDefault();$('loginError').textContent='';try{await api('/api/auth/login',{username:$('user').value,password:$('pass').value});$('pass').value='';start();}catch(error){$('loginError').textContent=error.message;}};
                    $('changeForm').onsubmit=async e=>{e.preventDefault();$('changeError').textContent='';try{await api('/api/auth/change-password',{currentPassword:$('oldPass').value,newPassword:$('newPass').value});$('oldPass').value='';$('newPass').value='';start();}catch(error){$('changeError').textContent=error.message;}};
                    $('logout').onclick=async()=>{try{await api('/api/auth/logout',{});}catch{}show('login');};
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
            return enabled is null || enabled.Any(entry =>
                string.Equals(entry, adapter.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry, adapter.Name, StringComparison.OrdinalIgnoreCase));
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

                var current = ReadEnabledAdapters(_context)
                    ?? _context.AdapterRegistry.GetAll().Where(a => a.Type == AdapterType.Custom).Select(a => a.Id).ToArray();
                var updated = current
                    .Where(entry => !string.Equals(entry, adapter.Id, StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(entry, adapter.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (enabled)
                {
                    updated.Add(adapter.Id);
                }

                _context.Config.Set("adapter.enabledAdapters", updated.ToArray());
                _context.Config.Save(Path.Combine(AppContext.BaseDirectory, "config.toml"));
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
                        AutoStart = IsAutoStart(enabledAdapters, adapter),
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

        private static class SecurityListings
        {
            public static async Task<EndpointResponse> RolesAsync(SecurityService security)
            {
                var roles = await security.Roles.ListAsync();
                var payload = roles.Select(role => new
                {
                    name = role.Name,
                    description = role.Description,
                    permissions = SecurityRoles.Flatten(role.Name).OrderBy(p => p, StringComparer.Ordinal).ToArray()
                });
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
                "/api/adapters/autostart" => SecurityPermissions.SystemConfigure,
                "/api/adapters/install" or "/api/adapters/install-remote" => SecurityPermissions.AdapterInstall,
                "/api/config" => write ? SecurityPermissions.SystemConfigure : SecurityPermissions.SystemRead,
                "/api/logs" => SecurityPermissions.SystemRead,
                "/api/i18n/extend" => SecurityPermissions.SystemConfigure,
                "/api/users" => write ? SecurityPermissions.UserManage : SecurityPermissions.UserRead,
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
