using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Route53;
using Amazon.Route53.Model;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers.DNS.AWSRoute53
{
    public class DnsProviderAWSRoute53 : IDnsProvider
    {
        private AmazonRoute53Client _route53Client;

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
                    Id = "DNS01.API.Route53",
                    Title = "Amazon Route 53 DNS API",
                    Description = "Validates via Route 53 APIs using IAM service credentials",
                    HelpUrl = "https://docs.certifytheweb.com/docs/dns-awsroute53.html",
                    PropagationDelaySeconds = 60,
                    ProviderParameters = new List<ProviderParameter>{
                        new ProviderParameter{ Key="accesskey",Name="Access Key", IsRequired=true, IsPassword=false },
                        new ProviderParameter{ Key="secretaccesskey",Name="Secret Access Key", IsRequired=true, IsPassword=true },
                        new ProviderParameter{ Key="zoneid",Name="DNS Zone Id", IsRequired=true, IsPassword=false, IsCredential=false }
                    },
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Config = "Provider=Certify.Providers.DNS.AWSRoute53",
                    HandlerType = ChallengeHandlerType.INTERNAL
                };
            }
        }

        public DnsProviderAWSRoute53(Dictionary<string, string> credentials)
        {
            _route53Client = new AmazonRoute53Client(credentials["accesskey"], credentials["secretaccesskey"], Amazon.RegionEndpoint.USEast1);
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

        private async Task<HostedZone> ResolveMatchingZone(DnsRecord request)
        {
            try
            {
                if (!String.IsNullOrEmpty(request.ZoneId))
                {
                    var zone = await _route53Client.GetHostedZoneAsync(new GetHostedZoneRequest { Id = request.ZoneId });
                    return zone.HostedZone;
                }
                else
                {
                    var zones = await _route53Client.ListHostedZonesAsync();
                    var zone = zones.HostedZones.Where(z => z.Name.Contains(request.TargetDomainName)).FirstOrDefault();
                    return zone;
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

            var recordsetResponse = await _route53Client.ChangeResourceRecordSetsAsync(recordsetRequest);

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

            System.Diagnostics.Debug.WriteLine("DNS change completed.");

            return true;
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/route53-apis-intro.html
            // find zone
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                // get existing record set for current TXT records with this name
                ListResourceRecordSetsResponse response = await _route53Client.ListResourceRecordSetsAsync(
                    new ListResourceRecordSetsRequest {
                        StartRecordName = request.RecordName,
                        StartRecordType = "TXT",
                        MaxItems = "1",
                        HostedZoneId = zone.Id
                    }
                    );
                var targetRecordSet = response.ResourceRecordSets.FirstOrDefault();

                if (targetRecordSet != null)
                {
                    targetRecordSet.ResourceRecords.Add(
                          new ResourceRecord { Value = "\"" + request.RecordValue + "\"" }
                        );
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
                          new ResourceRecord { Value =  "\""+request.RecordValue+"\""}
                        }
                    };
                }

                try
                {
                    // requests for *.domain.com + domain.com use the same TXT record name, so we need to allow multiple entires rather than doing Upsert
                    var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.UPSERT);
                }
                catch (Exception exp)
                {
                    new ActionResult { IsSuccess = false, Message = exp.InnerException.Message };
                }

                return new ActionResult { IsSuccess = true, Message = "Success" };
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
                var response = await _route53Client.ListResourceRecordSetsAsync(
                    new ListResourceRecordSetsRequest
                    {
                        StartRecordName = request.RecordName,
                        StartRecordType = "TXT",
                        MaxItems = "1",
                        HostedZoneId = zone.Id
                    }
                    );
                var targetRecordSet = response.ResourceRecordSets.FirstOrDefault();

                var result = await ApplyDnsChange(zone, targetRecordSet, ChangeAction.DELETE);

                return new ActionResult { IsSuccess = true, Message = "Success" };
            }
            else
            {
                return new ActionResult { IsSuccess = false, Message = "DNS Zone match could not be determined." };
            }
        }

        public async Task<List<DnsZone>> GetZones()
        {
            var zones = await _route53Client.ListHostedZonesAsync();

            List<DnsZone> results = new List<DnsZone>();
            foreach (var z in zones.HostedZones)
            {
                results.Add(new DnsZone
                {
                    ZoneId = z.Id,
                    Name = z.Name
                });
            }

            return results;
        }

        public async Task<bool> InitProvider()
        {
            return await Task.FromResult(true);
        }
    }
}
