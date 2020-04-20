using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.DNS.TransIP.Authentication;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.TransIP
{
    public class DnsProviderTransIP : DnsProviderBase, IDnsProvider
    {
        internal const string BASE_URI = "https://api.transip.nl/v6/";
        internal const string LIST_DOMAINS_URI = BASE_URI + "domains";
        internal const string RECORD_URI = BASE_URI + "domains/{0}/dns";

        private ILog _log;
        private DnsClient _dnsClient;
        private readonly string _login;
        private readonly string _privateKey;

        private int? _customPropagationDelay = null;
        public int PropagationDelaySeconds => _customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds;

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public static ChallengeProviderDefinition Definition
        {
            get
            {
                return new ChallengeProviderDefinition
                {
                    Id = "DNS01.API.TransIP",
                    Title = "TransIP DNS API",
                    Description = "Validates via TransIP DNS APIs using credentials",
                    HelpUrl = "https://api.transip.eu/rest/docs.html",
                    PropagationDelaySeconds = 300,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="login", Name="User Name", IsRequired=true, IsCredential=true },
                        new ProviderParameter{ Key="privatekey", Name="PrivateKey", IsRequired=true, IsMultiLine=true, IsCredential=true },
                        new ProviderParameter{ Key="propagationdelay",Name="Propagation Delay Seconds", IsRequired=false, IsPassword=false, Value="300", IsCredential=false }
                    },
                    ChallengeType = Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.TransIP",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderTransIP(Dictionary<string, string> credentials)
        {
            _login = credentials["login"];
            _privateKey = credentials["privatekey"];
        }

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await _dnsClient.GetDomains();
                if (!zones.IsSuccess)
                {
                    return zones;
                }

                if (zones != null && zones.Result.Any())
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
                return new ActionResult { IsSuccess = false, Message = $"Test Failed: {exp.Message}" };
            }
        }

        public override async Task<List<DnsZone>> GetZones()
        {
            var zones = await _dnsClient.GetDomains();
            if (!zones.IsSuccess)
            {
                return new List<DnsZone>();
            }

            return zones.Result
                .Select(zone => new DnsZone { ZoneId = zone.name, Name = zone.name })
                .ToList();
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            var test = await Test();
            if (!test.IsSuccess)
            {
                return test;
            }

            var root = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);
            var entry = GetEntry(request, root);
            return await _dnsClient.Add(root.RootDomain, entry);
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var test = await Test();
            if (!test.IsSuccess)
            {
                return test;
            }

            var root = await DetermineZoneDomainRoot(request.RecordName, request.ZoneId);            
            var entry = GetEntry(request, root);
            return await _dnsClient.Remove(root.RootDomain, entry);
        }

        public async Task<bool> InitProvider(Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out int customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            _dnsClient = new DnsClient(_login, _privateKey, PropagationDelaySeconds + 60);

            return await Task.FromResult(true);
        }

        private DTO.DnsEntry GetEntry(DnsRecord request, DnsRecord root) =>
            new DTO.DnsEntry
            {
                content = request.RecordValue,
                expire = 60,
                name = NormaliseRecordName(root, request.RecordName),
                type = "TXT"
            };
    }
}
