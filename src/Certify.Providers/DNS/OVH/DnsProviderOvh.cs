using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;

namespace Certify.Providers.DNS.OVH
{
    public class DnsProviderOvhProvider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    /// <summary>
    /// OVH DNS API Provider contributed by contributed by https://github.com/laugel and https://github.com/nuklon
    /// </summary>
    public class DnsProviderOvh : DnsProviderBase, IDnsProvider
    {
        private static List<(string, string)> _createdRecords = new List<(string, string)>();

        private ILog _log;
        private Dictionary<string, string> credentials;
        private int? _customPropagationDelay = null;

        public int PropagationDelaySeconds => (_customPropagationDelay != null ? (int)_customPropagationDelay : Definition.PropagationDelaySeconds);

        public string ProviderId => Definition.Id;

        public string ProviderTitle => Definition.Title;

        public string ProviderDescription => Definition.Description;

        public string ProviderHelpUrl => Definition.HelpUrl;

        public bool IsTestModeSupported => Definition.IsTestModeSupported;

        public List<ProviderParameter> ProviderParameters => Definition.ProviderParameters;

        public const string DefaultOvhEndpoint = "https://eu.api.ovh.com/1.0/";

        public static ChallengeProviderDefinition Definition
        {
            get
            {
                return new ChallengeProviderDefinition
                {
                    Id = "DNS01.API.Ovh",
                    Title = "OVH DNS API",
                    Description = "Validates via OVH APIs using credentials generated from the token creation page.",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns/providers/ovh",
                    PropagationDelaySeconds = 120,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{Key=ApplicationKeyParamKey, Name="Application Key", IsRequired=true },
                        new ProviderParameter{Key=ApplicationSecretParamKey, Name="Application Secret", IsRequired=true },
                        new ProviderParameter{Key=ApplicationEndpointParamKey, Name="Endpoint name of OVH API", IsRequired=false,
                                              Description =$"Should be one of the following : {OvhClient.GetAvailableEndpointsAsString()}" },
                        new ProviderParameter{Key=ConsumerKeyParamKey, Name="Consumer Key", IsRequired=true },
                        new ProviderParameter{Key="zoneid", Name="DNS Zone Id", Description="Zone Id is the root domain name e.g. example.com", IsRequired=true, IsPassword=false, IsCredential=false },
                        new ProviderParameter{Key="propagationdelay",Name="Propagation Delay Seconds", IsRequired=false, IsPassword=false, Value="120", IsCredential=false }
                    },
                    ChallengeType = Certify.Models.SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.Ovh",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        private const string ApplicationKeyParamKey = "appkey";
        public string OvhApplicationKey => credentials[ApplicationKeyParamKey];

        private const string ApplicationSecretParamKey = "appsecret";
        public string OvhApplicationSecret => credentials[ApplicationSecretParamKey];

        private const string ApplicationEndpointParamKey = "appendpoint";
        public string OvhApplicationEndpoint => credentials.ContainsKey(ApplicationEndpointParamKey) ? credentials[ApplicationEndpointParamKey] : null;

        private const string ConsumerKeyParamKey = "consumerkey";
        public string OvhConsumerKey => credentials[ConsumerKeyParamKey];

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            long? creationId = null;
            try
            {
                if (!request.RecordName.EndsWith(request.ZoneId, StringComparison.InvariantCultureIgnoreCase))
                {
                    return new ActionResult { IsSuccess = false, Message = $"DNS record creation failed for RecordName={request.RecordName} , because RecordName was expected to end with ZoneId (which is {request.ZoneId})." };
                }

                // record name received as argument : www.qwerty.sampledomain.com received zone id :
                // sampledomain.com required record name by OVH : www.qwerty
                var recordName = request.RecordName.Substring(0, request.RecordName.Length - request.ZoneId.Length - 1);

                var ovh = CreateOvhClient();
                var content = new Dictionary<string, object>
                {
                    { "fieldType", request.RecordType },
                    { "subDomain", recordName },
                    { "target",  request.RecordValue },
                    { "ttl", 1 }
                };
                var recordCreationResult = await ovh.Post<OvhDnsRecord>($"/domain/zone/{request.ZoneId}/record", content);
                creationId = recordCreationResult.Id;
                request.RecordId = creationId.ToString();

                _createdRecords.Add((request.RecordValue, request.RecordId));

                var zoneRefreshResult = ovh.Post($"/domain/zone/{request.ZoneId}/refresh", string.Empty);

                return new ActionResult { IsSuccess = true, Message = $"DNS record \"{request.RecordName}\" added. OVH id : {creationId} ." };
            }
            catch (Exception ex)
            {
                var detail = creationId != null ? $"Record creation was successful (ovh returned id {creationId}) BUT zone refresh failed." : string.Empty;
                return new ActionResult { IsSuccess = false, Message = $"DNS record creation failed for '{request.RecordName}'. {detail} Error was {ex}" };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            try
            {
                var ovh = CreateOvhClient();

                foreach (var createdRecord in _createdRecords)
                {
                    if (createdRecord.Item1.Equals(request.RecordValue, StringComparison.OrdinalIgnoreCase))
                    {
                        request.RecordId = createdRecord.Item2;

                        break;
                    }
                }

                if (!string.IsNullOrEmpty(request.RecordId))
                {
                    _createdRecords.RemoveAll(record => record.Item1 == request.RecordValue);
                }

                var recordDeletionResult = await ovh.Delete<object>($"/domain/zone/{request.ZoneId}/record/{request.RecordId}");
                var zoneRefreshResult = await ovh.Post($"/domain/zone/{request.ZoneId}/refresh", string.Empty);

                return new ActionResult { IsSuccess = true, Message = $"DNS record {request.RecordName} successfully deleted and zone was refreshed." };
            }
            catch (Exception ex)
            {
                return new ActionResult { IsSuccess = false, Message = $"DNS record deletion failed (record is {request.RecordName}). Error was {ex}" };
            }
        }

        public async override Task<List<DnsZone>> GetZones()
        {
            var ovh = CreateOvhClient();
            var result = await ovh.Get<List<string>>("/domain/zone");
            return result.Select(x => new DnsZone()
            {
                Name = x,
                ZoneId = x,
            }).ToList();
        }

        private OvhClient CreateOvhClient()
        {
            return new OvhClient(OvhApplicationEndpoint ?? DefaultOvhEndpoint, OvhApplicationKey, OvhApplicationSecret, OvhConsumerKey);
        }

        public DnsProviderOvh()
        {
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, ILog log = null)
        {
            _log = log;

            this.credentials = credentials;

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out int customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

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
                return new ActionResult { IsSuccess = true, Message = $"Test Failed: {exp}" };
            }
        }
    }
}
