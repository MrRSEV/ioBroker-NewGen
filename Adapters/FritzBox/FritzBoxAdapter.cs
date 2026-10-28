using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using ioBroker_NewGen.Core.Adapters.Protocol;

namespace ioBroker_NewGen.Adapters.FritzBox
{
    /// <summary>
    /// FRITZ!Box-Adapter für den TR-064-DeviceInfo-Service. Die Endpunkte und
    /// Zugangsdaten bleiben konfigurierbar, damit auch FritzOS-Varianten mit
    /// abweichendem Servicepfad unterstützt werden können.
    /// </summary>
    public sealed class FritzBoxAdapter : HttpPollingAdapterBase
    {
        private Uri _deviceUri = new("http://127.0.0.1");
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _servicePath = "/upnp/control/deviceinfo";
        private string _statePrefix = "fritzbox";

        public FritzBoxAdapter()
            : this(null)
        {
        }

        public FritzBoxAdapter(HttpClient? httpClient)
            : base(httpClient)
        {
            PollInterval = TimeSpan.FromSeconds(30);
        }

        public override string Name => "fritzbox";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _deviceUri = new Uri(ReadConfig("fritzbox.deviceUri", "http://127.0.0.1"));
            _username = ReadConfig("fritzbox.username", string.Empty);
            _password = ReadConfig("fritzbox.password", string.Empty);
            _servicePath = ReadConfig("fritzbox.servicePath", "/upnp/control/deviceinfo");
            _statePrefix = ReadConfig("fritzbox.statePrefix", "fritzbox");
            PollInterval = TimeSpan.FromMilliseconds(Math.Max(1000, ReadConfig("fritzbox.pollIntervalMs", 30000)));
            return Task.CompletedTask;
        }

        protected override async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            const string serviceType = "urn:dslforum-org:service:DeviceInfo:1";
            const string action = "GetInfo";
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_deviceUri, _servicePath));
            request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{serviceType}#{action}\"");
            if (!string.IsNullOrWhiteSpace(_username))
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            request.Content = new StringContent(
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:{action} xmlns:u=\"{serviceType}\" /></s:Body></s:Envelope>",
                Encoding.UTF8,
                "text/xml");
            using (request)
            using (var response = await HttpClient.SendAsync(request, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                var document = XDocument.Parse(responseText.TrimStart('\uFEFF'));
                PublishElement(document, "NewSerialNumber", "serialNumber");
                PublishElement(document, "NewSoftwareVersion", "softwareVersion");
                PublishElement(document, "NewHardwareVersion", "hardwareVersion");
                PublishElement(document, "NewUpTime", "uptime", numeric: true);
            }
        }

        private void PublishElement(XDocument document, string xmlName, string stateName, bool numeric = false)
        {
            var value = document.Descendants().FirstOrDefault(element => element.Name.LocalName == xmlName)?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (numeric && long.TryParse(value, out var number))
            {
                PublishState($"{_statePrefix}.{stateName}", number, "long");
            }
            else
            {
                PublishState($"{_statePrefix}.{stateName}", value, "string");
            }
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            return AdapterConfigReader.Read(Config, key, fallback);
        }
    }
}
