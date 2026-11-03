using System;
using System.Text.Json;
using System.Threading.Tasks;
using ioBroker_NewGen.Core.Adapters.Protocol;
using ioBroker_NewGen.Core.Bus;

namespace ioBroker_NewGen.Adapters.Zigbee
{
    public sealed class Zigbee2MqttAdapterOptions
    {
        public MqttClientOptions Client { get; init; } = new();

        public string BaseTopic { get; init; } = "zigbee2mqtt";
    }

    public sealed class Zigbee2MqttAdapter : MqttAdapterBase
    {
        public Zigbee2MqttAdapter()
            : this(new Zigbee2MqttAdapterOptions())
        {
        }

        public Zigbee2MqttAdapter(
            Zigbee2MqttAdapterOptions options,
            Func<MqttClientOptions, IMqttClient>? clientFactory = null)
            : base(new MqttAdapterOptions
            {
                Client = options?.Client ?? throw new ArgumentNullException(nameof(options)),
                AddressPrefix = options.BaseTopic
            }, clientFactory)
        {
        }

        public override string Name => "zigbee";

        protected override string AddressPrefixConfigurationKey => "baseTopic";

        protected override Task HandleIncomingMessageAsync(string topic, ReadOnlyMemory<byte> payload)
        {
            var prefix = Options.AddressPrefix.Trim('/');
            var normalizedTopic = topic.Trim('/');
            if (!normalizedTopic.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            var remainder = normalizedTopic[(prefix.Length + 1)..];
            var device = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            if (string.IsNullOrWhiteSpace(device))
            {
                return Task.CompletedTask;
            }

            try
            {
                using var document = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(payload.Span));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    PublishState(
                        $"zigbee.{device}.{property.Name}",
                        property.Value.Clone(),
                        "json",
                        new BusFrameMetadata { Values = { ["topic"] = topic, ["device"] = device } });
                }
            }
            catch (JsonException)
            {
                PublishState(
                    $"zigbee.{device}.raw",
                    payload.ToArray(),
                    "raw",
                    new BusFrameMetadata { Values = { ["topic"] = topic, ["device"] = device } });
            }

            return Task.CompletedTask;
        }
    }
}
