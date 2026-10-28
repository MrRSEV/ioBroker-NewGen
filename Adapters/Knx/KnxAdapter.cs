using System.Net;
using System.Net.Sockets;
using ioBroker_NewGen.Core.Adapters;
using ioBroker_NewGen.Core.Adapters.Protocol;
using ioBroker_NewGen.Core.Bus;

namespace ioBroker_NewGen.Adapters.Knx
{
    /// <summary>
    /// KNXnet/IP-Adapter für einen konfigurierten Gruppenzugriff. Der Adapter
    /// verwendet den KNXnet/IP-Tunneling-Datagrammrahmen und spiegelt empfangene
    /// cEMI-Payloads als Raw-State; DPT-spezifische Dekodierung kann im Adapter
    /// anhand der State-Definition ergänzt werden.
    /// </summary>
    public sealed class KnxAdapter : DeviceAdapterBase
    {
        private UdpClient? _udp;
        private CancellationTokenSource? _receiveCts;
        private Task? _receiveTask;
        private string _gateway = "127.0.0.1";
        private int _port = 3671;
        private ushort _groupAddress;
        private string _statePrefix = "knx";

        public override string Name => "knx";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _gateway = ReadConfig("knx.gateway", "127.0.0.1");
            _port = ReadConfig("knx.port", 3671);
            _groupAddress = ParseGroupAddress(ReadConfig("knx.groupAddress", "0/0/1"));
            _statePrefix = ReadConfig("knx.statePrefix", "knx");
            return Task.CompletedTask;
        }

        protected override async Task OnStartAsync(CancellationToken cancellationToken)
        {
            _udp = new UdpClient(0);
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
            await SendAsync(BuildGroupReadRequest(_groupAddress), cancellationToken);
        }

        protected override async Task OnStopAsync(CancellationToken cancellationToken)
        {
            _receiveCts?.Cancel();
            _udp?.Dispose();
            if (_receiveTask is not null)
            {
                try
                {
                    await _receiveTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            _receiveTask = null;
            _receiveCts?.Dispose();
            _receiveCts = null;
            _udp = null;
        }

        protected override void OnReceivePayload(BusFrame frame)
        {
            if (_udp is null)
            {
                return;
            }

            _ = SendAsync(BuildGroupWriteRequest(_groupAddress, frame.Payload.ToArray()), CancellationToken.None);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            if (_udp is null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udp.ReceiveAsync(cancellationToken);
                var payload = result.Buffer;
                PublishState(
                    $"{_statePrefix}.{FormatGroupAddress(_groupAddress)}.raw",
                    payload,
                    "raw",
                    new BusFrameMetadata { Values = { ["gateway"] = result.RemoteEndPoint.ToString() } });
            }
        }

        private async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
        {
            if (_udp is null)
            {
                return;
            }

            await _udp.SendAsync(payload, new IPEndPoint(await ResolveAddressAsync(_gateway), _port), cancellationToken);
        }

        private static async Task<IPAddress> ResolveAddressAsync(string host)
        {
            if (IPAddress.TryParse(host, out var address))
            {
                return address;
            }

            return (await Dns.GetHostAddressesAsync(host)).First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        }

        private static byte[] BuildGroupReadRequest(ushort groupAddress)
        {
            return BuildTunnelPacket(BuildCemi(groupAddress, Array.Empty<byte>(), read: true));
        }

        private static byte[] BuildGroupWriteRequest(ushort groupAddress, byte[] payload)
        {
            return BuildTunnelPacket(BuildCemi(groupAddress, payload, read: false));
        }

        private static byte[] BuildTunnelPacket(byte[] cemi)
        {
            var length = 10 + cemi.Length;
            var packet = new byte[length];
            packet[0] = 0x06;
            packet[1] = 0x10;
            packet[2] = 0x04;
            packet[3] = 0x20;
            packet[4] = (byte)(length >> 8);
            packet[5] = (byte)length;
            packet[6] = 0;
            packet[7] = 0;
            packet[8] = 0;
            packet[9] = 0;
            Buffer.BlockCopy(cemi, 0, packet, 10, cemi.Length);
            return packet;
        }

        private static byte[] BuildCemi(ushort groupAddress, byte[] payload, bool read)
        {
            var cemi = new byte[10 + payload.Length];
            cemi[0] = 0x11;
            cemi[1] = 0x00;
            cemi[2] = 0xBC;
            cemi[3] = 0xE0;
            cemi[4] = 0x00;
            cemi[5] = 0x00;
            cemi[6] = (byte)(groupAddress >> 8);
            cemi[7] = (byte)groupAddress;
            cemi[8] = (byte)(payload.Length + 1);
            cemi[9] = (byte)(read ? 0x00 : 0x80);
            Buffer.BlockCopy(payload, 0, cemi, 10, payload.Length);
            return cemi;
        }

        private static ushort ParseGroupAddress(string value)
        {
            var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !byte.TryParse(parts[0], out var main) || !byte.TryParse(parts[1], out var middle) || !byte.TryParse(parts[2], out var sub) || main > 31 || middle > 7 || sub > 255)
            {
                throw new FormatException($"Ungültige KNX-Gruppenadresse: '{value}'. Erwartet wird main/middle/sub.");
            }

            return (ushort)((main << 11) | (middle << 8) | sub);
        }

        private static string FormatGroupAddress(ushort value)
        {
            return $"{value >> 11}/{(value >> 8) & 0x07}/{value & 0xFF}";
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            return AdapterConfigReader.Read(Config, key, fallback);
        }
    }
}
