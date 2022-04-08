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

/// <summary>
/// Certify DNS is based on the ACME DNS provider with some extensions including credentials
/// </summary>
namespace Certify.Providers.DNS.CertifyDns
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

        // length of time to wait before checking challenge response, non-standard and used by CertifyDns in some cases
        public int? propagationDelaySeconds { get; set; }
    }

#pragma warning restore IDE1006 // Naming Styles

    public class DnsProviderCertifyDnsProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderCertifyDns : IDnsProvider
    {
        public static ChallengeProviderDefinition Definition
        {
            get
            {
                return new ChallengeProviderDefinition
                {
                    Id = "DNS01.API.CertifyDns",
                    Title = "Certify DNS",
                    Description = "Validates DNS Challenges via Certify DNS (a cloud based service provided by certifytheweb.com). This requires the one-time creation of CNAME records per domain.",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns/providers/certifydns",
                    PropagationDelaySeconds = 5,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="api",Name="API Url", IsRequired=true, IsCredential=false, IsPassword=false, Value="https://certify-dns.certifytheweb.com", Description="Base URL for a managed version of acme-dns" },
                        new ProviderParameter{ Key="user",Name="API Username", IsRequired=true, IsCredential=true, IsPassword=false,  Description="API Username" },
                        new ProviderParameter{ Key="key",Name="API Key", IsRequired=true, IsCredential=true, IsPassword=false,  Description="API Key" },

                    },
                    IsTestModeSupported = false,
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.CertifyDns",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderCertifyDns() : base()
        {
            this.EnableExtensions = true;

            _settingsPath = EnvironmentUtil.GetAppDataFolder();

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "Certify/DnsProviderCertifyDns");

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        private Dictionary<string, string> _credentials;

        private ILog _log;

        private int? _customPropagationDelay = null;

        /// <summary>
        /// if true, enable extensions to the base standard
        /// </summary>
        public bool EnableExtensions = false;

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        private HttpClient _client;

        private Dictionary<string, string> _parameters = new Dictionary<string, string>();

        private JsonSerializerSettings _serializerSettings;

        private string _settingsPath { get; set; }

        private async Task<ValueTuple<AcmeDnsRegistration, bool>> Register(string settingsPath, string domainId)
        {

            var apiPrefix = "";

            if (_parameters["api"] != null)
            {
                _client.BaseAddress = new System.Uri(_parameters["api"]);

                // we prefix the settings file with the encoded API url as these settings are 
                // only useful on the target API, changing the api should change all settings
                apiPrefix = ToUrlSafeBase64String(_client.BaseAddress.Host);
            }

            var registrationSettingsPath = Path.Combine(settingsPath, "acmedns");

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

                // is an existing registration
                return (reg, false);
            }

            var registration = new AcmeDnsRegistration();

            if (_parameters.ContainsKey("allowfrom") && _parameters["allowfrom"] != null)
            {
                var rules = _parameters["allowfrom"].Split(';');
                registration.allowfrom = new List<string>();
                foreach (var r in rules)
                {
                    registration.allowfrom.Add(r.Trim().ToLower());
                }
            }

            if (EnableExtensions)
            {
                registration.subject = domainId;
            }

            var json = JsonConvert.SerializeObject(registration, _serializerSettings);

            var req = new HttpRequestMessage(HttpMethod.Post, "/register");

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
                System.IO.File.WriteAllText(registrationSettingsPath, JsonConvert.SerializeObject(registration));

                // is a new registration
                return (registration, true);
            }
            else
            {
                // failed to register
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
                return new ActionResult { IsSuccess = false, Message = $"Failed to register with Certify DNS. Check API Url and required credentials (if any)." };
            }

            if (isNewRegistration)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"[Action Required] To complete setup, add a CNAME record in your DNS:\r\n\t{request.RecordName}\r\nwith the value:\r\n\t{registration.fulldomain} ",
                    Result = new { Name = request.RecordName, Value = registration.fulldomain }
                };
            }

            var req = new HttpRequestMessage(HttpMethod.Post, "/update");
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

                    if (updateResult.propagationDelaySeconds != null)
                    {
                        // some variants (CertifyDns) may provide a preferred propagation delay time which can vary.
                        _customPropagationDelay = updateResult.propagationDelaySeconds;
                    }

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
                new ActionResult { IsSuccess = true, Message = $"Dns Record Delete skipped: {request.RecordName}" }
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
                if (int.TryParse(parameters["propagationdelay"], out int customPropDelay))
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
