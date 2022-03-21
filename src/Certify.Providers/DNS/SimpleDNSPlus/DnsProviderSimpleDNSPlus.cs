using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.SimpleDNSPlus
{
    public class DnsProviderSimpleDNSPlusProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    internal class Zone
    {
        public string Domain { get; set; }
        public string DomainId { get; set; }

        public string Name { get; set; }
        public string Type { get; set; }
    }

    internal class DnsRecordSimpleDNSPlus
    {
        public string Data { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public int TTL { get; set; }
        public bool Remove { get; set; }
    }

    internal class DnsResultSimpleDNSPlus
    {
        public DnsRecord[] Result { get; set; }
    }

    /// <summary>
    /// SimpleDNSPlus DNS API Provider contributed by https://github.com/alphaz18
    /// </summary>
    public class DnsProviderSimpleDNSPlus : DnsProviderBase, IDnsProvider
    {
        private ILog _log;
        private HttpClient _client = new HttpClient();
        private string _authKey;
        private string _authSecret;
        private string _authServer;
        private string _baseUri;
        private string _listZonesUri;
        private string _createRecordUri;

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
                    Id = "DNS01.API.SimpleDNSPlus",
                    Title = "SimpleDNSPlus DNS API",
                    Description = "Validates via SimpleDNSPlus DNS APIs (use /v2/ endpoint) using basic authentication credentials",
                    HelpUrl = "https://simpledns.com/swagger-ui/",
                    PropagationDelaySeconds = 120,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="authserver", Name="API Host", IsRequired=true,Description="e.g. https://192.168.1.32:8053 or http://myserver:8053" },
                        new ProviderParameter{ Key="authkey", Name="Username", IsRequired=true },
                        new ProviderParameter{ Key="authsecret", Name="Password", IsRequired=true },
                        new ProviderParameter{ Key="zoneid", Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false },
                        new ProviderParameter{ Key="propagationdelay",Name="Propagation Delay Seconds", IsRequired=false, IsPassword=false, Value="120", IsCredential=false }
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.SimpleDNSPlus",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public string ListZonesUri { get => _listZonesUri; set => _listZonesUri = value; }

        public DnsProviderSimpleDNSPlus()
        {
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await GetZones();
                if (zones != null && zones.Any())
                {
                    return new ActionResult { IsSuccess = true, Message = "Test Completed OK." };
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = true, Message = $"Test Failed: {exp.Message}" };
            }
        }

        private static string Base64Encode(string Txt)
        {
            var txtBytes = System.Text.Encoding.UTF8.GetBytes(Txt);
            return System.Convert.ToBase64String(txtBytes);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
            var request = new HttpRequestMessage(method, url);
            var basicAuthString = Base64Encode(_authKey + ":" + _authSecret);
            request.Headers.Add("Authorization", $"Basic {basicAuthString}");
            return request;
        }

        private async Task<ActionResult> AddDnsRecord(string zoneName, string recordname, string value)
        {
            var request = CreateRequest(new HttpMethod("PATCH"), string.Format(_createRecordUri, zoneName));
            var rec = new DnsRecordSimpleDNSPlus()
            {
                Type = "TXT",
                Name = recordname + "." + zoneName,
                Data = value,
                TTL = 600,
                Remove = false
            };

            var recarr = new object[] { rec };

            request.Content = new StringContent(
                JsonConvert.SerializeObject(recarr)

                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not add dns record {recordname} to zone {zoneName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record added."
                };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // determine root domain and normalise new record name
            var domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);
            if (string.IsNullOrEmpty(domainInfo.RootDomain))
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
            }

            var rootDomain = domainInfo.RootDomain;
            var subdomainRecord = NormaliseRecordName(domainInfo, request.RecordName);

            return await AddDnsRecord(rootDomain, subdomainRecord, request.RecordValue);
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord requestreq)
        {
            // determine root domain and normalise new record name
            var domainInfo = await DetermineZoneDomainRoot(requestreq.RecordName, requestreq.ZoneId);
            if (string.IsNullOrEmpty(domainInfo.RootDomain))
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
            }

            var rootDomain = domainInfo.RootDomain;
            var subdomainRecord = NormaliseRecordName(domainInfo, requestreq.RecordName);

            // Send PATCH request with Remove flag set to try for our TXT record

            var request = CreateRequest(new HttpMethod("PATCH"), string.Format(_createRecordUri, rootDomain));

            var rec = new
            {
                Type = "TXT",
                Name = subdomainRecord + "." + rootDomain,
                Remove = true
            };

            var recarr = new object[] { rec };

            request.Content = new StringContent(
                JsonConvert.SerializeObject(recarr)
                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not delete dns record {subdomainRecord} to zone {rootDomain}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record deleted."
                };
            }
        }

        public async override Task<List<DnsZone>> GetZones()
        {
            var zones = new List<DnsZone>();

            var request = CreateRequest(HttpMethod.Get, $"{_listZonesUri}");

            var result = await _client.SendAsync(request);

            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var zonesResult = JsonConvert.DeserializeObject<IEnumerable<Zone>>(content).ToList();

                foreach (var zone in zonesResult)
                {
                    zones.Add(new DnsZone { ZoneId = zone.DomainId ?? zone.Name, Name = zone.Domain ?? zone.Name });
                }
            }
            else
            {
                return new List<DnsZone>();
            }

            return zones;
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;

            _authKey = credentials["authkey"];
            _authSecret = credentials["authsecret"];
            _authServer = credentials["authserver"];

            if (_authServer.StartsWith("http"))
            {
                _baseUri = _authServer.Trim('/') + "/v2/";
            }
            else
            {
                _baseUri = "https://" + _authServer.Trim('/') + "/v2/";
            }
            _listZonesUri = _baseUri + "zones";
            _createRecordUri = _baseUri + "zones/{0}/records";

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out int customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return await Task.FromResult(true);
        }
    }
}
