using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.DnsMadeEasy
{
    /// <summary>
    /// API calls based on https://api-docs.dnsmadeeasy.com/
    /// </summary>
    public class DnsProviderDnsMadeEasy : DnsProviderBase, IDnsProvider, IDisposable
    {
        private ILog _log;

        private class DnsQueryResults
        {
            public int TotalRecords { get; set; }
            public int TotalPages { get; set; }
            public List<DnsQueryResult> Data { get; set; }
            public int Page { get; set; }
        }

        private class DnsQueryResult
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
            public string Type { get; set; }
        }

        private static string _apiUrl = "https://api.dnsmadeeasy.com/V2.0/";

        private HttpClient _httpClient;
        private string _apiKey;
        private string _apiSecret;

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.DnsMadeEasy",
            Title = "DnsMadeEasy DNS API",
            Description = "Validates via DnsMadeEasy APIs using credentials found in your DnsMadeEasy control panel under Config - Account Settings",
            HelpUrl = "http://docs.certifytheweb.com/docs/dns-dnsmadeeasy.html",
            PropagationDelaySeconds = 60,
            ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Key="apikey", Name="API Key", IsRequired=true },
                        new ProviderParameter{Key="apisecret", Name="API Secret", IsRequired=true },
                        new ProviderParameter{ Key="zoneid",Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false }
                    },
            ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.DnsMadeEasy",
            HandlerType = ChallengeHandlerType.INTERNAL
        };

        public DnsProviderDnsMadeEasy(Dictionary<string, string> credentials)
        {
            _apiKey = credentials["apikey"];
            _apiSecret = credentials["apisecret"];
            _httpClient = new HttpClient();
        }

        private static string ComputeHMAC(string input, string key)
        {
            // inspired by/copied from https://github.com/Silvenga/DnsMadeEasy also from https://stackoverflow.com/questions/6067751/how-to-generate-hmac-sha1-in-c

            var encoding = Encoding.ASCII;

            var keyBytes = encoding.GetBytes(key);
            using (var hmacsha1 = new HMACSHA1(keyBytes))
            {
                var inputBytes = encoding.GetBytes(input);
                return hmacsha1
                    .ComputeHash(inputBytes)
                    .Aggregate("", (s, e) => s + string.Format("{0:x2}", e), s => s);
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url, DateTimeOffset requestDateTime)
        {
            /* from the docs:
             * Create the string representation of the current UTC date and time in HTTP format. Example: Sat, 12 Feb 2011 20:59:04 GMT
             * Calculate the hexadecimal HMAC SHA1 hash of that string using your Secret key as the hash key. Example: b3502e6116a324f3cf4a8ed693d78bcee8d8fe3c
             * Set the values for the request headers using your API key, the current date and time, and the HMAC hash that you calculated.
            */

            var requestDateString = requestDateTime.ToString("r");
            var hash = ComputeHMAC(requestDateString, _apiSecret);

            var request = new HttpRequestMessage(method, url);

            request.Headers.Add("x-dnsme-apiKey", _apiKey);
            request.Headers.Add("x-dnsme-hmac", hash);
            request.Headers.Add("x-dnsme-requestDate", requestDateString);

            return request;
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            DnsRecord domainInfo = null;

            try
            {
                domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);

                if (string.IsNullOrEmpty(domainInfo.RootDomain))
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = $"[{ProviderTitle}] Failed to create record: {exp.Message}" };
            }

            var recordName = NormaliseRecordName(domainInfo, request.RecordName);

            var url = $"{_apiUrl}dns/managed/{request.ZoneId}/records/";

            var apiRequest = CreateRequest(HttpMethod.Post, url, DateTimeOffset.Now);

            apiRequest.Content = new StringContent(
                    JsonConvert.SerializeObject(new
                    {
                        type = request.RecordType,
                        name = recordName,
                        value = request.RecordValue,
                        ttl = 5
                    })
                );

            apiRequest.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _httpClient.SendAsync(apiRequest);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not add dns record {recordName} to zone {request.ZoneId}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
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

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            // https://api-docs.dnsmadeeasy.com/ determine record id, if it exists

            // delete based on zoneid, recordId

            //https://api.dnsmadeeasy.com/V2.0/dns/managed/1119443/records/66814826

            DnsRecord domainInfo = null;

            try
            {
                domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);

                if (string.IsNullOrEmpty(domainInfo.RootDomain))
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = false, Message = $"[{ProviderTitle}] Failed to create record: {exp.Message}" };
            }

            var recordName = NormaliseRecordName(domainInfo, request.RecordName);

            var existingRecords = await GetDnsRecords(request.ZoneId);

            foreach (var r in existingRecords)
            {
                if (r.RecordName == recordName && r.RecordType == request.RecordType)
                {
                    //delete existing record
                    var url = $"{_apiUrl}dns/managed/{request.ZoneId}/records/{r.RecordId}";
                    var apiRequest = CreateRequest(HttpMethod.Delete, url, DateTimeOffset.Now);
                    var result = await _httpClient.SendAsync(apiRequest);

                    if (!result.IsSuccessStatusCode)
                    {
                        return new ActionResult
                        {
                            IsSuccess = false,
                            Message = $"Could not delete dns record {recordName} from zone {request.ZoneId}. Result: {result.StatusCode}"
                        };
                    }
                }
            }

            return new ActionResult { IsSuccess = true, Message = $"Dns record deleted: {recordName}" };
        }

        public async Task<List<DnsRecord>> GetDnsRecords(string zoneId)
        {
            var url = $"{_apiUrl}dns/managed/{zoneId}/records/";

            var request = CreateRequest(HttpMethod.Get, url, DateTimeOffset.Now);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var results = Newtonsoft.Json.JsonConvert.DeserializeObject<DnsQueryResults>(json);

                // TODO: paging

                var dnsRecords = results.Data.Select(x => new DnsRecord { RecordId = x.Id, RecordName = x.Name, RecordType = x.Type, RecordValue = x.Value }).ToList();
                return dnsRecords;
            }
            else
            {
                // failed
                throw new Exception("DnsMadeEasy: Failed to query DNS Records.");
            }
        }

        public override async Task<List<DnsZone>> GetZones()
        {
            // return all managed domains https://api.dnsmadeeasy.com/V2.0/dns/managed/
            var url = $"{_apiUrl}dns/managed/";

            var request = CreateRequest(HttpMethod.Get, url, DateTimeOffset.Now);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var results = Newtonsoft.Json.JsonConvert.DeserializeObject<DnsQueryResults>(json);

                // TODO: paging

                var dnsZones = results.Data.Select(x => new DnsZone { ZoneId = x.Id, Name = x.Name }).ToList();
                return dnsZones;
            }
            else
            {
                // failed
                var msg = await response.Content.ReadAsStringAsync();
                throw new Exception($"DnsMadeEasy: Failed to query DNS Zones. :: {msg}");
            }
        }

        public async Task<bool> InitProvider(ILog log = null)
        {
            _log = log;
            return await Task.FromResult(true);
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

        public void Dispose()
        {
            if (_httpClient != null)
            {
                _httpClient.Dispose();
            }
        }
    }
}
