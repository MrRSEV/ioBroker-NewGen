using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ioBroker_NewGen.Core.Bus;
using ioBroker_NewGen.Core.Registry;
using ioBroker_NewGen.Core.Routing;
using RSEV.Utilities.Logging;

namespace ioBroker_NewGen.Core.Bridge
{
    /// <summary>
    /// TCP-Bridge-Server für isolierte Legacy-ioBroker-Adapter (Node.js-Prozesse).
    /// Lauscht auf einem festen Host-Port, führt den Handshake durch (Adapter sendet
    /// Namen, Host vergibt ID im Format "Name.id") und transportiert anschließend
    /// <see cref="BusFrame"/>-Payloads bidirektional zwischen Adapter und Systembus.
    /// </summary>
    public sealed class TcpBridgeServer : IAsyncDisposable
    {
        private readonly int _port;
        private readonly AdapterRegistry _adapterRegistry;
        private readonly SystemMessageBus _systemBus;
        private readonly ILogger _logger;

        private readonly ConcurrentDictionary<string, BridgeConnection> _connectionsByAdapterId = new();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;

        public TcpBridgeServer(int port, AdapterRegistry adapterRegistry, SystemMessageBus systemBus, ILogger logger)
        {
            _port = port;
            _adapterRegistry = adapterRegistry ?? throw new ArgumentNullException(nameof(adapterRegistry));
            _systemBus = systemBus ?? throw new ArgumentNullException(nameof(systemBus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Startet den TCP-Listener und beginnt, eingehende Adapter-Verbindungen anzunehmen.
        /// </summary>
        public Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            _logger.LogInfo($"[TcpBridge] Lauscht auf Port {_port}.");

            _acceptLoopTask = AcceptLoopAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stoppt den Listener und schließt alle aktiven Verbindungen.
        /// </summary>
        public async Task StopAsync()
        {
            _cts?.Cancel();
            _listener?.Stop();

            if (_acceptLoopTask is not null)
            {
                try
                {
                    await _acceptLoopTask;
                }
                catch (OperationCanceledException)
                {
                    // Erwartet beim Herunterfahren.
                }
            }

            foreach (var connection in _connectionsByAdapterId.Values.ToList())
            {
                await connection.DisposeAsync();
            }

            _connectionsByAdapterId.Clear();
            _logger.LogInfo("[TcpBridge] Gestoppt.");
        }

        /// <summary>
        /// Sendet ein Frame an einen konkreten, verbundenen ioBroker-Adapter
        /// (z. B. Befehl vom Systembus Richtung Legacy-Adapter).
        /// </summary>
        public async Task<bool> SendToAdapterAsync(string adapterId, BusFrame frame)
        {
            if (!_connectionsByAdapterId.TryGetValue(adapterId, out var connection))
            {
                _logger.LogWarning($"[TcpBridge] Kein verbundener Adapter mit ID '{adapterId}' gefunden.");
                return false;
            }

            var envelope = new BridgeEnvelope
            {
                Type = BridgeMessageType.Frame,
                AdapterId = frame.AdapterId,
                Address = frame.Address,
                ValueType = frame.ValueType,
                FrameKind = (int)frame.Kind,
                PayloadBase64 = Convert.ToBase64String(frame.Payload.Span),
                Metadata = frame.Metadata.Values,
                Timestamp = frame.Timestamp
            };

            await connection.SendAsync(envelope);
            return true;
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                    _ = HandleConnectionAsync(client, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Erwartet beim Herunterfahren.
            }
            catch (ObjectDisposedException)
            {
                // Listener wurde gestoppt.
            }
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var connection = new BridgeConnection(client);
            string? adapterId = null;

            try
            {
                // Handshake: Erwartet die erste Nachricht als BridgeMessageType.Handshake mit dem Adapternamen.
                var handshake = await connection.ReadAsync(cancellationToken);
                if (handshake is null || handshake.Type != BridgeMessageType.Handshake || string.IsNullOrWhiteSpace(handshake.AdapterId))
                {
                    _logger.LogWarning("[TcpBridge] Ungültiger oder fehlender Handshake – Verbindung wird getrennt.");
                    await connection.DisposeAsync();
                    return;
                }

                var adapterName = handshake.AdapterId;
                var registration = _adapterRegistry.Register(adapterName, AdapterType.IoBroker);
                adapterId = registration.Id;
                registration.TcpEndpoint = client.Client.RemoteEndPoint?.ToString();
                _adapterRegistry.UpdateStatus(adapterId, AdapterStatus.Connected);

                connection.AdapterId = adapterId;
                _connectionsByAdapterId[adapterId] = connection;

                await connection.SendAsync(new BridgeEnvelope
                {
                    Type = BridgeMessageType.HandshakeAck,
                    AdapterId = adapterId
                }, cancellationToken);

                _logger.LogInfo($"[TcpBridge] Adapter '{adapterName}' verbunden, ID vergeben: '{adapterId}'.");

                await ReceiveLoopAsync(connection, adapterId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[TcpBridge] Fehler in Verbindung (Adapter '{adapterId ?? "unbekannt"}'): {ex.Message}");
            }
            finally
            {
                if (adapterId is not null)
                {
                    _connectionsByAdapterId.TryRemove(adapterId, out _);
                    _adapterRegistry.UpdateStatus(adapterId, AdapterStatus.Disconnected);
                    _logger.LogInfo($"[TcpBridge] Adapter '{adapterId}' getrennt.");
                }

                await connection.DisposeAsync();
            }
        }

        private async Task ReceiveLoopAsync(BridgeConnection connection, string adapterId, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var envelope = await connection.ReadAsync(cancellationToken);
                if (envelope is null)
                {
                    break;
                }

                connection.LastSeenAt = DateTime.UtcNow;
                _adapterRegistry.UpdateStatus(adapterId, AdapterStatus.Connected);

                switch (envelope.Type)
                {
                    case BridgeMessageType.Frame:
                        HandleIncomingFrame(adapterId, envelope);
                        break;

                    case BridgeMessageType.Heartbeat:
                        // Lebenszeichen wurde bereits über LastSeenAt/UpdateStatus verarbeitet.
                        break;

                    case BridgeMessageType.Error:
                        _logger.LogWarning($"[TcpBridge] Fehlermeldung von Adapter '{adapterId}': {envelope.ErrorMessage}");
                        break;

                    default:
                        _logger.LogWarning($"[TcpBridge] Unerwarteter Nachrichtentyp '{envelope.Type}' von Adapter '{adapterId}'.");
                        break;
                }
            }
        }

        private void HandleIncomingFrame(string adapterId, BridgeEnvelope envelope)
        {
            if (string.IsNullOrEmpty(envelope.Address))
            {
                _logger.LogWarning($"[TcpBridge] Frame von Adapter '{adapterId}' ohne Adresse empfangen – wird verworfen.");
                return;
            }

            var payload = string.IsNullOrEmpty(envelope.PayloadBase64)
                ? ReadOnlyMemory<byte>.Empty
                : Convert.FromBase64String(envelope.PayloadBase64);

            var metadata = new BusFrameMetadata { Values = envelope.Metadata ?? new() };

            var frame = new BusFrame(
                adapterId,
                envelope.Address,
                payload,
                envelope.ValueType ?? "raw",
                (BusFrameKind)envelope.FrameKind,
                envelope.Timestamp,
                metadata);

            // Externe Payloads werden gemäß Architektur 1:1 auf den Systembus gespiegelt.
            _systemBus.Publish(frame);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _cts?.Dispose();
        }
    }
}
