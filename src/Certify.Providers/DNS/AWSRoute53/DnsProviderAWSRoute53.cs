using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Plugins;
using Certify.Models.Providers;
using Certify.Plugins;
using Newtonsoft.Json;

namespace Certify.Providers.DNS.AWSRoute53
{
    public class DnsProviderAWSRoute53Provider : PluginProviderBase<IDnsProvider, ChallengeProviderDefinition>, IDnsProviderProviderPlugin { }

    public class DnsProviderAWSRoute53 : IDnsProvider
    {
        private const int MaxTxtValuesPerRecord = 8;
        private AmazonRoute53Client _route53Client;
        private ILog _log;

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
            Id = "DNS01.API.Route53",
            Title = "Amazon Route 53 DNS API",
            Description = "Validates via Route 53 APIs using IAM credentials. Optional cross-account Assume Role can be configured in stored credentials.",
            HelpUrl = "https://docs.certifytheweb.com/docs/dns/providers/awsroute53",
            PropagationDelaySeconds = 60,
            ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="accesskey",Name="Access Key", IsRequired=true, IsPassword=false },
                        new ProviderParameter{ Key="secretaccesskey",Name="Secret Access Key", IsRequired=true, IsPassword=true },
                        new ProviderParameter{ Key="useassumerole", Name="Assume cross-account role", IsRequired=false, Value="false", Type=OptionType.Boolean, Description="Use the access keys to assume an IAM role in another AWS account, then update Route 53 in that account." },
                        new ProviderParameter{ Key="rolearn", Name="Role ARN", IsRequired=false, IsPassword=false, Description="Target role ARN (e.g. arn:aws:iam::123456789012:role/CertifyRoute53). Required when Assume cross-account role is enabled." },
                        new ProviderParameter{ Key="rolesessionname", Name="Session name", IsRequired=false, IsPassword=false, Value="CertifyTheWeb", Description="Optional STS session name." },
                        new ProviderParameter{ Key="externalid",Name="External ID (optional)", IsRequired=false, IsPassword=true, Description="Optional. Required by the target role trust policy when using cross-account Assume Role." },
                        new ProviderParameter{ Key="zoneid",Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false, Description="Hosted zone ID from Route 53 (e.g. /hostedzone/Z1234567890)." },
                        new ProviderParameter{ Key="propagationdelay",Name="Propagation Delay Seconds", IsRequired=false, IsPassword=false, Value="60", IsCredential=false },
                    },
            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
            Config = "Provider=Certify.Providers.DNS.AWSRoute53",
            HandlerType = ChallengeHandlerType.INTERNAL
        };

        public DnsProviderAWSRoute53()
        {
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
                return new ActionResult { IsSuccess = false, Message = $"Test Failed: {exp.Message}" };
            }
        }

        private async Task<HostedZone> ResolveMatchingZone(DnsRecord request)
        {
            try
            {
                if (!string.IsNullOrEmpty(request.ZoneId))
                {
                    var zone = await _route53Client.GetHostedZoneAsync(new GetHostedZoneRequest { Id = request.ZoneId });
                    return zone.HostedZone;
                }
                else
                {
                    // invalid or empty zone id, don't attempt to fuzzy match
                    return null;
                }
            }
            catch (Exception)
            {
                //TODO: return error in result
                return null;
            }
        }

        private async Task<bool> ApplyDnsChange(HostedZone zone, ResourceRecordSet recordSet, ChangeAction action)
        {
            // prepare change
            var changeDetails = new Change()
            {
                ResourceRecordSet = recordSet,
                Action = action
            };

            var changeBatch = new ChangeBatch()
            {
                Changes = new List<Change> { changeDetails }
            };

            // Update the zone's resource record sets
            var recordsetRequest = new ChangeResourceRecordSetsRequest()
            {
                HostedZoneId = zone.Id,
                ChangeBatch = changeBatch
            };

            _log?.Debug($"Route53 :: ApplyDnsChange : ChangeResourceRecordSetsAsync: {JsonConvert.SerializeObject(recordsetRequest.ChangeBatch)} ");

            var recordsetResponse = await _route53Client.ChangeResourceRecordSetsAsync(recordsetRequest);

            _log?.Debug($"Route53 :: ApplyDnsChange : ChangeResourceRecordSetsAsync Response: {JsonConvert.SerializeObject(recordsetResponse)} ");

            // Monitor the change status
            var changeRequest = new GetChangeRequest()
            {
                Id = recordsetResponse.ChangeInfo.Id
            };

            while (ChangeStatus.PENDING == (await _route53Client.GetChangeAsync(changeRequest)).ChangeInfo.Status)
            {
                System.Diagnostics.Debug.WriteLine("DNS change is pending.");
                await Task.Delay(1500);
            }

            _log?.Information("DNS change completed.");

            return true;
        }

        private static bool IsMatchingTxtRecord(ResourceRecordSet recordSet, string recordName)
        {
            return recordSet != null
                && recordSet.Type == RRType.TXT
                && string.Equals(recordSet.Name?.TrimEnd('.'), recordName?.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        }

        private static string QuoteTxtValue(string value)
        {
            return $"\"{value}\"";
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/route53-apis-intro.html
            // find zone
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                // get existing record set for current TXT records with this name
                var response = await _route53Client.ListResourceRecordSetsAsync(
                    new ListResourceRecordSetsRequest
                    {
                        StartRecordName = request.RecordName,
                        StartRecordType = "TXT",
                        MaxItems = "1",
                        HostedZoneId = zone.Id
                    }
                    );

                var targetRecordSet = response?.ResourceRecordSets?.FirstOrDefault(r => IsMatchingTxtRecord(r, request.RecordName));
                var quotedRecordValue = QuoteTxtValue(request.RecordValue);

                if (targetRecordSet != null)
                {
                    if (targetRecordSet.ResourceRecords?.Any(t => string.Equals(t.Value, quotedRecordValue, StringComparison.Ordinal)) == true)
                    {
                        return new ActionResult { IsSuccess = true, Message = $"Dns Record already exists with required value. Skipping." };
                    }
                    else
                    {
                        var updatedResourceRecords = targetRecordSet.ResourceRecords?
                            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Value))
                            .ToList() ?? new List<ResourceRecord>();

                        if (updatedResourceRecords.Count >= MaxTxtValuesPerRecord)
                        {
                            updatedResourceRecords = updatedResourceRecords
                                .Skip(updatedResourceRecords.Count - (MaxTxtValuesPerRecord - 1))
                                .ToList();
                        }

                        updatedResourceRecords.Add(new ResourceRecord { Value = quotedRecordValue });
                        targetRecordSet.ResourceRecords = updatedResourceRecords;
                    }
                }
                else
                {
                    targetRecordSet = new ResourceRecordSet()
                    {
                        Name = request.RecordName,
                        TTL = 5,
                        Type = RRType.TXT,
                        ResourceRecords = new List<ResourceRecord>
                        {
                          new ResourceRecord { Value = quotedRecordValue }
                        }
                    };
                }

                try
                {
                    // requests for *.domain.com + domain.com use the same TXT record name, so we
                    // need to allow multiple entires rather than doing Upsert
                    var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.UPSERT);

                    return new ActionResult { IsSuccess = true, Message = $"Dns Record Created/Updated: {request.RecordName}" };
                }
                catch (AmazonRoute53Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = $"Dns Record Create/Update: {request.RecordName} - {exp.Message}" };
                }
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Zone match could not be determined." };
            }
        }

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        {
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                _log?.Information($"Route53 :: Delete Record : Zone matched {zone.Id} {zone.Id} : Fetching TXT record set {request.RecordName} ");

                var response = await _route53Client.ListResourceRecordSetsAsync(
                    new ListResourceRecordSetsRequest
                    {
                        StartRecordName = request.RecordName,
                        StartRecordType = "TXT",
                        MaxItems = "1",
                        HostedZoneId = zone.Id
                    }
                );

                var targetRecordSet = response?.ResourceRecordSets?.FirstOrDefault(r => IsMatchingTxtRecord(r, request.RecordName));
                var quotedRecordValue = QuoteTxtValue(request.RecordValue);

                if (targetRecordSet != null)
                {
                    _log?.Information($"Route53 :: Delete Record : Fetched TXT record set OK {targetRecordSet.Name} ");

                    var snapshot = targetRecordSet.ResourceRecords.ToList();
                    var preservedResourceRecords = targetRecordSet.ResourceRecords
                        .Where(r => !string.Equals(r.Value, quotedRecordValue, StringComparison.Ordinal))
                        .ToList();

                    if (preservedResourceRecords.Count == snapshot.Count)
                    {
                        return new ActionResult { IsSuccess = true, Message = $"Dns Record Delete skipped (value does not exist): {request.RecordName}" };
                    }

                    if (preservedResourceRecords.Count == 0)
                    {
                        // no records left, delete the record set
                        try
                        {
                            targetRecordSet.ResourceRecords = snapshot;
                            var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.DELETE);
                            return new ActionResult { IsSuccess = true, Message = $"Dns Record Delete completed: {request.RecordName}" };
                        }
                        catch (AmazonRoute53Exception exp)
                        {
                            return new ActionResult { IsSuccess = false, Message = $"Dns Record Delete failed: {request.RecordName} - {exp.Message}" };
                        }
                    }
                    else
                    {
                        targetRecordSet.ResourceRecords = preservedResourceRecords;

                        try
                        {
                            var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.UPSERT);
                            return new ActionResult { IsSuccess = true, Message = $"Dns Record Removed: {request.RecordName}" };
                        }
                        catch (AmazonRoute53Exception exp)
                        {
                            return new ActionResult { IsSuccess = false, Message = $"Dns Record Remove failed: {request.RecordName} - {exp.Message}" };
                        }
                    }
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = $"Dns Record Delete skipped (record set does not exist): {request.RecordName}" };
                }
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Zone match could not be determined." };
            }
        }

        public async Task<List<DnsZone>> GetZones()
        {

            var results = new List<DnsZone>();
            string pageMarker = null;
            var hasMorePages = true;
            try
            {

                while (hasMorePages)
                {
                    var zones = await _route53Client.ListHostedZonesAsync(new ListHostedZonesRequest { Marker = pageMarker });

                    hasMorePages = zones.IsTruncated ?? false;
                    pageMarker = zones.NextMarker;

                    foreach (var z in zones.HostedZones)
                    {
                        results.Add(new DnsZone
                        {
                            ZoneId = z.Id,
                            Name = z.Name
                        });
                    }
                }
            }
            catch (Exception) { }

            return results;
        }

        private static void ApplyProxySettings(ClientConfig clientConfig, IHttpClientProvider clientProvider, Uri serviceEndpoint)
        {
            var handler = clientProvider.CreateHandler();
            if (handler is HttpClientHandler httpClientHandler && httpClientHandler.Proxy != null)
            {
                var proxyUri = httpClientHandler.Proxy.GetProxy(serviceEndpoint);
                clientConfig.ProxyHost = proxyUri?.Host;
                clientConfig.ProxyPort = proxyUri?.Port ?? 0;

                if (httpClientHandler.Proxy.Credentials != null)
                {
                    clientConfig.ProxyCredentials = httpClientHandler.Proxy.Credentials;
                }
            }
        }

        private static bool IsAssumeRoleEnabled(Dictionary<string, string> credentials, Dictionary<string, string> parameters)
        {
            if (TryGetBoolValue(credentials, "useassumerole", out var useFromCredentials))
            {
                return useFromCredentials;
            }

            return TryGetBoolValue(parameters, "useassumerole", out var useFromParameters) && useFromParameters;
        }

        private static bool TryGetBoolValue(Dictionary<string, string> source, string key, out bool value)
        {
            value = false;
            return source?.ContainsKey(key) == true
                && bool.TryParse(source[key], out value);
        }

        private static bool TryGetConfigValue(
            Dictionary<string, string> credentials,
            Dictionary<string, string> parameters,
            string key,
            out string value)
        {
            if (credentials?.TryGetValue(key, out value) == true && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (parameters?.TryGetValue(key, out value) == true && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            value = null;
            return false;
        }

        private async Task<SessionAWSCredentials> AssumeRoleAsync(
            string accessKey,
            string secretKey,
            IHttpClientProvider clientProvider,
            string roleArn,
            string roleSessionName,
            string externalId)
        {
            var stsConfig = new AmazonSecurityTokenServiceConfig
            {
                RegionEndpoint = RegionEndpoint.USEast1
            };

            ApplyProxySettings(stsConfig, clientProvider, new Uri("https://sts.amazonaws.com"));

            using (var stsClient = new AmazonSecurityTokenServiceClient(accessKey, secretKey, stsConfig))
            {
                var request = new AssumeRoleRequest
                {
                    RoleArn = roleArn,
                    RoleSessionName = roleSessionName,
                    DurationSeconds = 3600
                };

                if (!string.IsNullOrWhiteSpace(externalId))
                {
                    request.ExternalId = externalId;
                }

                _log?.Debug($"Route53 :: AssumeRole : Requesting role {roleArn} with session {roleSessionName}");

                var response = await stsClient.AssumeRoleAsync(request);
                var assumedCredentials = response.Credentials;

                return new SessionAWSCredentials(
                    assumedCredentials.AccessKeyId,
                    assumedCredentials.SecretAccessKey,
                    assumedCredentials.SessionToken);
            }
        }

        public async Task<bool> InitProvider(Dictionary<string, string> credentials, Dictionary<string, string> parameters, IHttpClientProvider clientProvider, ILog log = null)
        {
            _log = log;

            var route53Config = new AmazonRoute53Config
            {
                RegionEndpoint = RegionEndpoint.USEast1
            };

            ApplyProxySettings(route53Config, clientProvider, new Uri("https://route53.amazonaws.com"));

            var accessKey = credentials["accesskey"];
            var secretKey = credentials["secretaccesskey"];

            if (IsAssumeRoleEnabled(credentials, parameters))
            {
                if (!TryGetConfigValue(credentials, parameters, "rolearn", out var roleArn))
                {
                    throw new ArgumentException("Role ARN is required when Assume Role (Cross-Account) is enabled.");
                }

                var roleSessionName = "CertifyTheWeb";
                if (TryGetConfigValue(credentials, parameters, "rolesessionname", out var customSessionName))
                {
                    roleSessionName = customSessionName;
                }

                TryGetConfigValue(credentials, parameters, "externalid", out var externalId);

                var sessionCredentials = await AssumeRoleAsync(
                    accessKey,
                    secretKey,
                    clientProvider,
                    roleArn,
                    roleSessionName,
                    externalId);

                _route53Client = new AmazonRoute53Client(sessionCredentials, route53Config);
                _log?.Information($"Route53 :: Initialized with assumed role {roleArn}");
            }
            else
            {
                _route53Client = new AmazonRoute53Client(accessKey, secretKey, route53Config);
            }

            if (parameters?.ContainsKey("propagationdelay") == true)
            {
                if (int.TryParse(parameters["propagationdelay"], out var customPropDelay))
                {
                    _customPropagationDelay = customPropDelay;
                }
            }

            return true;
        }
    }
}
