using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.IONOS
{
    public class DnsProviderIONOSProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>,
        IDnsProviderProviderPlugin
    {
    }

    public class DnsProviderIONOS : DnsProviderBase, IDnsProvider
    {

        private ILog _log;
        private HttpClient _client;
        private Dictionary<string, string> _credentials;
        private const string baseUri = "https://api.hosting.ionos.com/dns/v1/";
        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.IONOS",
            Title = "IONOS DNS API",
            Description = "Validates via IONOS DNS APIs using keys",
            PropagationDelaySeconds = 60,
            ProviderParameters = new List<ProviderParameter>
            {
                new ProviderParameter{Key="public", Name="public key", IsRequired=true},
                new ProviderParameter{Key="secret", Name="secret key", IsRequired=true, IsCredential=true}
            },
            ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.IONOS",
            HandlerType = ChallengeHandlerType.INTERNAL,
            HelpUrl = "https://developer.hosting.ionos.com/docs/getstarted"
        };

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters,
            ILog log = null)
        {
            _log = log;
            _credentials = credentials;

            _client = new HttpClient();

            return await Task.FromResult(true);
        }

        public async Task<ActionResult> Test()
        {
            var result = new ActionResult();
            try
            {
                var zones = await GetZones();
                result.Message = "Authentication successful";
                result.IsSuccess = zones.Any();
            }
            catch (Exception)
            {
                result.Message = "Authentication failed";
                result.IsSuccess = false;
            }

            return result;
        }

        private async Task<DnsRecord> EnrichRecordByZoneIfRequired(DnsRecord request)
        {
            if (string.IsNullOrEmpty(request.ZoneId))
            {
                var zones = await GetZones();
                request.ZoneId = zones.Single(z => request.TargetDomainName.Contains(z.Name)).ZoneId;
            }

            return request;
        }

        private async Task<List<DnsRecord>> GetRecordsForZone(string zone, string suffix = null, string recordName = null, string recordType = null)
        {
            var rawJsonDefinition = new
            {
                name = string.Empty,
                id = string.Empty,
                type = string.Empty,
                records =
                new[] { new { name = string.Empty, id = string.Empty, type = string.Empty, content = string.Empty, rootName = string.Empty, changeDate = string.Empty, ttl = 0, disabled = false } }
            };
            var url = $"{baseUri}zones/{zone}";

            var parameters = new Dictionary<string, string> { { nameof(suffix), suffix }, { nameof(recordName), recordName }, { nameof(recordType), recordType } };

            var parameterString = string.Join("&", parameters.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Key}={p.Value}"));
            if (!string.IsNullOrWhiteSpace(parameterString))
            {
                url += "?" + parameterString;
            }

            var result = await _client.SendAsync(CreateRequest(HttpMethod.Get, url));
            if (!result.IsSuccessStatusCode)
            {
                return Array.Empty<DnsRecord>().ToList();
            }

            var resultJson = await result.Content.ReadAsStringAsync();
            var recordsRaw = JsonConvert.DeserializeAnonymousType(resultJson, rawJsonDefinition);

            return recordsRaw.records.Select(r => new DnsRecord
            {
                RecordId = r.id,
                RecordName = r.name,
                RecordType = r.type,
                RecordValue = r.content,
                RootDomain = r.rootName,
                Data = r.content,
                TargetDomainName = recordsRaw.name,
                ZoneId = zone
            }).ToList();
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            request = await EnrichRecordByZoneIfRequired(request);
            var records = await GetRecordsForZone(request.ZoneId, null, request.RecordName, "TXT");

            records.Add(request);

            var ionosRecords = records.Select(r => new
            {
                disabled = false,
                type = r.RecordType,
                content = r.RecordValue,
                name = r.RecordName,
                prio = 10,
                ttl = 60
            }).ToList();

            var httpRequest = CreateRequest(new HttpMethod("PATCH"), $"{baseUri}zones/{request.ZoneId}", JsonConvert.SerializeObject(ionosRecords));
            var result = await _client.SendAsync(httpRequest);
            return new ActionResult
            {
                IsSuccess = result.IsSuccessStatusCode,
                Message = !result.IsSuccessStatusCode
                    ? $"DNS Record {request.RecordName} with content {request.RecordValue} could not be created.\r\nHTTP-Error was {result.StatusCode}."
                    : null
            };
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            request = await EnrichRecordByZoneIfRequired(request);
            var requests = await EnrichRecordByRecordIdIfRequired(request);
            var results = new List<ActionResult>();
            foreach (var iRequest in requests)
            {
                var httpRequest = CreateRequest(HttpMethod.Delete, $"{baseUri}zones/{iRequest.ZoneId}/records/{iRequest.RecordId}");
                var result = await _client.SendAsync(httpRequest);
                results.Add(new ActionResult
                {
                    IsSuccess = result.IsSuccessStatusCode,
                    Message = !result.IsSuccessStatusCode
                        ? $"DNS Record {request.RecordName} could not be deleted.\r\nHTTP-Error was {result.StatusCode}."
                        : null
                });
            }

            var resultComplete = new ActionResult { IsSuccess = results.All(r => r.IsSuccess) };
            if (!resultComplete.IsSuccess)
            {
                resultComplete.Message = string.Join("\r\n", results.Where(r => !r.IsSuccess).Select(r => r.Message));
            }

            return resultComplete;
        }

        private async Task<List<DnsRecord>> EnrichRecordByRecordIdIfRequired(DnsRecord request)
        {
            var records = await GetRecordsForZone(request.ZoneId, null, request.RecordName, request.RecordType);
            return records;
        }

        public override async Task<List<DnsZone>> GetZones()
        {
            var rawJsonDefinition = new[] { new { name = string.Empty, id = string.Empty, type = string.Empty } };
            var result = await _client.SendAsync(CreateRequest(HttpMethod.Get, baseUri + "zones"));
            if (!result.IsSuccessStatusCode)
            {
                throw new Exception("DNS Zones could not be fetched due to a http exception.");
            }

            var resultJson = await result.Content.ReadAsStringAsync();
            var zonesRaw = JsonConvert.DeserializeAnonymousType(resultJson, rawJsonDefinition);
            return zonesRaw.Select(r => new DnsZone { Name = r.name, ZoneId = r.id }).ToList();
        }

        private HttpRequestMessage CreateRequest(HttpMethod httpMethod, string url, string httpContent = null)
        {
            if (_credentials == null || (_credentials != null && (!_credentials.ContainsKey("public") || !_credentials.ContainsKey("secret"))))
            {
                throw new Exception("IONOS DNS provider requires credentials to be set (Public Key and Secret)");
            }

            var request = new HttpRequestMessage(httpMethod, url);
            if (httpContent != null)
            {
                request.Content = new StringContent(httpContent, Encoding.Default, "application/json");
            }

            request.Headers.Add("accept", "application/json");
            request.Headers.Add("X-API-Key", $"{_credentials["public"]}.{_credentials["secret"]}");
            request.Headers.Add("User-Agent", "Certify");
            return request;
        }

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;
        public string ProviderId => Definition.Id;
        public string ProviderTitle => Definition.Title;
        public string ProviderDescription => Definition.Description;
        public string ProviderHelpUrl => Definition.HelpUrl;
        public bool IsTestModeSupported => Definition.IsTestModeSupported;
        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;
    }
}
