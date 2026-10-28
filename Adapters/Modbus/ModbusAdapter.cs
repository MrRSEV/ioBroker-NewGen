using System.Text;
using ioBroker_NewGen.Core.Adapters;
using ioBroker_NewGen.Core.Bus;
using ioBroker_NewGen.Core.Adapters.Protocol;

namespace ioBroker_NewGen.Adapters.Modbus
{
    /// <summary>
    /// Modbus-TCP-Adapter für Holding Register. Jeder Registerwert wird als
    /// eigener State gespiegelt; Commands auf diesen States schreiben Funktion 06.
    /// </summary>
    public sealed class ModbusAdapter : DeviceAdapterBase
    {
        private ModbusTcpClient? _client;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private string _host = "127.0.0.1";
        private int _port = 502;
        private byte _unitId = 1;
        private ushort _startAddress;
        private ushort _registerCount = 1;
        private string _statePrefix = "modbus";
        private TimeSpan _pollInterval = TimeSpan.FromSeconds(10);

        public override string Name => "modbus";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _host = ReadConfig("modbus.host", "127.0.0.1");
            _port = ReadConfig("modbus.port", 502);
            _unitId = ReadConfig("modbus.unitId", (byte)1);
            _startAddress = ReadConfig("modbus.startAddress", (ushort)0);
            _registerCount = (ushort)Math.Clamp(ReadConfig("modbus.registerCount", 1), 1, 125);
            _statePrefix = ReadConfig("modbus.statePrefix", "modbus");
            _pollInterval = TimeSpan.FromMilliseconds(Math.Max(1000, ReadConfig("modbus.pollIntervalMs", 10000)));
            return Task.CompletedTask;
        }

        protected override async Task OnStartAsync(CancellationToken cancellationToken)
        {
            _client = new ModbusTcpClient(_host, _port);
            await _client.ConnectAsync(cancellationToken);
            await PollAsync(cancellationToken);
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollTask = PollLoopAsync(_pollCts.Token);
        }

        protected override async Task OnStopAsync(CancellationToken cancellationToken)
        {
            _pollCts?.Cancel();
            if (_pollTask is not null)
            {
                try
                {
                    await _pollTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _pollTask = null;
            _pollCts?.Dispose();
            _pollCts = null;
            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }

        protected override void OnReceivePayload(BusFrame frame)
        {
            if (_client is null || !frame.Address.StartsWith(_statePrefix + ".register.", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var suffix = frame.Address[(_statePrefix.Length + ".register.".Length)..];
            if (!ushort.TryParse(suffix, out var offset) || !ushort.TryParse(Encoding.UTF8.GetString(frame.Payload.Span), out var value))
            {
                return;
            }

            _ = _client.WriteSingleRegisterAsync(
                _unitId,
                (ushort)(_startAddress + offset),
                value);
        }

        private async Task PollLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(_pollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await PollAsync(cancellationToken);
            }
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            if (_client is null)
            {
                return;
            }

            var values = await _client.ReadHoldingRegistersAsync(_unitId, _startAddress, _registerCount, cancellationToken);
            for (var index = 0; index < values.Length; index++)
            {
                PublishState($"{_statePrefix}.register.{index}", (int)values[index], "int");
            }
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            try
            {
                return Config.Get<T>(key);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
