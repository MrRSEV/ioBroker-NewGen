using System.Xml.Linq;
using ioBroker_NewGen.Core.Adapters.Protocol;

namespace ioBroker_NewGen.Adapters.Homematic
{
    /// <summary>
    /// Homematic-Adapter für die XML-API der CCU. Der Endpunkt ist bewusst
    /// konfigurierbar, weil die XML-API als CCU-Add-on installiert wird.
    /// </summary>
    public sealed class HomematicAdapter : HttpPollingAdapterBase
    {
        private Uri _baseUri = new("http://127.0.0.1");
        private string _statePath = "/addons/xmlapi/state.cgi";
        private string _statePrefix = "homematic";

        public HomematicAdapter()
            : this(null)
        {
        }

        public HomematicAdapter(HttpClient? httpClient)
            : base(httpClient)
        {
            PollInterval = TimeSpan.FromSeconds(10);
        }

        public override string Name => "homematic";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _baseUri = new Uri(ReadConfig("homematic.baseUri", "http://127.0.0.1"));
            _statePath = ReadConfig("homematic.statePath", "/addons/xmlapi/state.cgi");
            _statePrefix = ReadConfig("homematic.statePrefix", "homematic");
            PollInterval = TimeSpan.FromMilliseconds(Math.Max(1000, ReadConfig("homematic.pollIntervalMs", 10000)));
            return Task.CompletedTask;
        }

        protected override async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            using var response = await HttpClient.GetAsync(new Uri(_baseUri, _statePath), cancellationToken);
            response.EnsureSuccessStatusCode();
            var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach (var datapoint in document.Descendants().Where(element => element.Name.LocalName == "datapoint"))
            {
                var id = (string?)datapoint.Attribute("ise_id") ?? (string?)datapoint.Attribute("id");
                var value = (string?)datapoint.Attribute("value");
                if (string.IsNullOrWhiteSpace(id) || value is null)
                {
                    continue;
                }

                var address = $"{_statePrefix}.{id}";
                if (bool.TryParse(value, out var boolean))
                {
                    PublishState(address, boolean, "bool");
                }
                else if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    PublishState(address, number, "double");
                }
                else
                {
                    PublishState(address, value, "string");
                }
            }
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            return AdapterConfigReader.Read(Config, key, fallback);
        }
    }
}
