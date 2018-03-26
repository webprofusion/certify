using Certify.Models.Config;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Certify.Models.Providers
{
    public class DnsZone
    {
        public string ZoneId { get; set; }
        public string Description { get; set; }
    }

    public class DnsRecordRequest
    {
        public string ZoneId { get; set; }
        public string TargetDomainName { get; set; }
        public string RecordType { get; set; } = "TXT";
    }

    public class DnsCreateRecordRequest : DnsRecordRequest
    {
        public string RecordName { get; set; }
        public string RecordValue { get; set; }
    }

    public class DnsDeleteRecordRequest : DnsRecordRequest
    {
        public string RecordName { get; set; }
        public string RecordValue { get; set; }
    }

    public interface IDnsProvider
    {
        Task<bool> InitProvider();

        Task<ActionResult> CreateRecord(DnsCreateRecordRequest request);

        Task<ActionResult> DeleteRecord(DnsDeleteRecordRequest request);

        Task<List<DnsZone>> GetZones();
    }
}