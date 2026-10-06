using System;
using System.Collections.Generic;
using ioBroker_NewGen.Core.Routing;

namespace ioBroker_NewGen.Core.Registry
{
    /// <summary>
    /// Registrierter Eintrag für einen Adapter (Custom oder ioBroker-Legacy).
    /// Wird von der <see cref="AdapterRegistry"/> verwaltet.
    /// </summary>
    public sealed class AdapterRegistration
    {
        /// <summary>
        /// Sprechender Name des Adapters (z. B. "mqtt", "zigbee").
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Eindeutige ID des Adapters im Format "Name.id" (vom Host beim Handshake vergeben).
        /// </summary>
        public required string Id { get; set; }

        /// <summary>
        /// Adaptertyp: Custom (Host-intern) oder IoBroker (TCP-Bridge, Legacy).
        /// </summary>
        public required AdapterType Type { get; init; }

        /// <summary>
        /// Aktueller Lebenszyklus-Status.
        /// </summary>
        public AdapterStatus Status { get; set; } = AdapterStatus.Created;

        /// <summary>
        /// Beliebige zusätzliche Metadaten (z. B. Version, Hersteller, Node.js-Skriptpfad).
        /// </summary>
        public Dictionary<string, string> Metadata { get; init; } = new();

        /// <summary>
        /// TCP-Endpunkt des Adapters (nur für <see cref="AdapterType.IoBroker"/> relevant,
        /// sobald die Verbindung über die TCP-Bridge aufgebaut wurde).
        /// </summary>
        public string? TcpEndpoint { get; set; }

        /// <summary>
        /// Referenz auf die laufende Instanz (nur für <see cref="AdapterType.Custom"/> relevant,
        /// z. B. das geladene Plugin-/Assembly-Objekt – wird in Sprint 3 scharf geschaltet).
        /// </summary>
        public object? InstanceReference { get; set; }

        /// <summary>
        /// Zeitpunkt der Registrierung (UTC).
        /// </summary>
        public DateTime RegisteredAt { get; } = DateTime.UtcNow;

        /// <summary>
        /// Zeitpunkt des letzten Handshakes/Heartbeats (UTC), falls bekannt.
        /// </summary>
        public DateTime? LastSeenAt { get; set; }
    }
}
