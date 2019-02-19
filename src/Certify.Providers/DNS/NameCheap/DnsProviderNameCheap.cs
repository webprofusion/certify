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

        private const string API_URL = "https://api.namecheap.com/xml.response";
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
                PropagationDelaySeconds = 180,
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
        public static ProviderDefinition Definition { get; }

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;
        public string ProviderId => Definition.Id;
        public string ProviderTitle => Definition.Title;
        public string ProviderDescription => Definition.Description;
        public string ProviderHelpUrl => Definition.HelpUrl;
        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        #endregion

        #region IDnsProvider method implementation

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
            try
            {
                var hosts = await GetHosts(request.TargetDomainName);

                var recordName = request.RecordName.Replace(ParseDomain(request.TargetDomainName).Mask, "");
                hosts.Add(new NameCheapHostRecord
                {
                    Name = recordName,
                    Address = request.RecordValue,
                    Type = request.RecordType,
                    Ttl = 60
                });

                await SetHosts(request.TargetDomainName, hosts);

                return new ActionResult
                {
                    IsSuccess = true,
                    Message = $"DNS record added: {request.RecordName}"
                };
            }
            catch (Exception ex)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Failed to create DNS record {request.RecordName}: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Removes a DNS record from the list.
        /// </summary>
        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            try
            {
                var hosts = await GetHosts(request.TargetDomainName);
                var recordName = request.RecordName.Replace(ParseDomain(request.TargetDomainName).Mask, "");
                var toRemove = hosts.FirstOrDefault(x => x.Name == recordName && x.Address == request.RecordValue);
                if (toRemove == null)
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = $"DNS record {request.RecordName} does not exist, nothing to remove."
                    };
                }

                hosts.Remove(toRemove);
                await SetHosts(request.TargetDomainName, hosts);

                return new ActionResult
                {
                    IsSuccess = true,
                    Message = $"DNS record removed: {request.RecordName}"
                };
            }
            catch (Exception ex)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Failed to remove DNS record {request.RecordName}: {ex.Message}"
                };
            }
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

        #endregion

        #region Private helpers

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
                                  .Where(x => x.Attr<bool>("IsExpired") == false
                                              && x.Attr<bool>("IsLocked") == false
                                              && x.Attr<bool>("IsOurDNS") == true)
                                  .Select(x => x.Attr<string>("Name"))
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

        /// <summary>
        /// Returns the list of hosts for a domain.
        /// </summary>
        private async Task<List<NameCheapHostRecord>> GetHosts(string domain)
        {
            var domainInfo = ParseDomain(domain);

            var xml = await InvokeGetApiAsync("domains.dns.getHosts", new Dictionary<string, string>
            {
                ["SLD"] = domainInfo.SLD,
                ["TLD"] = domainInfo.TLD
            });

            return xml.Element(_ns + "CommandResponse")
                      .Descendants(_ns + "host")
                      .Select(x => new NameCheapHostRecord
                      {
                          Address = x.Attr<string>("Address"),
                          Name = x.Attr<string>("Name"),
                          Type = x.Attr<string>("Type"),
                          HostId = x.Attr<int>("HostId"),
                          MxPref = x.Attr<int>("MXPref"),
                          Ttl = x.Attr<int>("TTL"),
                      })
                      .ToList();
        }

        /// <summary>
        /// Updates the list of hosts in the domain.
        /// </summary>
        private async Task SetHosts(string domain, IReadOnlyList<NameCheapHostRecord> hosts)
        {
            var domainInfo = ParseDomain(domain);

            var args = WithDefaultArgs("domains.dns.setHosts", new Dictionary<string, string>
            {
                ["SLD"] = domainInfo.SLD,
                ["TLD"] = domainInfo.TLD
            });

            var idx = 1;
            foreach (var host in hosts)
            {
                args["HostName" + idx] = host.Name;
                args["RecordType" + idx] = host.Type;
                args["Address" + idx] = host.Address;
                args["MXPref" + idx] = host.MxPref.ToString();
                args["TTL" + idx] = host.Ttl.ToString();

                idx++;
            }

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(API_URL),
                Content = new FormUrlEncodedContent(args)
            };

            await InvokeApiAsync(request);
        }

        #endregion

        #region API invocation

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

        /// <summary>
        /// Returns the parsed domain name from forms like "blabla.com" and "*.blabla.com".
        /// </summary>
        private ParsedDomain ParseDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain))
                throw new ArgumentException("Domain was not specified!");

            var parts = domain.Split('.');
            if (parts.Length < 2)
                throw new ArgumentException($"The value '{domain}' is not a valid domain.");

            return new ParsedDomain
            {
                TLD = parts[parts.Length - 1],
                SLD = parts[parts.Length - 2]
            };
        }

        private class ParsedDomain
        {
            public string TLD;
            public string SLD;

            public string Mask => $".{SLD}.{TLD}";
        }

        #endregion
    }
}
