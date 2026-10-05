namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Erzeugt semantische Embeddings aus Text oder Payloads.
    /// Implementierungen können später durch echte ML-Modelle
    /// (z. B. ONNX, Sentence-Transformers) ersetzt werden.
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Dimension der erzeugten Embedding-Vektoren.
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Erzeugt einen Embedding-Vektor für den übergebenen Text.
        /// </summary>
        float[] Encode(string text);
    }
}
