using System;

namespace ioBroker_NewGen.Core.Bus
{
    /// <summary>
    /// Neutrales Transport-Paket des <see cref="SystemMessageBus"/>. Kennt
    /// weder den konkreten Adaptertyp noch die Semantik des Inhalts – reine
    /// Transportstruktur gemäß Architektur-Vorgabe ("Systembus transportiert
    /// Payloads ohne Kenntnis des Adaptertyps").
    /// </summary>
    /// <param name="AdapterId">ID des sendenden/zugehörigen Adapters (Format "Name.id", siehe TCP-Bridge-Handshake).</param>
    /// <param name="Address">Logische Zieladresse, z. B. eine State-ID ("temp.livingroom").</param>
    /// <param name="Payload">Rohdaten des Frames.</param>
    /// <param name="ValueType">Typbezeichnung des Payloads (z. B. "double", "bool", "json").</param>
    /// <param name="Kind">Art des Frames.</param>
    /// <param name="Timestamp">Erzeugungszeitpunkt (UTC).</param>
    /// <param name="Metadata">Zusätzliche, optionale Metadaten.</param>
    public sealed record BusFrame(
        string AdapterId,
        string Address,
        ReadOnlyMemory<byte> Payload,
        string ValueType,
        BusFrameKind Kind,
        DateTime Timestamp,
        BusFrameMetadata Metadata)
    {
        /// <summary>
        /// Erstellt ein <see cref="BusFrame"/> mit aktuellem UTC-Zeitstempel
        /// und leeren Metadaten, sofern nicht anders angegeben.
        /// </summary>
        public static BusFrame Create(
            string adapterId,
            string address,
            ReadOnlyMemory<byte> payload,
            string valueType,
            BusFrameKind kind,
            BusFrameMetadata? metadata = null)
        {
            return new BusFrame(
                adapterId,
                address,
                payload,
                valueType,
                kind,
                DateTime.UtcNow,
                metadata ?? BusFrameMetadata.Empty);
        }
    }
}
