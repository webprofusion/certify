using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DNS.DnsMadeEasy
{
    /// <summary>
    /// API calls based on https://api-docs.dnsmadeeasy.com/ 
    /// </summary>
    public class DnsProviderDnsMadeEasy : IDnsProvider
    {
        public int PropagationDelaySeconds => 60;

        public string ProviderId => "DNS01.API.DnsMadeEasy";

        public string ProviderTitle => "DnsMadeEasy (dnsmadeeasy.com)";

        public string ProviderDescription => "Validates via DnsMadeEasy APIs using credentials found in your DnsMadeEasy control panel under Config - Account.";

        public string ProviderHelpUrl => "https://certifytheweb.com/docs/dns/dnsmadeeasy";

        public List<ProviderParameter> ProviderParameters => new List<ProviderParameter>{
                    new ProviderParameter{Key="apikey", Name="API Key", IsRequired=true },
                    new ProviderParameter{Key="apisecret", Name="API Secret", IsRequired=true }
                };

        // private static string _apiUrl = "https://api.dnsmadeeasy.com/V2.0/";
        private static string _apiUrl = "https://api.sandbox.dnsmadeeasy.com/V2.0/";

        private HttpClient _httpClient;
        private string _apiKey;
        private string _apiSecret;

        public DnsProviderDnsMadeEasy(Dictionary<string, string> credentials)
        {
            _apiKey = credentials["apikey"];
            _apiSecret = credentials["apisecret"];
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

        public Task<ActionResult> CreateRecord(DnsCreateRecordRequest request)
        {
            string url = $"{_apiUrl}/dns/managed/{request.ZoneId}/records/";

            ///{"name":"mail","type":"A","value":"1.1.1.1","gtdLocation":"DEFAULT","ttl":86400}
            ///
            throw new System.NotImplementedException();
        }

        public Task<ActionResult> DeleteRecord(DnsDeleteRecordRequest request)
        {
            // https://api-docs.dnsmadeeasy.com/ determine record id, if it exists

            // delete based on zoneid, recordId

            //https://api.dnsmadeeasy.com/V2.0/dns/managed/1119443/records/66814826
            throw new System.NotImplementedException();
        }

        public async Task<List<DnsZone>> GetZones()
        {
            // return all managed domains https://api.dnsmadeeasy.com/V2.0/dns/managed/
            throw new System.NotImplementedException();
        }

        public async Task<bool> InitProvider()
        {
            return await Task.FromResult(true);
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
    }
}
