using System.Collections.Generic;

namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Semantischer Index für Embeddings. Ermöglicht k-nächste-Nachbarn
    /// Suche über alle registrierten States der <see cref="SemanticStateMatrix"/>.
    /// </summary>
    public interface IVectorIndex
    {
        /// <summary>
        /// Fügt (oder aktualisiert) den Embedding-Eintrag für die übergebene ID.
        /// </summary>
        void Add(string id, float[] embedding);

        /// <summary>
        /// Entfernt den Embedding-Eintrag für die übergebene ID.
        /// </summary>
        void Remove(string id);

        /// <summary>
        /// Führt eine k-nächste-Nachbarn Suche nach dem übergebenen Embedding durch.
        /// </summary>
        /// <returns>Liste von (Id, Score) sortiert nach absteigender Ähnlichkeit.</returns>
        IReadOnlyList<(string Id, float Score)> Search(float[] queryEmbedding, int k);
    }
}
