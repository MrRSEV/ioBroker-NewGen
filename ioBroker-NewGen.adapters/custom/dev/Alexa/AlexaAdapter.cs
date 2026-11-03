using System.Net.Http.Headers;
using System.Text.Json;
using ioBroker_NewGen.Core.Adapters.Protocol;

namespace ioBroker_NewGen.Adapters.Alexa
{
    /// <summary>
    /// Alexa-Adapter über einen konfigurierbaren REST-Endpunkt. Der Endpunkt
    /// kann ein eigener Alexa-Bridge-Dienst oder ein zertifizierter Cloud-Gateway
    /// sein; Zugangsdaten werden nicht im Adaptercode fest verdrahtet.
    /// </summary>
    public sealed class AlexaAdapter : HttpPollingAdapterBase
    {
        private Uri _endpoint = new("http://127.0.0.1");
        private string _token = string.Empty;
        private long _tokenExpiresAt;
        private bool _hasRefreshToken;
        private string _statePrefix = "alexa";

        public AlexaAdapter()
            : this(null)
        {
        }

        public AlexaAdapter(HttpClient? httpClient)
            : base(httpClient)
        {
            PollInterval = TimeSpan.FromSeconds(30);
        }

        public override string Name => "alexa";

        public override ioBroker_NewGen.Core.Adapters.Configuration.AdapterConfigSchema? ConfigSchema => BuildSchema(
            TextField("endpoint", "Endpoint", ""),
            TextField("clientId", "Client-ID (Login with Amazon)", ""),
            PasswordField("clientSecret", "Client-Secret"),
            TextField("scope", "OAuth-Scope", "profile"),
            PasswordField("token", "Access-Token"),
            PasswordField("refreshToken", "Refresh-Token"),
            NumberField("tokenExpiresAt", "Token gültig bis (Unix-Sekunden)", 0),
            TextField("statePrefix", "State-Präfix", "alexa"),
            NumberField("pollIntervalMs", "Abfrageintervall (ms)", 30000))
            .WithOAuth(new ioBroker_NewGen.Core.Adapters.Configuration.OAuthProviderSpec(
                OAuthProviderId, "Amazon", "https://www.amazon.com/ap/oa", "https://api.amazon.com/auth/o2/token")
            {
                Scope = "profile",
                ScopeKey = "scope",
                ClientSecretKey = "clientSecret",
                RefreshTokenKey = "refreshToken",
                ExpiresAtKey = "tokenExpiresAt",
                UsePkce = true
            });

        private const string OAuthProviderId = "amazon";

        protected override Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            _endpoint = new Uri(ReadConfig("alexa.endpoint", "http://127.0.0.1"));
            _token = ReadConfig("alexa.token", string.Empty);
            _tokenExpiresAt = ReadConfig("alexa.tokenExpiresAt", 0L);
            _hasRefreshToken = !string.IsNullOrEmpty(ReadConfig("alexa.refreshToken", string.Empty));
            _statePrefix = ResolvePrefix(ReadConfig("alexa.statePrefix", "alexa"), "alexa");
            PollInterval = TimeSpan.FromMilliseconds(Math.Max(1000, ReadConfig("alexa.pollIntervalMs", 30000)));
            return Task.CompletedTask;
        }

        protected override async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            if (_hasRefreshToken && _tokenExpiresAt > 0
                && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= _tokenExpiresAt - 60)
            {
                var tokens = await Context.AdapterRegistry.OAuth.RefreshAsync(Id, Name, OAuthProviderId, cancellationToken);
                _token = tokens.AccessToken;
                _tokenExpiresAt = tokens.ExpiresAt ?? 0;
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            if (!string.IsNullOrWhiteSpace(_token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var devices = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                : document.RootElement.TryGetProperty("devices", out var nested) && nested.ValueKind == JsonValueKind.Array
                    ? nested.EnumerateArray()
                    : Enumerable.Empty<JsonElement>();

            foreach (var device in devices)
            {
                var id = device.TryGetProperty("id", out var idElement) ? idElement.ToString() : null;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                PublishState($"{_statePrefix}.{id}", device.Clone(), "json");
            }
        }

        private T ReadConfig<T>(string key, T fallback)
        {
            return AdapterConfigReader.Read(Config, key, fallback);
        }
    }
}
