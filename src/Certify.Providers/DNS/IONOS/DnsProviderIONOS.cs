using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
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
        private RestSharp.RestClient _client = new RestSharp.RestClient();
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
            return true;
        }

        public async Task<ActionResult> Test()
        {
            var result = new ActionResult();
            try
            {
                var zones = await GetZones();
                result.IsSuccess = zones.Any();
            }
            catch (Exception e)
            {
                _log.Error(e, "");
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

            var result = await _client.ExecuteAsync(CreateRequest(RestSharp.Method.GET, url));
            if (!result.IsSuccessful)
            {
                return Array.Empty<DnsRecord>().ToList();
            }

            var resultJson = result.Content;
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

            var httpRequest = CreateRequest(RestSharp.Method.PATCH, $"{baseUri}zones/{request.ZoneId}");
            httpRequest.AddJsonBody(ionosRecords);
            var result = await _client.ExecuteAsync(httpRequest);
            return new ActionResult
            {
                IsSuccess = result.IsSuccessful,
                Message = !result.IsSuccessful
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
                var httpRequest = CreateRequest(RestSharp.Method.DELETE, $"{baseUri}zones/{iRequest.ZoneId}/records/{iRequest.RecordId}");
                var result = await _client.ExecuteAsync(httpRequest);
                results.Add(new ActionResult
                {
                    IsSuccess = result.IsSuccessful,
                    Message = !result.IsSuccessful
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

        public async override Task<List<DnsZone>> GetZones()
        {
            var rawJsonDefinition = new[] { new { name = string.Empty, id = string.Empty, type = string.Empty } };
            var result = await _client.ExecuteAsync(CreateRequest(RestSharp.Method.GET, baseUri + "zones"));
            if (!result.IsSuccessful)
            {
                return Array.Empty<DnsZone>().ToList();
            }

            var resultJson = result.Content;
            var zonesRaw = JsonConvert.DeserializeAnonymousType(resultJson, rawJsonDefinition);
            return zonesRaw.Select(r => new DnsZone { Name = r.name, ZoneId = r.id }).ToList();
        }

        private RestSharp.RestRequest CreateRequest(RestSharp.Method httpMethod, string url, string httpContent = null)
        {
            var request = new RestSharp.RestRequest(url, httpMethod);
            if (httpContent != null)
            {
                request.AddParameter("application/json", httpContent);
            }

            request.AddHeader("accept", "application/json");
            request.AddHeader("X-API-Key", $"{_credentials["public"]}.{_credentials["secret"]}");
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
