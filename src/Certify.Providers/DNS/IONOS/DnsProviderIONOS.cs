using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

    public class DnsProviderIONOS : DnsProviderBase,IDnsProvider
    {

        private ILog _log;
        private HttpClient _client = new HttpClient();
        private Dictionary<string, string> _credentials;
        private Dictionary<string, string> _parameters;
        private const string baseUri = "https://api.hosting.ionos.com/dns/v1/";
        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.IONOS",
            Title ="IONOS DNS API",
            Description = "Validates via IONOS DNS APIs using keys",
            PropagationDelaySeconds = 60,
            ProviderParameters = new List<ProviderParameter>
            {
                new ProviderParameter{Key="public", Name="public key", IsRequired=true},
                new ProviderParameter{Key="secret", Name="secret key", IsRequired=true, IsCredential=true}
            }
        };

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters,
            ILog log = null)
        {
            _log = log;
            _credentials = credentials;
            _parameters = parameters;
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

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var ionosRecord = new
            {
                disabled = false,
                type = request.RecordType,
                content = request.RecordValue,
                name = request.RecordName,
                prio = 10,
                ttl = 1
            };
            var content = new StringContent(JsonConvert.SerializeObject(ionosRecord));
            content.Headers.ContentType.MediaType = "application/json";
            var httpRequest = CreateRequest(HttpMethod.Put, $"{baseUri}zones/{request.ZoneId}/records/{request.RecordId}", content);
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
            var httpRequest = CreateRequest(HttpMethod.Delete, $"{baseUri}zones/{request.ZoneId}/records/{request.RecordId}");
            var result = await _client.SendAsync(httpRequest);
            return new ActionResult
            {
                IsSuccess = result.IsSuccessStatusCode,
                Message = !result.IsSuccessStatusCode
                    ? $"DNS Record {request.RecordName} could not be deleted.\r\nHTTP-Error was {result.StatusCode}."
                    : null
            };
        }
        public async override Task<List<DnsZone>> GetZones()
        {
            var rawJsonDefinition = new [] {new {name = string.Empty, id = string.Empty, type = string.Empty}};
            var result = await _client.SendAsync(CreateRequest(HttpMethod.Get, baseUri + "zones"));
            var resultJson = await result.Content.ReadAsStringAsync();
            var zonesRaw = JsonConvert.DeserializeAnonymousType(resultJson, rawJsonDefinition);
            return zonesRaw.Select(r => new DnsZone {Name = r.name, ZoneId = r.id}).ToList();
        }

        private HttpRequestMessage CreateRequest(HttpMethod httpMethod, string url, HttpContent httpContent = null)
        {
            var request = new HttpRequestMessage(httpMethod, url);
            if (httpContent != null)
            {
                request.Content = httpContent;
            }
            request.Headers.Add("X-API-Key", $"{_parameters["public"]}.{_credentials["secret"]}");
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
