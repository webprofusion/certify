using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;

namespace Certify.Models.Providers
{
    public class DnsZone
    {
        public string ZoneId { get; set; }
        public string Name { get; set; }
    }

    public class DnsRecord
    {
        public string ZoneId { get; set; }
        public string RecordId { get; set; }

        public string TargetDomainName { get; set; }

        public string RecordType { get; set; } = "TXT";
        public string RootDomain { get; set; }

        public string RecordName { get; set; }
        public string RecordValue { get; set; }
    }

    public interface IDnsProvider
    {
        Task<bool> InitProvider();

        Task<ActionResult> Test();

        Task<ActionResult> CreateRecord(DnsRecord request);

        Task<ActionResult> DeleteRecord(DnsRecord request);

        Task<List<DnsZone>> GetZones();

        int PropagationDelaySeconds { get; }

        string ProviderId { get; }

        string ProviderTitle { get; }

        string ProviderDescription { get; }

        string ProviderHelpUrl { get; }

        List<ProviderParameter> ProviderParameters { get; }
    }
}
