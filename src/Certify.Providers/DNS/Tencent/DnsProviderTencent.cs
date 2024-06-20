using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;
using Newtonsoft.Json;
using TencentCloud.Common;
using TencentCloud.Dnspod.V20210323;
using TencentCloud.Dnspod.V20210323.Models;

namespace Certify.Providers.DNS.Tencent
{

    public class DnsProviderTencentProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    /// <summary>
    /// Tencent Cloud DNS API Provider contributed by https://github.com/xiaotiannet
    /// 20240620 
    /// </summary>
    /// 
    public class DnsProviderTencent : DnsProviderBase, IDnsProvider
    {
        private ILog _log ;
        //public ILog log=> _log?? new TraceLog();
        private string _accessKeyId;
        private string _accessKeySecret;

        private int? _customPropagationDelay = null;
        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition => new ChallengeProviderDefinition
        {
            Id = "DNS01.API.Tencent.DNSPod_v3",
            Title = "DNSPod (v3) (Tencent DNS API 3.0)",
            Description = "Validates via Tencent DNS APIs using api key and secret. ",
            HelpUrl = "https://cloud.tencent.com/document/api/1427",
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
                            IsRequired = false,
                            IsPassword = false,
                            IsCredential = false
                        },
                        new ProviderParameter{
                            Key="propagationdelay",
                            Name="Propagation Delay Seconds",
                            IsRequired=false, 
                            IsPassword=false,
                            Value="120",
                            IsCredential=false
                        }
                    },
            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.Tencent",
            HandlerType = ChallengeHandlerType.INTERNAL
        };
        public DnsProviderTencent()
        {
           

        }

        public async Task<ActionResult> Test()
        {
            //log?.Debug($"Test"); 
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
                //log?.Error($"Test,{exp}");
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = $"Test Failed: {exp.Message}"
                };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            //log?.Debug($"CreateRecord,request:{JsonConvert.SerializeObject(request)}");
            try
            {
                var domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);
                if (string.IsNullOrEmpty(domainInfo.RootDomain))
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
                }

                var rootDomain = domainInfo.RootDomain;
                var rr = NormaliseRecordName(domainInfo, request.RecordName);

                await AddDomainRecord(rootDomain, rr, RecordType.TXT, request.RecordValue);
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record added."
                };
            }
            catch (Exception exp)
            {

                //log?.Error($"CreateRecord,{exp}");
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = exp.Message
                };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            //log?.Debug($"DeleteRecord,request:{JsonConvert.SerializeObject(request)}");

            try
            {
                var domainInfo = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);
                if (string.IsNullOrEmpty(domainInfo.RootDomain))
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to determine root domain in zone." };
                }

                var rootDomain = domainInfo.RootDomain;
                var rr = NormaliseRecordName(domainInfo, request.RecordName);
                var records = await GetDnsRecords(rootDomain);
                var targetRecord = records.First(c => c.Type == "TXT" && c.Name == rr);//throw if not exists
                await DeleteDomainRecord(rootDomain,targetRecord.RecordId);
                return new ActionResult
                {
                    IsSuccess = true,
                    Message = "DNS record deleted."
                };
            }
            catch (Exception ex)
            {
                //log?.Error($"DeleteRecord,{ex}");
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not delete record {request.RecordName}. Message: {ex.Message}"
                };
            }
        }

        private async Task<List<RecordListItem>> GetDnsRecords(string domainName)
        {
            var records = new List<RecordListItem>();
            var finishedPaginating = false;
            var page = 1;

            while (!finishedPaginating)
            {
                var result = await GetDomainRecords(domainName, page);

                if (result != null)
                {
                    records.AddRange(result.RecordList);
                    var cnt = result.RecordCountInfo;
                    var totalPage = (int)Math.Ceiling((int)cnt.TotalCount / (double)cnt.ListCount);
                    if (page == totalPage)
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

        public override async Task<List<DnsZone>> GetZones()
        {
            //TODO does Tencent really have Zones?
            var zones = new List<DnsZone>();
            var finishedPaginating = false;
            var page = 1;

            while (!finishedPaginating)
            {
                var result = await GetDomains(page);
                if (result != null)
                {
                    if (result.DomainList.Length == 0)
                    {
                        break;
                    }
                    foreach (var z in result.DomainList)
                    {
                        zones.Add(new DnsZone
                        {
                            ZoneId = z.DomainId.ToString(),
                            Name = z.Name
                        });
                    }

                    var cnt = result.DomainCountInfo;
                    var totalPage = (int)Math.Ceiling((int)cnt.AllTotal / (double)cnt.DomainTotal);
                    if (page == totalPage)
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

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;
            //_log = new TraceLog(log);
            //_log?.Information("InitProvider");
            _accessKeyId = credentials["accesskeyid"];
            _accessKeySecret = credentials["accesskeysecret"];

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return await Task.FromResult(true);
        }

        #region AliMethods

        /// <summary>
        /// Add Tencent DNS Record
        /// </summary>
        /// <param name="domainName"> Domain name </param>
        /// <param name="rr"> @.exmaple.com =&gt; @ </param>
        /// <param name="type"> A/NS/MX/TXT/CNAME/SRV/AAAA/CAA/REDIRECT_URL/FORWARD_URL </param>
        /// <param name="value"> Value </param>
        /// <param name="ttl"> Default 600 sec </param>
        /// <param name="priority"> Default 0(1-10 when type is MX) </param>
        /// <param name="line"> default </param>
        /// <returns>  </returns>
        private async Task<CreateRecordResponse> AddDomainRecord(string domainName, string rr, RecordType type, string value, long ttl = 600, long priority = 0, string line = "默认")
        {
            if (string.IsNullOrEmpty(domainName))
            {
                throw new ArgumentNullException(nameof(domainName));
            }
            if (string.IsNullOrEmpty(rr))
            {
                throw new ArgumentNullException(nameof(rr));
            }
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }
           

            Credential cred = new Credential
            {
                SecretId = _accessKeyId,
                SecretKey = _accessKeySecret
            };
            var client = new DnspodClient(cred, "");
            var req = new CreateRecordRequest()
            {
                Domain = domainName,
                RecordType = type.ToString(),
                RecordLine = line,
                Value = value,
                SubDomain = rr,
                TTL = (ulong)ttl, 
            };

            if (type == RecordType.MX)
            {
                if (priority < 1 || priority > 10)
                {
                    throw new Exception("priority must in 1 to 10 when type is MX");
                }

                req.MX = (ulong)priority; 
            }

            //log?.Debug($"CreateRecordRequest:{JsonConvert.SerializeObject(req)}");
            return await client.CreateRecord(req);

        }

        private async Task<DeleteRecordResponse> DeleteDomainRecord(string domain, ulong? recordId)
        {
            Credential cred = new Credential
            {
                SecretId = _accessKeyId,
                SecretKey = _accessKeySecret
            };
            var client = new DnspodClient(cred, "");
            var req = new DeleteRecordRequest()
            {
                Domain = domain,
                RecordId = recordId,
            };
            //log?.Debug($"DeleteRecordRequest:{JsonConvert.SerializeObject(req)}");
            return await client.DeleteRecord(req);

        }

        private async Task<DescribeRecordListResponse> GetDomainRecords(string domain, int pageNumber = 1, int pageSize = 100)
        {
            Credential cred = new Credential
            {
                SecretId = _accessKeyId,
                SecretKey = _accessKeySecret
            };
            var client = new DnspodClient(cred, "");
            var req = new DescribeRecordListRequest()
            {
                Domain = domain,
                Offset = (ulong)pageNumber-1,
                Limit = (ulong)pageSize
            };
            //log?.Debug($"DescribeRecordListRequest:{JsonConvert.SerializeObject(req)}");
            return await client.DescribeRecordList(req);
        }

        private async Task<DescribeDomainListResponse> GetDomains(int pageNumber = 1, int pageSize = 100)
        {
            Credential cred = new Credential
            {
                SecretId = _accessKeyId,
                SecretKey = _accessKeySecret
            };
            var client = new DnspodClient(cred, "ap-guangzhou");
            var req = new DescribeDomainListRequest()
            {
                Offset = (long)pageNumber-1,
                Limit = (long)pageSize
            };
            //log?.Debug($"DescribeDomainListRequest:{JsonConvert.SerializeObject(req)}");
            return await client.DescribeDomainList(req);

        }
        #endregion AliMethods
    }
}
