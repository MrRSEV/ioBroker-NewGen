using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ioBroker_NewGen.Core.Registry;
using RSEV.Utilities.Logging;
using RSEV.Utilities.Processes;

namespace ioBroker_NewGen.Core.Bridge
{
    /// <summary>
    /// Verwaltet isolierte Node.js-Prozesse für Legacy-ioBroker-Adapter.
    /// Nutzt die Prozess-Infrastruktur aus RSEV.Utilities
    /// (<see cref="NodeProcess"/>, <see cref="RuntimeProcessController"/>)
    /// und ergänzt Crash-Recovery sowie periodische Heartbeat-/Health-Checks.
    /// </summary>
    public sealed class NodeAdapterRuntime : IAsyncDisposable
    {
        private readonly RuntimeProcessController _processController;
        private readonly AdapterRegistry _adapterRegistry;
        private readonly ILogger _logger;
        private readonly TimeSpan _crashRestartDelay;
        private readonly TimeSpan _healthCheckInterval;

        private readonly ConcurrentDictionary<string, string> _processIdByAdapterId = new();
        private Timer? _healthCheckTimer;

        public NodeAdapterRuntime(
            RuntimeProcessController processController,
            AdapterRegistry adapterRegistry,
            ILogger logger,
            TimeSpan? crashRestartDelay = null,
            TimeSpan? healthCheckInterval = null)
        {
            _processController = processController ?? throw new ArgumentNullException(nameof(processController));
            _adapterRegistry = adapterRegistry ?? throw new ArgumentNullException(nameof(adapterRegistry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _crashRestartDelay = crashRestartDelay ?? TimeSpan.FromMilliseconds(2000);
            _healthCheckInterval = healthCheckInterval ?? TimeSpan.FromMilliseconds(5000);
        }

        /// <summary>
        /// Startet einen isolierten Node.js-Adapterprozess, registriert ihn in der
        /// <see cref="AdapterRegistry"/> (Typ <see cref="Routing.AdapterType.IoBroker"/>)
        /// und beim <see cref="RuntimeProcessController"/>.
        /// </summary>
        /// <param name="name">Sprechender Adaptername (z. B. "mqtt").</param>
        /// <param name="scriptPath">Pfad zum Node.js-Einstiegsskript des Adapters.</param>
        /// <param name="arguments">Optionale Kommandozeilen-Argumente.</param>
        public async Task<AdapterRegistration> StartAdapterAsync(string name, string scriptPath, string[]? arguments = null)
        {
            var registration = _adapterRegistry.Register(name, Routing.AdapterType.IoBroker);
            registration.Metadata["scriptPath"] = scriptPath;
            _adapterRegistry.UpdateStatus(registration.Id, AdapterStatus.Connecting);

            var process = new NodeProcess(registration.Id, scriptPath, arguments, _logger);
            process.OnStdErr += (_, line) => _logger.LogWarning($"[Node:{registration.Id}] {line}");

            _processController.Register(process);
            _processIdByAdapterId[registration.Id] = process.ProcessId;

            var started = await process.StartAsync();
            _adapterRegistry.UpdateStatus(registration.Id, started ? AdapterStatus.Connected : AdapterStatus.Faulted);

            if (!started)
            {
                _logger.LogError($"[NodeRuntime] Adapter '{registration.Id}' konnte nicht gestartet werden (Skript: {scriptPath}).");
            }

            return registration;
        }

        /// <summary>
        /// Stoppt den Node.js-Prozess eines Adapters und aktualisiert dessen Status.
        /// </summary>
        public async Task<bool> StopAdapterAsync(string adapterId)
        {
            if (!_processIdByAdapterId.TryGetValue(adapterId, out var processId))
            {
                return false;
            }

            var stopped = await _processController.StopAsync(processId);
            _adapterRegistry.UpdateStatus(adapterId, AdapterStatus.Disconnected);
            return stopped;
        }

        /// <summary>
        /// Startet den periodischen Health-Check/Crash-Recovery-Timer.
        /// Prozesse, die nicht mehr "gesund" sind, werden automatisch neu gestartet.
        /// </summary>
        public void StartHealthMonitoring()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = new Timer(
                _ => CheckAndRecoverAsync().GetAwaiter().GetResult(),
                state: null,
                dueTime: _healthCheckInterval,
                period: _healthCheckInterval);
        }

        /// <summary>
        /// Stoppt den Health-Check-Timer.
        /// </summary>
        public void StopHealthMonitoring()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = null;
        }

        /// <summary>
        /// Stoppt alle verwalteten Node.js-Prozesse (z. B. beim Systemstopp).
        /// </summary>
        public async Task StopAllAsync()
        {
            StopHealthMonitoring();

            foreach (var adapterId in _processIdByAdapterId.Keys.ToList())
            {
                await StopAdapterAsync(adapterId);
            }
        }

        private async Task CheckAndRecoverAsync()
        {
            foreach (var (adapterId, processId) in _processIdByAdapterId.ToList())
            {
                var process = _processController.Get(processId);
                if (process is null)
                {
                    continue;
                }

                bool? healthy;
                try
                {
                    healthy = await process.CheckHealthAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[NodeRuntime] Health-Check für Adapter '{adapterId}' fehlgeschlagen: {ex.Message}");
                    healthy = false;
                }

                if (healthy == false)
                {
                    _logger.LogWarning($"[NodeRuntime] Adapter '{adapterId}' unhealthy/abgestürzt – Neustart nach {_crashRestartDelay.TotalMilliseconds} ms.");
                    _adapterRegistry.UpdateStatus(adapterId, AdapterStatus.Faulted);

                    await Task.Delay(_crashRestartDelay);

                    var restarted = await process.RestartAsync();
                    _adapterRegistry.UpdateStatus(adapterId, restarted ? AdapterStatus.Connected : AdapterStatus.Faulted);

                    if (restarted)
                    {
                        _logger.LogInfo($"[NodeRuntime] Adapter '{adapterId}' erfolgreich neu gestartet.");
                    }
                    else
                    {
                        _logger.LogError($"[NodeRuntime] Neustart von Adapter '{adapterId}' fehlgeschlagen.");
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAllAsync();
        }
    }
}
