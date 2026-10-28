using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ioBroker_NewGen.Core.Adapters;
using ioBroker_NewGen.Core.Adapters.Protocol;
using ioBroker_NewGen.Core.Bus;
using RSEV.Utilities.Configuration;

namespace ioBroker_NewGen.Adapters.Shelly
{
    public sealed class ShellyAdapterOptions
    {
        public Uri DeviceUri { get; init; } = new("http://127.0.0.1");

        public int SwitchId { get; init; }

        public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    }

    public sealed class ShellyAdapter : HttpPollingAdapterBase
    {
        private ShellyAdapterOptions _options;

        public ShellyAdapter()
            : this(new ShellyAdapterOptions())
        {
        }

        public ShellyAdapter(ShellyAdapterOptions options, HttpClient? httpClient = null)
            : base(httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override string Name => "shelly";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _options = new ShellyAdapterOptions
            {
                DeviceUri = new Uri(AdapterConfigReader.Read(Config, "shelly.deviceUri", _options.DeviceUri.ToString())),
                SwitchId = AdapterConfigReader.Read(Config, "shelly.switchId", _options.SwitchId),
                PollInterval = TimeSpan.FromMilliseconds(Math.Max(
                    1000,
                    AdapterConfigReader.Read(Config, "shelly.pollIntervalMs", (int)_options.PollInterval.TotalMilliseconds)))
            };
            PollInterval = _options.PollInterval;
            return Task.CompletedTask;
        }

        protected override void OnReceivePayload(BusFrame frame)
        {
            _ = SendCommandAsync(frame, CancellationToken.None);
        }

        protected override async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            var response = await HttpClient.GetAsync(BuildRpcUri("Switch.GetStatus", $"id={_options.SwitchId}"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            if (document.RootElement.TryGetProperty("output", out var output))
            {
                PublishState(
                    $"shelly.switch.{_options.SwitchId}.output",
                    output.Clone(),
                    "json",
                    new BusFrameMetadata { Values = { ["component"] = $"switch:{_options.SwitchId}" } });
            }
        }

        private async Task SendCommandAsync(BusFrame frame, CancellationToken cancellationToken)
        {
            if (!frame.Address.EndsWith(".output", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var body = JsonSerializer.Serialize(new
            {
                id = _options.SwitchId,
                on = ParseJsonValue(frame.Payload)
            });
            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using (content)
            {
                var response = await HttpClient.PostAsync(BuildRpcUri("Switch.Set", null), content, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
        }

        private Uri BuildRpcUri(string method, string? query)
        {
            var baseUri = _options.DeviceUri.ToString().TrimEnd('/');
            return new Uri($"{baseUri}/rpc/{method}{(string.IsNullOrWhiteSpace(query) ? string.Empty : "?" + query)}");
        }

        private static object ParseJsonValue(ReadOnlyMemory<byte> payload)
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
            return document.RootElement.Clone();
        }
    }
}
