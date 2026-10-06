using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ioBroker_NewGen.Core.Routing;

namespace ioBroker_NewGen.Core.Registry
{
    /// <summary>
    /// Thread-sichere Registry aller bekannten Adapter (Custom + ioBroker-Legacy).
    /// Vergibt beim TCP-Bridge-Handshake eindeutige IDs im Format "Name.id".
    /// </summary>
    public sealed class AdapterRegistry
    {
        private readonly ConcurrentDictionary<string, AdapterRegistration> _adaptersById = new();
        private readonly ConcurrentDictionary<string, int> _instanceCounters = new();
        private readonly object _idLock = new();

        /// <summary>
        /// Registriert einen neuen Adapter und vergibt, falls noch keine ID gesetzt ist,
        /// automatisch eine neue eindeutige ID im Format "Name.id" (ID beginnt bei 0).
        /// </summary>
        public AdapterRegistration Register(string name, AdapterType type, Dictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Adaptername darf nicht leer sein.", nameof(name));
            }

            var id = AssignNextId(name);
            var registration = new AdapterRegistration
            {
                Name = name,
                Id = id,
                Type = type,
                Metadata = metadata ?? new Dictionary<string, string>()
            };

            _adaptersById[id] = registration;
            return registration;
        }

        /// <summary>
        /// Vergibt die nächste freie Instanz-ID für den übergebenen Adapternamen
        /// (Format "Name.id", z. B. "mqtt.0", "mqtt.1", ...).
        /// </summary>
        public string AssignNextId(string name)
        {
            lock (_idLock)
            {
                var nextInstance = _instanceCounters.AddOrUpdate(name, 0, (_, current) => current + 1);
                return $"{name}.{nextInstance}";
            }
        }

        /// <summary>
        /// Liefert den registrierten Adapter zur übergebenen ID, oder <c>null</c>, falls unbekannt.
        /// </summary>
        public AdapterRegistration? Get(string id)
        {
            return _adaptersById.TryGetValue(id, out var registration) ? registration : null;
        }

        /// <summary>
        /// Prüft, ob ein Adapter mit der übergebenen ID existiert.
        /// </summary>
        public bool Exists(string id) => _adaptersById.ContainsKey(id);

        /// <summary>
        /// Liefert alle registrierten Adapter.
        /// </summary>
        public IReadOnlyCollection<AdapterRegistration> GetAll() => _adaptersById.Values.ToList();

        /// <summary>
        /// Liefert alle registrierten Adapter eines bestimmten Typs.
        /// </summary>
        public IReadOnlyCollection<AdapterRegistration> GetByType(AdapterType type)
        {
            return _adaptersById.Values.Where(a => a.Type == type).ToList();
        }

        /// <summary>
        /// Entfernt einen Adapter aus der Registry (z. B. nach Verbindungsabbruch).
        /// </summary>
        public bool Unregister(string id)
        {
            return _adaptersById.TryRemove(id, out _);
        }

        /// <summary>
        /// Aktualisiert Status und optional den Zeitpunkt des letzten Lebenszeichens eines Adapters.
        /// </summary>
        public void UpdateStatus(string id, AdapterStatus status, bool touchLastSeen = true)
        {
            if (_adaptersById.TryGetValue(id, out var registration))
            {
                registration.Status = status;
                if (touchLastSeen)
                {
                    registration.LastSeenAt = DateTime.UtcNow;
                }
            }
        }
    }
}
