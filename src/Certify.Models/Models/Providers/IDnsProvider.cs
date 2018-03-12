using System.Threading.Tasks;

namespace Certify.Models.Providers
{
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

    public class DnsRequestResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
    }

    public interface IDnsProvider
    {
        Task<DnsRequestResult> CreateRecord(DnsCreateRecordRequest request);

        Task<DnsRequestResult> DeleteRecord(DnsDeleteRecordRequest request);
    }
}