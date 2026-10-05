using System.Collections.Generic;

namespace ioBroker_NewGen.Core.Bus
{
    /// <summary>
    /// Art des Bus-Frames: unterscheidet rohe Payloads, State-Updates,
    /// Befehle und Events.
    /// </summary>
    public enum BusFrameKind
    {
        Raw,
        StateUpdate,
        Command,
        Event
    }

    /// <summary>
    /// Zusätzliche, optionale Metadaten eines <see cref="BusFrame"/>
    /// (z. B. für Routing-Entscheidungen oder Korrelation von Anfragen/Antworten).
    /// </summary>
    public sealed class BusFrameMetadata
    {
        /// <summary>
        /// Beliebige Schlüssel-Wert-Paare (z. B. "correlationId", "replyTo").
        /// </summary>
        public Dictionary<string, string> Values { get; init; } = new();

        public static BusFrameMetadata Empty { get; } = new();
    }
}
