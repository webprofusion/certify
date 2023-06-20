using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.AcmeDns
{

#pragma warning disable IDE1006 // Naming Styles
    internal class AcmeDnsRegistration
    {

        public List<string> allowfrom { get; set; }
        public string fulldomain { get; set; }
        public string subdomain { get; set; }
        public string password { get; set; }
        public string username { get; set; }
        public string subject { get; set; }
    }

    internal class AcmeDnsUpdate
    {
        public string txt { get; set; }
    }

#pragma warning restore IDE1006 // Naming Styles

    public class DnsProviderAcmeDnsProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderAcmeDns : IDnsProvider
    {
        private Dictionary<string, string> _credentials;

        private ILog _log;

        private int? _customPropagationDelay = null;

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition
        {
            get
            {
                return new ChallengeProviderDefinition
                {
                    Id = "DNS01.API.AcmeDns",
                    Title = "acme-dns DNS API",
                    Description = "Validates via an acme-dns server using CNAME redirection to an alternative DNS service dedicated to ACME challenge responses.",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns/providers/acme-dns",
                    PropagationDelaySeconds = 5,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="api",Name="API Url", IsRequired=true, IsCredential=false, IsPassword=false, Value="https://auth.acme-dns.io", Description="Self hosted API is recommended: https://github.com/joohoi/acme-dns" },
                        new ProviderParameter{ Key="allowfrom",Name="Optional Allow From IPs", IsCredential=false, IsRequired=false, IsPassword=false,  Description="e.g.  192.168.100.1/24; 1.2.3.4/32; 2002:c0a8:2a00::0/40" },
                        new ProviderParameter{ Key="credentials_json",Name="Optional credentials JSON", IsCredential=false, IsRequired=false, IsPassword=false, Type=OptionType.MultiLineText,  Description="(optional) Only used if already registered or service supports pre-registering." }
                    },
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.AcmeDns",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        private HttpClient _client;

        private Dictionary<string, string> _parameters = new Dictionary<string, string>();

        private JsonSerializerSettings _serializerSettings;

        private string _settingsPath { get; set; }
        private Uri _apiBaseUri { get; set; }
        public DnsProviderAcmeDns()
        {
            _settingsPath = EnvironmentUtil.GetAppDataFolder();

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "Certify/DnsProviderAcmeDns");

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };

        }

        private string _settingsStoreName { get; set; } = "acmedns";

        /// <summary>
        /// Checks for and loads an existing registration settings file and also returns the computed settings path used.
        /// </summary>
        /// <param name="storeName"></param>
        /// <param name="settingsPath"></param>
        /// <param name="domainId"></param>
        /// <param name="apiPrefix"></param>
        /// <param name="ensureSuffix"></param>
        /// <returns></returns>
        private (AcmeDnsRegistration registration, bool isNewRegistration, string fullSettingsPath) EnsureExistingRegistration(string storeName, string settingsPath, string domainId, string apiPrefix, string ensureSuffix = null)
        {
            var registrationSettingsPath = Path.Combine(settingsPath, storeName);

            if (!System.IO.Directory.Exists(registrationSettingsPath))
            {
                System.IO.Directory.CreateDirectory(registrationSettingsPath);
            }

            var domainConfigFile = domainId.Replace("*.", "") + ".json";

            var filenameRegex = new Regex(
                $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
                RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant
                );

            domainConfigFile = filenameRegex.Replace(domainConfigFile, "_");

            registrationSettingsPath = Path.Combine(registrationSettingsPath, apiPrefix + "_" + domainConfigFile);

            if (System.IO.File.Exists(registrationSettingsPath))
            {
                // registration exists
                var reg = JsonConvert.DeserializeObject<AcmeDnsRegistration>(System.IO.File.ReadAllText(registrationSettingsPath));

                if (ensureSuffix == null || reg.fulldomain.EndsWith(ensureSuffix))
                {
                    // is an existing registration
                    return (registration: reg, isNewRegistration: false, fullSettingsPath: registrationSettingsPath);
                }
                else
                {
                    // registration is not a valid certify dns registration, must be annother acme-dns service}
                    _log?.Warning("Existing acme-dns registration found, new registration required for Certify DNS");
                }
            }

            // registration config not matched
            return (registration: null, isNewRegistration: true, fullSettingsPath: registrationSettingsPath);
        }

        private async Task<(AcmeDnsRegistration registration, bool isNewRegistration)> Register(string settingsPath, string domainId)
        {

            var apiPrefix = "";

            if (_parameters.TryGetValue("api", out var apiBase))
            {
                _apiBaseUri = new System.Uri(apiBase);

                if (!_apiBaseUri.ToString().EndsWith("/"))
                {
                    _apiBaseUri = new Uri($"{_apiBaseUri}/");
                }

                _client.BaseAddress = _apiBaseUri;

                // we prefix the settings file with the encoded API url as these settings are 
                // only useful on the target API, changing the api should change all settings
                apiPrefix = ToUrlSafeBase64String(_client.BaseAddress.Host);
            }

            // optionally load and use an existing registration from provided json
            if (_parameters.TryGetValue("credentials_json", out var credentialsJson))
            {
                // use an existing registration credentials file
                try
                {
                    var reg = JsonConvert.DeserializeObject<AcmeDnsRegistration>(credentialsJson);

                    if (!string.IsNullOrEmpty(reg.fulldomain))
                    {
                        return (registration: reg, isNewRegistration: false);
                    }
                    else
                    {
                        _log.Error("Credentials JSON does not appear to be valid.");
                    }
                }
                catch
                {
                    _log.Error("Failed to parse credentials JSON");
                }
            }

            _settingsStoreName = "acmedns";
            var registrationCheck = EnsureExistingRegistration(_settingsStoreName, settingsPath, domainId, apiPrefix);

            // return existing if any
            if (registrationCheck.registration != null)
            {
                return (registration: registrationCheck.registration, isNewRegistration: registrationCheck.isNewRegistration);
            }

            var registration = new AcmeDns.AcmeDnsRegistration();

            if (_parameters.TryGetValue("allowfrom", out var allowFrom))
            {
                var rules = allowFrom.Split(';');
                registration.allowfrom = new List<string>();
                foreach (var r in rules)
                {
                    registration.allowfrom.Add(r.Trim().ToLower());
                }
            }

            var json = JsonConvert.SerializeObject(registration, _serializerSettings);

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUri}register");

            if (_credentials?.ContainsKey("user") == true && _credentials?.ContainsKey("key") == true)
            {
                var basicCredentials = "Basic " + ToUrlSafeBase64String(_credentials["user"] + ":" + _credentials["key"]);
                req.Headers.Add("Authorization", basicCredentials);
            }

            req.Content = new StringContent(json);

            var response = await _client.SendAsync(req);

            if (response.IsSuccessStatusCode)
            {
                // got new registration
                var responseJson = await response.Content.ReadAsStringAsync();
                registration = JsonConvert.DeserializeObject<AcmeDnsRegistration>(responseJson);

                // save these settings for later
                System.IO.File.WriteAllText(registrationCheck.fullSettingsPath, JsonConvert.SerializeObject(registration));

                _log?.Information("API registration completed");

                // is a new registration
                return (registration, true);
            }
            else
            {
                // failed to register
                _log?.Information("API registration failed");

                return (null, false);
            }
        }

        public async Task<ActionResult> Test()
        {
            return await Task.FromResult(new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." });
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // create or load registration settings
            var (registration, isNewRegistration) = await Register(_settingsPath, request.TargetDomainName);

            if (registration == null)
            {
                return new ActionResult { IsSuccess = false, Message = $"Failed to register with acme-dns. Check API Url and required credentials (if any)." };
            }

            if (isNewRegistration)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"[Action Required] To complete setup, add a CNAME record in your DNS:\r\n\t{request.RecordName}\r\nwith the value:\r\n\t{registration.fulldomain}",
                    Result = new { Name = request.RecordName, Value = registration.fulldomain }
                };
            }

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUri}update");
            req.Headers.Add("X-Api-User", registration.username);
            req.Headers.Add("X-Api-Key", registration.password);

            var update = new
            {
                subdomain = registration.subdomain,
                txt = request.RecordValue
            };

            var json = JsonConvert.SerializeObject(update, _serializerSettings);

            req.Content = new StringContent(json);

            var result = await _client.SendAsync(req);

            try
            {
                if (result.IsSuccessStatusCode)
                {
                    var responseJson = await result.Content.ReadAsStringAsync();
                    var updateResult = JsonConvert.DeserializeObject<AcmeDnsUpdate>(responseJson);

                    return new ActionResult { IsSuccess = true, Message = $"Updated: {request.RecordName} :: {registration.fulldomain}" };
                }
                else
                {
                    return new ActionResult { IsSuccess = false, Message = $"Update failed: Ensure the {request.RecordName} CNAME points to {registration.fulldomain}" };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = $"Update failed: {exp.Message}" };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            return await Task.FromResult(
                new ActionResult { IsSuccess = true, Message = $"Dns Record Delete skipped (not applicable): {request.RecordName}" }
                );
        }

        public async Task<List<DnsZone>> GetZones()
        {
            var results = new List<DnsZone>();
            return await Task.FromResult(results);
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _credentials = credentials;
            _log = log;
            _parameters = parameters;

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return await Task.FromResult(true);
        }

        public static string ToUrlSafeBase64String(byte[] data)
        {
            var s = Convert.ToBase64String(data);
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-'); // 62nd char of encoding
            s = s.Replace('/', '_'); // 63rd char of encoding
            return s;
        }

        public static string ToUrlSafeBase64String(string val)
        {
            var bytes = System.Text.UTF8Encoding.UTF8.GetBytes(val);
            return ToUrlSafeBase64String(bytes);
        }
    }
}
