using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

// ReSharper disable once CheckNamespace
namespace Certify.Providers.DNS.NameCheap
{
    public class DnsProviderNameCheap: IDnsProvider
    {
        public DnsProviderNameCheap(Dictionary<string, string> credentials)
        {
            _apiUser = credentials[PARAM_API_USER];
            _apiKey = credentials[PARAM_API_KEY];
            _ip = credentials[PARAM_IP];

            _http = new HttpClient();
        }

        private readonly string _apiUser;
        private readonly string _apiKey;
        private readonly string _ip;

        private readonly HttpClient _http;

        private ILog _log;

        #region Definition

        private const string PARAM_API_USER = "apiuser";
        private const string PARAM_API_KEY = "apikey";
        private const string PARAM_IP = "ip";

        private const string API_URL = "https://api.sandbox.namecheap.com/xml.response";
        private const int BATCH_SIZE = 100;

        private static XNamespace _ns;

        static DnsProviderNameCheap()
        {
            Definition = new ProviderDefinition
            {
                Id = "DNS01.API.NameCheap",
                Title = "NameCheap DNS API",
                Description = "Validates via NameCheap APIs",
                HelpUrl = "https://www.namecheap.com/",
                PropagationDelaySeconds = 60,
                ProviderParameters = new List<ProviderParameter>
                {
                    new ProviderParameter { Key = PARAM_API_USER, Name = "API User", IsRequired = true, IsPassword = false },
                    new ProviderParameter { Key = PARAM_API_KEY, Name = "API Key", IsRequired = true, IsPassword = true },
                    new ProviderParameter { Key = PARAM_IP, Name = "Your IP", Description = "IP Address of the server that sends requests to NameCheap API", IsRequired = true, IsPassword = false }
                },
                ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                Config = "Provider=Certify.Providers.DNS.NameCheap",
                HandlerType = ChallengeHandlerType.INTERNAL
            };

            _ns = XNamespace.Get("http://api.namecheap.com/xml.response");
        }

        /// <summary>
        /// The definition properties for NameCheap.
        /// </summary>
        public static ProviderDefinition Definition { get; private set; }

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;
        public string ProviderId => Definition.Id;
        public string ProviderTitle => Definition.Title;
        public string ProviderDescription => Definition.Description;
        public string ProviderHelpUrl => Definition.HelpUrl;
        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        #endregion

        /// <summary>
        /// Initializes the provider.
        /// </summary>
        public Task<bool> InitProvider(ILog log = null)
        {
            _log = log;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Tests connection to the service.
        /// </summary>
        public async Task<ActionResult> Test()
        {
            try
            {
                var zones = await GetZonesBatchAsync(1);
                if (zones?.Any() == true)
                    return new ActionResult {IsSuccess = true, Message = "Test completed."};

                return new ActionResult {IsSuccess = true, Message = "Test completed, but no zones returned."};
            }
            catch (Exception ex)
            {
                return new ActionResult {IsSuccess = false, Message = "Test failed: " + ex.Message };
            }
        }

        /// <summary>
        /// Adds a DNS record to the list.
        /// </summary>
        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Removes a DNS record from the list.
        /// </summary>
        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns the list of available zones.
        /// </summary>
        public async Task<List<DnsZone>> GetZones()
        {
            var result = new List<DnsZone>();
            var page = 1;

            while (true)
            {
                var zoneBatch = await GetZonesBatchAsync(page);
                result.AddRange(zoneBatch);
                page++;

                if (zoneBatch.Count < BATCH_SIZE)
                    break;
            }

            return result;
        }

        /// <summary>
        /// Returns a batch of zones
        /// </summary>
        private async Task<IReadOnlyList<DnsZone>> GetZonesBatchAsync(int page)
        {
            try
            {
                var xmlResponse = await InvokeGetApiAsync("domains.getList", new Dictionary<string, string>
                {
                    ["Page"] = page.ToString(),
                    ["PageSize"] = BATCH_SIZE.ToString(),
                    ["SortBy"] = "NAME"
                });

                return xmlResponse.Element(_ns + "CommandResponse")
                                  .Element(_ns + "DomainGetListResult")
                                  .Elements(_ns + "Domain")
                                  .Where(x => x.Attribute("IsExpired")?.Value == "false"
                                              && x.Attribute("IsLocked")?.Value == "false"
                                              && x.Attribute("IsOurDNS")?.Value == "true")
                                  .Select(x => x.Attribute("Name").Value)
                                  .Select(x => new DnsZone
                                  {
                                      Name = x,
                                      ZoneId = x
                                  })
                                  .ToList();
            }
            catch (Exception exp)
            {
                _log.Error(exp, "Failed to get a batch of domain zones.");

                return new DnsZone[0];
            }
        }

        #region Private helpers

        /// <summary>
        /// Invokes the API method via a GET request, returning the XML results.
        /// </summary>
        private async Task<XElement> InvokeGetApiAsync(string command, Dictionary<string, string> args = null)
        {
            string Encode(string arg) => HttpUtility.UrlEncode(arg);

            args = WithDefaultArgs(command, args);
            var url = API_URL + "?" + string.Join("&", args.Select(kvp => Encode(kvp.Key) + "=" + Encode(kvp.Value)));
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            return await InvokeApiAsync(request);
        }

        /// <summary>
        /// Invokes the API method.
        /// </summary>
        private async Task<XElement> InvokeApiAsync(HttpRequestMessage request)
        {
            var response = await _http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"NameCheap API method {request.RequestUri} returned HTTP Code {response.StatusCode}");

            XElement xml;
            try
            {
                xml = XElement.Parse(content);
            }
            catch (Exception ex)
            {
                throw new Exception($"NameCheap API method {request.RequestUri} returned invalid XML response:\n{content}", ex);
            }

            if(xml.Attribute("Status")?.Value.ToLower() != "ok")
                throw new Exception($"NameCheap API method {request.RequestUri} returned an error status '{xml.Attribute("Status")?.Value}':\n{content}");

            return xml;
        }

        /// <summary>
        /// Populates the argument list with default args.
        /// </summary>
        private Dictionary<string, string> WithDefaultArgs(string command, Dictionary<string, string> args = null)
        {
            if(args == null)
                args = new Dictionary<string, string>();

            args.Add("ApiUser", _apiUser);
            args.Add("ApiKey", _apiKey);
            args.Add("UserName", _apiUser);
            args.Add("Command", "namecheap." + command);
            args.Add("ClientIp", _ip);

            return args;
        }

        #endregion
    }
}
