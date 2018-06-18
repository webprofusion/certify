using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.SimpleDNSPlus
{
    /// <summary>
    /// SimpleDNSPlus DNS API Provider contributed by https://github.com/alphaz18 
    /// </summary>
    internal class Zone
    {
        public string Domain { get; set; }
        public string DomainId { get; set; }
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

    public class DnsProviderSimpleDNSPlus : IDnsProvider
    {
        private HttpClient _client = new HttpClient();
        private readonly string _authKey;
        private readonly string _authSecret;
        private readonly string _authServer;
        private string _baseUri;
        private string _listZonesUri;
        private string _createRecordUri;
        private string _listRecordsUri;
        private string _deleteRecordUri;
        private string _updateRecordUri;

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
                    Id = "DNS01.API.SimpleDNSPlus",
                    Title = "SimpleDNSPlus DNS API",
                    Description = "Validates via SimpleDNSPlus DNS APIs using credentials",
                    HelpUrl = "https://simpledns.com/swagger-ui/",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="authserver", Name="Server IP", IsRequired=true },
                        new ProviderParameter{ Key="authkey", Name="Auth Key", IsRequired=true },
                        new ProviderParameter{ Key="authsecret", Name="Auth Secret", IsRequired=true },
                        new ProviderParameter{ Key="zoneid", Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false }
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.SimpleDNSPlus",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public string ListZonesUri { get => _listZonesUri; set => _listZonesUri = value; }

        public DnsProviderSimpleDNSPlus(Dictionary<string, string> credentials)
        {

            _authKey = credentials["authkey"];
            _authSecret = credentials["authsecret"];
            _authServer = credentials["authserver"];
            _baseUri = "https://" + _authServer + "/v2/";
            _listZonesUri = _baseUri + "zones";
            _createRecordUri = _baseUri + "zones/{0}/records";
            _listRecordsUri = _baseUri + "zones/{0}/records";
            _deleteRecordUri = _baseUri + "zones/{0}/records";
            _updateRecordUri = _baseUri + "zones/{0}/records";

        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await this.GetZones();
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

        private async Task<List<DnsRecord>> GetDnsRecords(string zoneName)
        {
            var records = new List<DnsRecord>();

            var domains = zoneName.Split(new char[] { '.' });
            var tldName = domains[domains.Length - 2] + "." + domains[domains.Length - 1];
            var sub = "";

            for (int i = 0; i < domains.Length - 1; i++)
            {
                sub += domains[i];
            }

            var request = CreateRequest(HttpMethod.Get, $"{string.Format(_listRecordsUri, tldName)}");

            var result = await _client.SendAsync(request);

            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var dnsResult = JsonConvert.DeserializeObject<DnsRecordSimpleDNSPlus[]>(content);

                records.AddRange(dnsResult.Select(x => new DnsRecord { RecordId = x.Name, RecordName = x.Name, RecordType = x.Type, RecordValue = x.Data }));
            }
            else
            {
                throw new Exception($"Could not get DNS records for zone {tldName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}");
            }

            return records;
        }

        private async Task<ActionResult> AddDnsRecord(string zoneName, string recordname, string value)
        {
            var request = CreateRequest(new HttpMethod("PATCH"), string.Format(_createRecordUri, zoneName));
            var rec = new DnsRecordSimpleDNSPlus();
            rec.Type = "TXT"; rec.Name = recordname + "." + zoneName; rec.Data = value; rec.TTL = 600; rec.Remove = false;
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

        private async Task<ActionResult> UpdateDnsRecord(string zoneName, DnsRecord record, string value)
        {
            await Test();

            var request = CreateRequest(HttpMethod.Put, string.Format(_updateRecordUri, zoneName));

            request.Content = new StringContent(
                JsonConvert.SerializeObject(new object[] { new
                        {
                            Name = record.RecordName,
                            Type = "TXT",
                            Data = value,
                            TTL = 600
                        }
                    })
                );

            request.Content.Headers.ContentType.MediaType = "application/json";

            var result = await _client.SendAsync(request);

            if (!result.IsSuccessStatusCode)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not update dns record {record.RecordName} to zone {zoneName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
            else
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record updated" };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            //check if record already exists
            string[] domains = request.RecordName.Split(new char[] { '.' });
            string tldName = domains[domains.Length - 2] + "." + domains[domains.Length - 1];
            string sub = "";
            for (int i = 0; i < domains.Length - 2; i++)
            {
                if (i == 0)
                {
                    sub += domains[i];
                }
                else
                {
                    sub += "." + domains[i];
                }
            }
            var records = await GetDnsRecords(tldName);
            var record = records.FirstOrDefault(x => x.RecordName == sub);

            return await AddDnsRecord(tldName, sub, request.RecordValue);

        }

        public async Task<ActionResult> DeleteRecord(DnsRecord requestreq)
        {
            // grab all the txt records for the zone as a json array, remove the txt record in
            // question, and send an update command.
            var recordname = requestreq.RecordName;
            var zoneName = requestreq.RootDomain;
            var request = CreateRequest(new HttpMethod("PATCH"), string.Format(_createRecordUri, zoneName));
            var rec = new DnsRecordSimpleDNSPlus();
            rec.Type = "TXT"; rec.Name = recordname + "." + zoneName; rec.Remove = true;
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
                    Message = $"Could not delete dns record {recordname} to zone {zoneName}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
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

        public async Task<List<DnsZone>> GetZones()
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
                    zones.Add(new DnsZone { ZoneId = zone.DomainId, Name = zone.Domain });
                }
            }
            else
            {
                return new List<DnsZone>();
            }

            return zones;
        }

        public async Task<bool> InitProvider()
        {
            return await Task.FromResult(true);
        }
    }
}
