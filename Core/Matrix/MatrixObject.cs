using System.Collections.Generic;
using System.Linq;

namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Beschreibt ein Gerät, einen Kanal oder einen Datenpunkt innerhalb der
    /// <see cref="SemanticStateMatrix"/>. Dient als Identitätsträger für
    /// einen oder mehrere <see cref="StateNode"/>-Einträge.
    /// </summary>
    public sealed class MatrixObject
    {
        /// <summary>
        /// Eindeutige ID des Objekts (z. B. "temp.livingroom").
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Sprechender Name des Objekts.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Typ des Objekts (z. B. "device", "channel", "state").
        /// </summary>
        public required string Type { get; init; }

        /// <summary>
        /// Beliebige zusätzliche Metadaten (Rolle, Einheit, Hersteller etc.).
        /// </summary>
        public Dictionary<string, string> Meta { get; init; } = new();

        /// <summary>
        /// Baut den Text zusammen, der als Grundlage für das semantische
        /// Objekt-Embedding verwendet wird (Id + Name + Type + Meta).
        /// </summary>
        public string BuildEmbeddingSourceText()
        {
            var metaText = string.Join(' ', Meta.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
            return $"{Id} {Name} {Type} {metaText}".Trim();
        }
    }
}
