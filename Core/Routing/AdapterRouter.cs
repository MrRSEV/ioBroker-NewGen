using System;
using ioBroker_NewGen.Core.Bus;
using RSEV.Utilities.Logging;

namespace ioBroker_NewGen.Core.Routing
{
    /// <summary>
    /// Entscheidet anhand des Adaptertyps, ob ein <see cref="BusFrame"/>
    /// intern (direkt über den <see cref="SystemMessageBus"/>) oder extern
    /// (über die TCP-Bridge, Legacy-ioBroker-Adapter) transportiert wird.
    /// </summary>
    /// <remarks>
    /// Sprint-1-Skeleton: Die TCP-Bridge-Anbindung für <see cref="AdapterType.IoBroker"/>
    /// folgt in Sprint 2. Bis dahin wird der Routing-Vorgang lediglich geloggt.
    /// Externe Payloads werden gemäß Architektur 1:1 auf den Systembus gespiegelt.
    /// </remarks>
    public sealed class AdapterRouter
    {
        private readonly SystemMessageBus _systemBus;
        private readonly ILogger _logger;

        public AdapterRouter(SystemMessageBus systemBus, ILogger logger)
        {
            _systemBus = systemBus ?? throw new ArgumentNullException(nameof(systemBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Routet ein Frame gemäß Adaptertyp:
        /// <list type="bullet">
        /// <item><see cref="AdapterType.Custom"/> ? direkt über den Systembus (intern).</item>
        /// <item><see cref="AdapterType.IoBroker"/> ? TCP-Bridge (folgt Sprint 2, aktuell Platzhalter/Log).</item>
        /// </list>
        /// </summary>
        public void Route(BusFrame frame, AdapterType adapterType)
        {
            ArgumentNullException.ThrowIfNull(frame);

            switch (adapterType)
            {
                case AdapterType.Custom:
                    RouteInternal(frame);
                    break;

                case AdapterType.IoBroker:
                    RouteToTcpBridge(frame);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(adapterType), adapterType, "Unbekannter Adaptertyp.");
            }
        }

        /// <summary>
        /// Spiegelt ein von extern empfangenes Payload 1:1 auf den Systembus.
        /// </summary>
        public void RouteExternalPayload(BusFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            RouteInternal(frame);
        }

        private void RouteInternal(BusFrame frame)
        {
            _systemBus.Publish(frame);
        }

        private void RouteToTcpBridge(BusFrame frame)
        {
            // TODO (Sprint 2): Anbindung an die TCP-Bridge zur Weiterleitung
            // an isolierte Node.js-Adapter-Prozesse.
            _logger.LogInfo(
                $"[AdapterRouter] Frame für Adresse '{frame.Address}' von Adapter '{frame.AdapterId}' " +
                "würde an die TCP-Bridge gehen (folgt in Sprint 2).");
        }
    }
}
