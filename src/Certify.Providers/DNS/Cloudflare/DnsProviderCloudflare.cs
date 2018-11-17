using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.Cloudflare
{
    /// <summary>
    /// Adapted from
    /// https://github.com/ebekker/ACMESharp/tree/master/ACMESharp/ACMESharp.Providers.CloudFlare By
    /// janpieterz and ebekker, used with permission under MIT license
    /// </summary>
    internal class ZoneResult
    {
        public Zone[] Result { get; set; }

        [JsonProperty("result_info")]
        public ResultInfo ResultInfo { get; set; }
    }

    internal class Zone
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    internal class ResultInfo
    {
        public int Page { get; set; }

        [JsonProperty("per_page")]
        public int PerPage { get; set; }

        [JsonProperty("total_pages")]
        public int TotalPages { get; set; }

        public int Count { get; set; }

        [JsonProperty("total_count")]
        public int TotalCount { get; set; }
    }

    internal class DnsRecordCloudflare
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Content { get; set; }
    }

    internal class DnsResultCloudflare
    {
        public DnsRecordCloudflare[] Result { get; set; }

        [JsonProperty("result_info")]
        public ResultInfo ResultInfo { get; set; }
    }

    /// <summary>
    /// Helper class to interface with the CloudFlare API endpoint.
    /// </summary>
    /// <remarks> 
    /// See <see cref="https://api.cloudflare.com/#getting-started-endpoints" /> for more details.
    /// </remarks>
    public class DnsProviderCloudflare : IDnsProvider
    {
        private ILog _log;

        private HttpClient _client = new HttpClient();

        private readonly string _authKey;
        private readonly string _emailAddress;

        private const string _baseUri = "https://api.cloudflare.com/client/v4/";
        private const string _listZonesUri = _baseUri + "zones";
        private const string _createRecordUri = _baseUri + "zones/{0}/dns_records";
        private const string _listRecordsUri = _baseUri + "zones/{0}/dns_records";
        private const string _deleteRecordUri = _baseUri + "zones/{0}/dns_records/{1}";
        private const string _updateRecordUri = _baseUri + "zones/{0}/dns_records/{1}";

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    Id = "DNS01.API.Cloudflare",
                    Title = "Cloudflare DNS API",
                    Description = "Validates via Cloudflare DNS APIs using credentials",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns-cloudflare.html",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Key="emailaddress", Name="Email Address", IsRequired=true },
                        new ProviderParameter{Key="authkey", Name="Auth Key", IsRequired=true },
                        new ProviderParameter{ Key="zoneid",Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false }
                     },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.Cloudflare",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderCloudflare(Dictionary<string, string> credentials)
        {
            _authKey = credentials["authkey"];
            _emailAddress = credentials["emailaddress"];
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await this.GetZones();

                if (zones != null && zones.Any())
                {
                    return new ActionResult { IsSuccess = true, Message = "Dns API Test Completed OK." };
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = true, Message = $"Dns API Test Failed: {exp.Message}" };
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-AUTH-KEY", _authKey);
            request.Headers.Add("X-AUTH-EMAIL", _emailAddress);
            return request;
        }

        private async Task<List<DnsRecordCloudflare>> GetDnsRecords(string zoneId)
        {
            List<DnsRecordCloudflare> records = new List<DnsRecordCloudflare>();
            bool finishedPaginating = false;
            int page = 1;

            while (!finishedPaginating)
            {
                var request = CreateRequest(HttpMethod.Get, $"{string.Format(_listRecordsUri, zoneId)}?page={page}");

                var result = await _client.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStringAsync();
                    var dnsResult = JsonConvert.DeserializeObject<DnsResultCloudflare>(content);

                    records.AddRange(dnsResult.Result);

                    if (dnsResult.ResultInfo.Page == dnsResult.ResultInfo.TotalPages)
                    {
                        finishedPaginating = true;
                    }
                    else
                    {
                        page = page + 1;
                    }
                }
                else
                {
                    throw new Exception($"Could not get DNS records for zone {zoneId}. Result: {result.StatusCode} - {result.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");
                }
            }
            return records;
        }

        private async Task<ActionResult> AddDnsRecord(string zoneId, string name, string value)
        {
            var request = CreateRequest(HttpMethod.Post, string.Format(_createRecordUri, zoneId));

            request.Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    type = "TXT",
                    name = name,
                    content = value,
                    ttl = 120
                })
                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not add dns record {name} to zone {zoneId}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = $"DNS record added: {name}"
                };
            }
        }

        private async Task<ActionResult> UpdateDnsRecord(string zoneId, DnsRecordCloudflare record, string value)
        {
            var request = CreateRequest(HttpMethod.Put, string.Format(_updateRecordUri, zoneId, record.Id));
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    type = "TXT",
                    name = record.Name,
                    content = value,
                    ttl = 120
                })
                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not update dns record {record.Name} to zone {zoneId}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult { IsSuccess = true, Message = $"DNS record updated: {record.Name}" };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            try
            {
                var records = await GetDnsRecords(request.ZoneId);

                return await AddDnsRecord(request.ZoneId, request.RecordName, request.RecordValue);
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = exp.Message };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var records = await GetDnsRecords(request.ZoneId);
            var recordsToDelete = records.Where(x => x.Name == request.RecordName);

            if (!recordsToDelete.Any())
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record does not exist, nothing to delete." };
            }

            var itemError = "";

            // when deleting a TXT record with multiple values several records will be returned but
            // only the first delete will succeed (removing all values) for this reason we return a
            // success state even if the delete failed
            foreach (var r in recordsToDelete)
            {
                var req = CreateRequest(HttpMethod.Delete, string.Format(_deleteRecordUri, request.ZoneId, r.Id));

                var result = await _client.SendAsync(req);
                if (!result.IsSuccessStatusCode)
                {
                    itemError += " " + await result.Content.ReadAsStringAsync();
                    return new ActionResult { IsSuccess = false, Message = $"DNS record delete failed: {request.RecordName}" };
                }
            }

            return new ActionResult { IsSuccess = true, Message = $"DNS record deleted: {request.RecordName}" };
        }

        public async Task<List<DnsZone>> GetZones()
        {
            var zones = new List<DnsZone>();
            bool finishedPaginating = false;
            int page = 1;

            while (!finishedPaginating)
            {
                var request = CreateRequest(HttpMethod.Get, $"{_listZonesUri}?page={page}");

                var result = await _client.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadAsStringAsync();
                    var zonesResult = JsonConvert.DeserializeObject<ZoneResult>(content);

                    foreach (var z in zonesResult.Result)
                    {
                        zones.Add(new DnsZone { ZoneId = z.Id, Name = z.Name });
                    }

                    if (zonesResult.ResultInfo.Page == zonesResult.ResultInfo.TotalPages)
                    {
                        finishedPaginating = true;
                    }
                    else
                    {
                        page++;
                    }
                }
                else
                {
                    return new List<DnsZone>();
                }
            }

            return zones;
        }

        public async Task<bool> InitProvider(ILog log = null)
        {
            _log = log;
            return await Task.FromResult(true);
        }
    }
}
