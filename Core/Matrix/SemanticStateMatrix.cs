using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ioBroker_NewGen.Core.Matrix
{
    /// <summary>
    /// Zentrale Engine für Objekte, States, Semantik, History und Persistenz.
    /// Geometrischer, mehrdimensionaler Vectorspace: jeder State besitzt neben
    /// seinem Wert auch ein semantisches Embedding, über das sich der gesamte
    /// Objektbaum durchsuchen lässt (<see cref="SemanticQuery"/>).
    /// Thread-sicher über <see cref="ConcurrentDictionary{TKey,TValue}"/> und
    /// interne Locks in <see cref="StateNode"/>.
    /// </summary>
    public sealed class SemanticStateMatrix : IDisposable
    {
        private readonly ConcurrentDictionary<string, MatrixObject> _objects = new();
        private readonly ConcurrentDictionary<string, StateNode> _states = new();
        private readonly ConcurrentDictionary<string, List<Action<StateNode>>> _subscriptions = new();
        private readonly object _subscriptionLock = new();

        private readonly IRoleService _roleService;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorIndex _semanticIndex;
        private readonly string _snapshotPath;

        private Timer? _cronTimer;

        /// <summary>
        /// Erstellt eine neue <see cref="SemanticStateMatrix"/>.
        /// </summary>
        /// <param name="snapshotPath">Pfad zur JSONL-Snapshot-Datei.</param>
        /// <param name="roleService">Rollen-/Berechtigungsdienst. Standard: <see cref="DefaultRoleService"/> (voller Zugriff).</param>
        /// <param name="embeddingService">Embedding-Dienst. Standard: <see cref="HashingEmbeddingService"/>.</param>
        /// <param name="semanticIndex">Semantischer Index. Standard: <see cref="VectorIndex"/> (Brute-Force-Kosinus).</param>
        public SemanticStateMatrix(
            string snapshotPath,
            IRoleService? roleService = null,
            IEmbeddingService? embeddingService = null,
            IVectorIndex? semanticIndex = null)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath))
            {
                throw new ArgumentException("Snapshot-Pfad darf nicht leer sein.", nameof(snapshotPath));
            }

            _snapshotPath = snapshotPath;
            _roleService = roleService ?? new DefaultRoleService();
            _embeddingService = embeddingService ?? new HashingEmbeddingService();
            _semanticIndex = semanticIndex ?? new VectorIndex();
        }

        /// <summary>
        /// Registriert ein Objekt, erzeugt dessen Objekt-Embedding, legt den
        /// zugehörigen <see cref="StateNode"/> an und fügt einen Eintrag zum
        /// semantischen Index hinzu.
        /// </summary>
        public bool RegisterObject(MatrixObject matrixObject)
        {
            ArgumentNullException.ThrowIfNull(matrixObject);

            var added = _objects.TryAdd(matrixObject.Id, matrixObject);

            var state = _states.GetOrAdd(matrixObject.Id, id => new StateNode(id));
            foreach (var kvp in matrixObject.Meta)
            {
                state.Meta[kvp.Key] = kvp.Value;
            }

            var embedding = _embeddingService.Encode(matrixObject.BuildEmbeddingSourceText());
            state.Restore(state.Value, state.Timestamp, embedding, state.LastWrittenBy);
            _semanticIndex.Add(matrixObject.Id, embedding);

            return added;
        }

        /// <summary>
        /// Liefert das registrierte Objekt zur übergebenen ID, oder <c>null</c>, falls unbekannt.
        /// </summary>
        public MatrixObject? GetObject(string id)
        {
            return _objects.TryGetValue(id, out var matrixObject) ? matrixObject : null;
        }

        /// <summary>
        /// Liefert den aktuellen Zustand (State) zur übergebenen ID, oder <c>null</c>, falls unbekannt.
        /// </summary>
        public StateNode? GetState(string id)
        {
            return _states.TryGetValue(id, out var state) ? state : null;
        }

        /// <summary>
        /// Setzt den Wert eines States. Validiert zuvor die Schreibrechte des
        /// übergebenen Benutzers, erzeugt ein Payload-Embedding, aktualisiert
        /// den <see cref="StateNode"/>, hängt eine <see cref="HistorySample"/>
        /// an, aktualisiert den semantischen Index und benachrichtigt alle Subscriber.
        /// </summary>
        /// <returns><c>true</c> bei Erfolg, <c>false</c> wenn der Benutzer keine Schreibrechte besitzt.</returns>
        public bool SetState(string id, object? value, string user = "system")
        {
            if (!_roleService.CanWrite(user, id))
            {
                return false;
            }

            var state = _states.GetOrAdd(id, key => new StateNode(key));
            var embeddingSource = $"{id} {value}";
            var embedding = _embeddingService.Encode(embeddingSource);

            state.Update(value, embedding, user);
            _semanticIndex.Add(id, embedding);

            NotifySubscribers(id, state);

            return true;
        }

        /// <summary>
        /// Registriert einen Callback, der bei jeder Änderung des Zustands mit
        /// der übergebenen ID aufgerufen wird.
        /// </summary>
        public void Subscribe(string id, Action<StateNode> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_subscriptionLock)
            {
                if (!_subscriptions.TryGetValue(id, out var callbacks))
                {
                    callbacks = new List<Action<StateNode>>();
                    _subscriptions[id] = callbacks;
                }

                callbacks.Add(callback);
            }
        }

        /// <summary>
        /// Entfernt einen zuvor registrierten Subscription-Callback.
        /// </summary>
        public void Unsubscribe(string id, Action<StateNode> callback)
        {
            lock (_subscriptionLock)
            {
                if (_subscriptions.TryGetValue(id, out var callbacks))
                {
                    callbacks.Remove(callback);
                }
            }
        }

        private void NotifySubscribers(string id, StateNode state)
        {
            List<Action<StateNode>>? callbacks;
            lock (_subscriptionLock)
            {
                if (!_subscriptions.TryGetValue(id, out var existing))
                {
                    return;
                }

                callbacks = existing.ToList();
            }

            foreach (var callback in callbacks)
            {
                try
                {
                    callback(state);
                }
                catch
                {
                    // Subscriber-Fehler dürfen die Matrix nicht destabilisieren.
                }
            }
        }

        /// <summary>
        /// Führt eine semantische Suche über alle registrierten States anhand
        /// eines Freitextes durch (z. B. "Temperatur Wohnzimmer").
        /// </summary>
        public List<StateNode> SemanticQuery(string text, int k = 10)
        {
            var queryEmbedding = _embeddingService.Encode(text);
            var results = _semanticIndex.Search(queryEmbedding, k);

            var stateNodes = new List<StateNode>();
            foreach (var (id, _) in results)
            {
                if (_states.TryGetValue(id, out var state))
                {
                    stateNodes.Add(state);
                }
            }

            return stateNodes;
        }

        /// <summary>
        /// Liefert die History-Samples eines States, optional gefiltert nach
        /// einem Zeitraum. Wird z. B. von einem history.0-kompatiblen Adapter genutzt.
        /// </summary>
        public IEnumerable<HistorySample> GetHistory(string id, DateTime? from = null, DateTime? to = null)
        {
            if (!_states.TryGetValue(id, out var state))
            {
                return Enumerable.Empty<HistorySample>();
            }

            IEnumerable<HistorySample> samples = state.History;

            if (from.HasValue)
            {
                samples = samples.Where(sample => sample.Timestamp >= from.Value);
            }

            if (to.HasValue)
            {
                samples = samples.Where(sample => sample.Timestamp <= to.Value);
            }

            return samples.OrderBy(sample => sample.Timestamp).ToList();
        }

        /// <summary>
        /// Gibt den aktuellen Objektbaum (Objekte + aktuelle State-Werte) als JSON aus.
        /// </summary>
        public string DisplayAsJson()
        {
            var tree = _objects.Values.Select(obj => new
            {
                obj.Id,
                obj.Name,
                obj.Type,
                obj.Meta,
                State = _states.TryGetValue(obj.Id, out var state)
                    ? new { state.Value, state.Timestamp, state.LastWrittenBy }
                    : null
            });

            return JsonSerializer.Serialize(tree, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Lädt die Snapshot-Datei (sofern vorhanden), rekonstruiert States
        /// und History sowie die semantischen Embeddings im Vektorindex.
        /// Wird typischerweise im OnStart-Lifecycle-Hook aufgerufen.
        /// </summary>
        public async Task LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_snapshotPath))
            {
                return;
            }

            await foreach (var line in ReadLinesAsync(_snapshotPath, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                SnapshotEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize<SnapshotEntry>(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (entry is null || string.IsNullOrEmpty(entry.Id))
                {
                    continue;
                }

                var state = _states.GetOrAdd(entry.Id, id => new StateNode(id));
                var sample = new HistorySample(entry.Id, entry.Timestamp, NormalizeValue(entry.Value), entry.Embedding);
                state.AppendHistorySample(sample);
                state.Restore(sample.Value, sample.Timestamp, sample.Embedding, writtenBy: null);

                _semanticIndex.Add(entry.Id, sample.Embedding);

                if (!_objects.ContainsKey(entry.Id) && entry.Name is not null && entry.Type is not null)
                {
                    _objects.TryAdd(entry.Id, new MatrixObject
                    {
                        Id = entry.Id,
                        Name = entry.Name,
                        Type = entry.Type
                    });
                }
            }
        }

        /// <summary>
        /// Persistiert alle History-Samples aller States als JSONL-Datei
        /// (Append-Only-Format, siehe Spezifikation "SnapshotFormat").
        /// Wird typischerweise im OnStop-Lifecycle-Hook sowie periodisch
        /// (<see cref="StartCronSnapshot"/>) aufgerufen.
        /// </summary>
        public async Task WriteSnapshotAsync(CancellationToken cancellationToken = default)
        {
            var directory = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var writer = new StreamWriter(_snapshotPath, append: false);
            foreach (var state in _states.Values)
            {
                var matrixObject = _objects.GetValueOrDefault(state.Id);

                foreach (var sample in state.History)
                {
                    var entry = new SnapshotEntry
                    {
                        Id = sample.Id,
                        Timestamp = sample.Timestamp,
                        Value = sample.Value,
                        Embedding = sample.Embedding,
                        Name = matrixObject?.Name,
                        Type = matrixObject?.Type
                    };

                    var json = JsonSerializer.Serialize(entry);
                    await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
                }
            }
        }

        /// <summary>
        /// Startet einen periodischen Timer, der alle <paramref name="interval"/>
        /// ein Abbild der History-Samples auf die Festplatte schreibt.
        /// </summary>
        public void StartCronSnapshot(TimeSpan interval)
        {
            _cronTimer?.Dispose();
            _cronTimer = new Timer(
                _ => WriteSnapshotAsync().GetAwaiter().GetResult(),
                state: null,
                dueTime: interval,
                period: interval);
        }

        /// <summary>
        /// Stoppt den periodischen Snapshot-Timer.
        /// </summary>
        public void StopCronSnapshot()
        {
            _cronTimer?.Dispose();
            _cronTimer = null;
        }

        public void Dispose()
        {
            StopCronSnapshot();
        }

        private static object? NormalizeValue(object? value)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString(),
                    JsonValueKind.Number => jsonElement.TryGetInt64(out var longValue) ? longValue : jsonElement.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => jsonElement.GetRawText()
                };
            }

            return value;
        }

        private static async IAsyncEnumerable<string> ReadLinesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(path);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                yield return line;
            }
        }
    }
}
