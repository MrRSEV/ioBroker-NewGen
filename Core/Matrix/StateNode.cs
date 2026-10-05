using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Aktueller Zustand eines <see cref="MatrixObject"/> inklusive
    /// semantischer Informationen (Embedding) und History.
    /// Thread-sicher für nebenläufige Lese-/Schreibzugriffe.
    /// </summary>
    public sealed class StateNode
    {
        private readonly object _valueLock = new();
        private readonly int _maxInMemoryHistory;

        /// <summary>
        /// Eindeutige ID des Zustands (identisch mit der zugehörigen <see cref="MatrixObject.Id"/>).
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Aktueller Wert des Zustands.
        /// </summary>
        public object? Value { get; private set; }

        /// <summary>
        /// Zeitstempel der letzten Aktualisierung (UTC).
        /// </summary>
        public DateTime Timestamp { get; private set; }

        /// <summary>
        /// Letzter Benutzer/Adapter, der den Wert gesetzt hat.
        /// </summary>
        public string? LastWrittenBy { get; private set; }

        /// <summary>
        /// Semantisches Embedding des aktuellen Payloads.
        /// </summary>
        public float[] Embedding { get; private set; } = Array.Empty<float>();

        /// <summary>
        /// Beliebige zusätzliche Metadaten (Rolle, Einheit, Typ etc.).
        /// </summary>
        public Dictionary<string, string> Meta { get; init; } = new();

        /// <summary>
        /// In-Memory-Verlauf der letzten Werte (begrenzt). Die vollständige
        /// History wird über die Snapshot-Datei persistiert.
        /// </summary>
        public ConcurrentQueue<HistorySample> History { get; } = new();

        public StateNode(string id, int maxInMemoryHistory = 500)
        {
            Id = id;
            _maxInMemoryHistory = maxInMemoryHistory;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Aktualisiert Wert, Zeitstempel, Embedding und History thread-sicher.
        /// </summary>
        internal HistorySample Update(object? value, float[] embedding, string? writtenBy)
        {
            lock (_valueLock)
            {
                Value = value;
                Embedding = embedding;
                Timestamp = DateTime.UtcNow;
                LastWrittenBy = writtenBy;

                var sample = new HistorySample(Id, Timestamp, value, embedding);
                History.Enqueue(sample);

                while (History.Count > _maxInMemoryHistory && History.TryDequeue(out _))
                {
                    // Älteste In-Memory-Einträge verwerfen; die vollständige
                    // History bleibt über den Snapshot erhalten.
                }

                return sample;
            }
        }

        /// <summary>
        /// Stellt einen State aus einem geladenen Snapshot wieder her,
        /// ohne eine neue History-Sample-Erzeugung auszulösen.
        /// </summary>
        internal void Restore(object? value, DateTime timestamp, float[] embedding, string? writtenBy)
        {
            lock (_valueLock)
            {
                Value = value;
                Timestamp = timestamp;
                Embedding = embedding;
                LastWrittenBy = writtenBy;
            }
        }

        /// <summary>
        /// Fügt eine aus dem Snapshot rekonstruierte History-Sample direkt hinzu,
        /// ohne den aktuellen Wert zu verändern.
        /// </summary>
        internal void AppendHistorySample(HistorySample sample)
        {
            History.Enqueue(sample);
            while (History.Count > _maxInMemoryHistory && History.TryDequeue(out _))
            {
            }
        }
    }
}
