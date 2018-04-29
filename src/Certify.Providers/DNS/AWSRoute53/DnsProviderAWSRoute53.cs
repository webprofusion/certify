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

        public int PropagationDelaySeconds => 60;

        public string ProviderId => "DNS01.API.Route53";

        public string ProviderTitle => "Amazon Route 53 DNS API";

        public string ProviderDescription => "Validates via Route 53 APIs using AMI service credentials";

        public string ProviderHelpUrl => "https://certifytheweb.com/docs/dns/awsroute53";

        public static ProviderDefinition Definition
        {
            get
            {
                return new ProviderDefinition
                {
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS
                    // etc TODO
                };
            }
        }

        public List<ProviderParameter> ProviderParameters =>
                new List<ProviderParameter>{
                    new ProviderParameter{ Key="accesskey",Name="Access Key", IsRequired=true, IsPassword=false },
                    new ProviderParameter{ Key="secretaccesskey",Name="Secret Access Key", IsRequired=true, IsPassword=true }
                };

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

        private async Task<HostedZone> ResolveMatchingZone(DnsRecordRequest request)
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
                    var zones = _route53Client.ListHostedZones();
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

            var recordsetResponse = _route53Client.ChangeResourceRecordSets(recordsetRequest);

            // Monitor the change status
            var changeRequest = new GetChangeRequest()
            {
                Id = recordsetResponse.ChangeInfo.Id
            };

            while (ChangeStatus.PENDING == _route53Client.GetChange(changeRequest).ChangeInfo.Status)
            {
                System.Diagnostics.Debug.WriteLine("DNS change is pending.");
                await Task.Delay(1500);
            }

            System.Diagnostics.Debug.WriteLine("DNS change completed.");

            return true;
        }

        public async Task<ActionResult> CreateRecord(DnsCreateRecordRequest request)
        {
            // https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/route53-apis-intro.html
            // find zone
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                var recordSet = new ResourceRecordSet()
                {
                    Name = request.RecordName,
                    TTL = 5,
                    Type = RRType.TXT,
                    ResourceRecords = new List<ResourceRecord>
                        {
                          new ResourceRecord { Value =  "\""+request.RecordValue+"\""}
                        }
                };

                try
                {
                    var result = await ApplyDnsChange(zone, recordSet, ChangeAction.UPSERT);
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

        public async Task<ActionResult> DeleteRecord(DnsDeleteRecordRequest request)
        {
            var zone = await ResolveMatchingZone(request);

            if (zone != null)
            {
                var recordSet = new ResourceRecordSet()
                {
                    Name = request.RecordName,
                    TTL = 5,
                    Type = RRType.TXT,
                    ResourceRecords = new List<ResourceRecord>
                        {
                          new ResourceRecord { Value = "\""+request.RecordValue+"\""}
                        }
                };

                var result = ApplyDnsChange(zone, recordSet, ChangeAction.DELETE);

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
                    Description = z.Name
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
