using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.Aliyun
{
    /// <summary>
    /// Alibaba Cloud DNS API Provider contributed by https://github.com/TkYu
    /// </summary>
    public class DnsProviderAliyun : IDnsProvider
    {
        private ILog _log;

        private readonly string _accessKeyId;
        private readonly string _accessKeySecret;

        public int PropagationDelaySeconds => Definition.PropagationDelaySeconds;

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.Aliyun",
            Title = "Aliyun (Alibaba Cloud) DNS API",
            Description = "Validates via Aliyun DNS APIs using api key and secret",
            HelpUrl = "https://help.aliyun.com/document_detail/29739.html",
            PropagationDelaySeconds = 120,
            ProviderParameters = new List<ProviderParameter>
                    {
                        new ProviderParameter
                        {
                            Key = "accesskeyid",
                            Name = "Access Key ID",
                            IsRequired = true,
                            IsPassword = false
                        },
                        new ProviderParameter
                        {
                            Key = "accesskeysecret",
                            Name = "Access Key Secret",
                            IsRequired = true,
                            IsPassword = true
                        },
                        new ProviderParameter
                        {
                            Key = "zoneid",
                            Name = "DNS Zone Id",
                            IsRequired = true,
                            IsPassword = false,
                            IsCredential = false
                        }
                    },
            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.Aliyun",
            HandlerType = ChallengeHandlerType.INTERNAL
        };

        public DnsProviderAliyun(Dictionary<string, string> credentials)
        {
            _accessKeyId = credentials["accesskeyid"];
            _accessKeySecret = credentials["accesskeysecret"];
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await GetZones();

                if (zones != null && zones.Any())
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = "Test Completed OK."
                    };
                }
                else
                {
                    return new ActionResult
                    {
                        IsSuccess = true,
                        Message = "Test completed, but no zones returned."
                    };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = $"Test Failed: {exp.Message}"
                };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            try
            {
                var rootDomain = request.TargetDomainName.Replace("*.", "");
                var rr = request.RecordName.Replace(rootDomain, "").TrimEnd('.');
                await AddDomainRecord(rootDomain, rr, RecordType.TXT, request.RecordValue);
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record added."
                };
            }
            catch (Exception exp)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = exp.Message
                };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            try
            {
                var rootDomain = request.TargetDomainName.Replace("*.", "");
                var rr = request.RecordName.Replace(rootDomain, "").TrimEnd('.');
                var records = await GetDnsRecords(rootDomain);
                var targetRecord = records.First(c => c.Type == "TXT" && c.RR == rr);//throw if not exists
                await DeleteDomainRecord(targetRecord.RecordId);
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record deleted."
                };
            }
            catch (Exception ex)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not delete record {request.RecordName}. Message: {ex.Message}"
                };
            }
        }

        private async Task<List<Record>> GetDnsRecords(string domainName)
        {
            var records = new List<Record>();
            var finishedPaginating = false;
            var page = 1;

            while (!finishedPaginating)
            {
                var result = await GetDomainRecords(domainName, page);

                if (result != null)
                {
                    records.AddRange(result.DomainRecords.Record);
                    var totalPage = (int)Math.Ceiling(result.TotalCount / (double)result.PageSize);
                    if (result.PageNumber == totalPage)
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
                    throw new Exception($"Could not get DNS records for domain {domainName}.");
                }
            }

            return records;
        }

        public async Task<List<DnsZone>> GetZones()
        {
            //TODO does aliyun really have Zones?
            var zones = new List<DnsZone>();
            var finishedPaginating = false;
            var page = 1;

            while (!finishedPaginating)
            {
                var result = await GetDomains(page);
                if (result != null)
                {
                    foreach (var z in result.Domains.Domain)
                    {
                        zones.Add(new DnsZone
                        {
                            ZoneId = z.DomainId,
                            Name = z.DomainName
                        });
                    }

                    var totalPage = (int)Math.Ceiling(result.TotalCount / (double)result.PageSize);
                    if (result.PageNumber == totalPage)
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

        #region AliMethods

        /// <summary>
        /// Add Aliyun DNS Record
        /// </summary>
        /// <param name="domainName"> Domain name </param>
        /// <param name="rr"> @.exmaple.com =&gt; @ </param>
        /// <param name="type"> A/NS/MX/TXT/CNAME/SRV/AAAA/CAA/REDIRECT_URL/FORWARD_URL </param>
        /// <param name="value"> Value </param>
        /// <param name="ttl"> Default 600 sec </param>
        /// <param name="priority"> Default 0(1-10 when type is MX) </param>
        /// <param name="line"> default </param>
        /// <returns>  </returns>
        private async Task<DomainRecord> AddDomainRecord(string domainName, string rr, RecordType type, string value, long ttl = 600, long priority = 0, string line = "default")
        {
            var parameters = new Dictionary<string, string>
            {
                {"Action", "AddDomainRecord"},
                {"DomainName", domainName},
                {"RR", rr},
                {"Type", type.ToString()},
                {"Value", value},
                {"TTL", ttl.ToString()},
                {"Line", line}
            };
            if (type == RecordType.MX)
            {
                if (priority < 1 || priority > 10)
                {
                    throw new Exception("priority must in 1 to 10 when type is MX");
                }

                parameters.Add("Priority", priority.ToString());
            }

            var request = new AliDnsRequest(HttpMethod.Get, _accessKeyId, _accessKeySecret, parameters);
            var url = request.GetUrl();
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<DomainRecord>(content);
            }
        }

        private async Task<DomainRecord> DeleteDomainRecord(string recordId)
        {
            var parameters = new Dictionary<string, string>
            {
                {"Action", "DeleteDomainRecord"},
                {"RecordId", recordId}
            };
            var request = new AliDnsRequest(HttpMethod.Get, _accessKeyId, _accessKeySecret, parameters);
            var url = request.GetUrl();
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<DomainRecord>(content);
            }
        }

        private async Task<DescribeDomainRecords> GetDomainRecords(string domain, int pageNumber = 1, int pageSize = 20)
        {
            var parameters = new Dictionary<string, string>
            {
                {"Action", "DescribeDomainRecords"},
                {"PageNumber", pageNumber.ToString()},
                {"PageSize", pageSize.ToString()},
                {"DomainName", domain}
            };
            var request = new AliDnsRequest(HttpMethod.Get, _accessKeyId, _accessKeySecret, parameters);
            var url = request.GetUrl();
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<DescribeDomainRecords>(content);
            }
        }

        private async Task<DescribeDomainsResponse> GetDomains(int pageNumber = 1, int pageSize = 20)
        {
            var parameters = new Dictionary<string, string>()
            {
                {"Action", "DescribeDomains"},
                {"PageNumber", pageNumber.ToString()},
                {"PageSize", pageSize.ToString()}
            };
            var request = new AliDnsRequest(HttpMethod.Get, _accessKeyId, _accessKeySecret, parameters);
            var url = request.GetUrl();
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<DescribeDomainsResponse>(content);
            }
        }

        #endregion AliMethods
    }
}
