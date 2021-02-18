using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Providers.DNS.TransIP.Authentication;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.TransIP
{
    internal class DnsClient
    {
        private HttpClient _client;
        private Authenticator _authenticator;

        internal DnsClient(
            string login,
            string privateKey,
            int loginDuration)
        {
            _authenticator = new Authenticator(login, privateKey, loginDuration);

            _client = new HttpClient();
        }

        private async Task<ActionResult<HttpRequestMessage>> CreateRequest(HttpMethod method, string url)
        {
            var login = await _authenticator.GetLoginToken();
            if (!login.IsSuccess)
            {
                return new ActionResult<HttpRequestMessage> { IsSuccess = false, Message = login.Message };
            }

            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Authorization", $"Bearer {login.Result}");
            return new ActionResult<HttpRequestMessage> { IsSuccess = true, Result = request };
        }

        private StringContent GetContent(object value)
        {
            var json = JsonConvert.SerializeObject(value);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        internal async Task<ActionResult<IEnumerable<DTO.Domain>>> GetDomains()
        {
            var request = await CreateRequest(HttpMethod.Get, DnsProviderTransIP.LIST_DOMAINS_URI);
            if (!request.IsSuccess)
            {
                return new ActionResult<IEnumerable<DTO.Domain>> { IsSuccess = false, Message = request.Message };
            }

            var result = await _client.SendAsync(request.Result);

            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var domains = JsonConvert.DeserializeObject<DTO.Domains>(content).domains;
                return new ActionResult<IEnumerable<DTO.Domain>> { IsSuccess = true, Result = domains };
            }
            else
            {
                return new ActionResult<IEnumerable<DTO.Domain>>
                {
                    IsSuccess = false,
                    Message = $"Could not get domains. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
        }

        internal async Task<ActionResult<IEnumerable<DTO.DnsEntry>>> GetDnsEntries(string domain)
        {
            var request = await CreateRequest(HttpMethod.Get, string.Format(DnsProviderTransIP.RECORD_URI, domain));
            if (!request.IsSuccess)
            {
                return new ActionResult<IEnumerable<DTO.DnsEntry>> { IsSuccess = false, Message = request.Message };
            }

            var result = await _client.SendAsync(request.Result);

            if (result.IsSuccessStatusCode)
            {
                var content = await result.Content.ReadAsStringAsync();
                var entries = JsonConvert.DeserializeObject<DTO.DnsEntries>(content).dnsEntries;
                return new ActionResult<IEnumerable<DTO.DnsEntry>> { IsSuccess = true, Result = entries };
            }
            else
            {
                return new ActionResult<IEnumerable<DTO.DnsEntry>>
                {
                    IsSuccess = false,
                    Message = $"Could not get DNS entries. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
        }

        internal async Task<ActionResult> Add(string domain, DTO.DnsEntry entry)
        {
            var request = await CreateRequest(HttpMethod.Post, string.Format(DnsProviderTransIP.RECORD_URI, domain));
            if (!request.IsSuccess)
            {
                return request;
            }

            request.Result.Content = GetContent(new DTO.SingleDnsEntry { dnsEntry = entry });
            var result = await _client.SendAsync(request.Result);

            if (result.IsSuccessStatusCode)
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record added." };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not add DNS record {entry.name} to zone {domain}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
        }

        internal async Task<ActionResult> Update(string domain, DTO.DnsEntry entry)
        {
            var request = await CreateRequest(new HttpMethod("PATCH"), string.Format(DnsProviderTransIP.RECORD_URI, domain));
            if (!request.IsSuccess)
            {
                return request;
            }

            request.Result.Content = GetContent(new DTO.SingleDnsEntry { dnsEntry = entry });
            var result = await _client.SendAsync(request.Result);

            if (result.IsSuccessStatusCode)
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record updated." };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not update DNS record {entry.name} to zone {domain}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
        }

        internal async Task<ActionResult> Remove(string domain, DTO.DnsEntry entry)
        {
            var request = await CreateRequest(HttpMethod.Delete, string.Format(DnsProviderTransIP.RECORD_URI, domain));
            if (!request.IsSuccess)
            {
                return request;
            }

            request.Result.Content = GetContent(new DTO.SingleDnsEntry { dnsEntry = entry });
            var result = await _client.SendAsync(request.Result);

            if (result.IsSuccessStatusCode)
            {
                return new ActionResult { IsSuccess = true, Message = "DNS record deleted." };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not delete DNS record {entry.name} from zone {domain}. Result: {result.StatusCode} - {await result.Content.ReadAsStringAsync()}"
                };
            }
        }
    }
}
