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

namespace ioBroker_NewGen.Adapters.Hue
{
    public sealed class PhilipsHueAdapterOptions
    {
        public Uri BridgeUri { get; init; } = new("http://127.0.0.1");

        public string ApiKey { get; init; } = string.Empty;

        public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);
    }

    public sealed class PhilipsHueAdapter : HttpPollingAdapterBase
    {
        private PhilipsHueAdapterOptions _options;

        public PhilipsHueAdapter()
            : this(new PhilipsHueAdapterOptions())
        {
        }

        public PhilipsHueAdapter(PhilipsHueAdapterOptions options, HttpClient? httpClient = null)
            : base(httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override string Name => "hue";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _options = new PhilipsHueAdapterOptions
            {
                BridgeUri = new Uri(AdapterConfigReader.Read(Config, "hue.bridgeUri", _options.BridgeUri.ToString())),
                ApiKey = AdapterConfigReader.Read(Config, "hue.apiKey", _options.ApiKey),
                PollInterval = TimeSpan.FromMilliseconds(Math.Max(
                    1000,
                    AdapterConfigReader.Read(Config, "hue.pollIntervalMs", (int)_options.PollInterval.TotalMilliseconds)))
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
            var response = await HttpClient.GetAsync(BuildUri("lights"), cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            foreach (var light in document.RootElement.EnumerateObject())
            {
                var id = light.Name;
                if (!light.Value.TryGetProperty("state", out var state))
                {
                    continue;
                }

                if (state.TryGetProperty("on", out var on))
                {
                    PublishState($"hue.{id}.on", on.Clone(), "json", Metadata(id));
                }

                if (state.TryGetProperty("bri", out var brightness))
                {
                    PublishState($"hue.{id}.brightness", brightness.Clone(), "json", Metadata(id));
                }
            }
        }

        private async Task SendCommandAsync(BusFrame frame, CancellationToken cancellationToken)
        {
            var parts = frame.Address.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].Equals("hue", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var body = new System.Collections.Generic.Dictionary<string, object?>();
            if (parts[2].Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                body["on"] = ParseJsonValue(frame.Payload);
            }
            else if (parts[2].Equals("brightness", StringComparison.OrdinalIgnoreCase))
            {
                body["bri"] = ParseJsonValue(frame.Payload);
            }
            else
            {
                return;
            }

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using (content)
            {
                var response = await HttpClient.PutAsync(BuildUri($"lights/{parts[1]}/state"), content, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
        }

        private Uri BuildUri(string suffix)
        {
            var baseUri = _options.BridgeUri.ToString().TrimEnd('/');
            return new Uri($"{baseUri}/api/{Uri.EscapeDataString(_options.ApiKey)}/{suffix.TrimStart('/')}");
        }

        private static object ParseJsonValue(ReadOnlyMemory<byte> payload)
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(payload.Span));
            return document.RootElement.Clone();
        }

        private static BusFrameMetadata Metadata(string id)
        {
            return new BusFrameMetadata { Values = { ["device"] = id } };
        }
    }
}
