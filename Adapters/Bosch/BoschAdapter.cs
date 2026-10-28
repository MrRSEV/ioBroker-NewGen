using System.Net.Http.Headers;
using System.Text.Json;
using ioBroker_NewGen.Core.Adapters.Protocol;

namespace ioBroker_NewGen.Adapters.Bosch
{
    /// <summary>
    /// Bosch-Smart-Home-Controller-Adapter. Der Controller stellt Items als
    /// REST-Ressource bereit; jedes Item wird als JSON-State gespiegelt.
    /// </summary>
    public sealed class BoschAdapter : HttpPollingAdapterBase
    {
        private Uri _baseUri = new("http://127.0.0.1");
        private string _token = string.Empty;
        private string _itemsPath = "/rest/items";
        private string _statePrefix = "bosch";

        public BoschAdapter()
            : this(null)
        {
        }

        public BoschAdapter(HttpClient? httpClient)
            : base(httpClient)
        {
            PollInterval = TimeSpan.FromSeconds(30);
        }

        public override string Name => "bosch";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _baseUri = new Uri(ReadConfig("bosch.baseUri", "http://127.0.0.1"));
            _token = ReadConfig("bosch.token", string.Empty);
            _itemsPath = ReadConfig("bosch.itemsPath", "/rest/items");
            _statePrefix = ReadConfig("bosch.statePrefix", "bosch");
            PollInterval = TimeSpan.FromMilliseconds(Math.Max(1000, ReadConfig("bosch.pollIntervalMs", 30000)));
            return Task.CompletedTask;
        }

        protected override async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, _itemsPath));
            if (!string.IsNullOrWhiteSpace(_token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var items = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                : document.RootElement.TryGetProperty("items", out var nested) && nested.ValueKind == JsonValueKind.Array
                    ? nested.EnumerateArray()
                    : Enumerable.Empty<JsonElement>();

            foreach (var item in items)
            {
                var id = ReadString(item, "id") ?? ReadString(item, "name");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var safeId = id.Replace('/', '.').Replace(' ', '_');
                PublishState($"{_statePrefix}.{safeId}", item.Clone(), "json");
            }
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            return AdapterConfigReader.Read(Config, key, fallback);
        }

        private static string? ReadString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }
}
