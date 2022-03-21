namespace Certify.Models
{
    public class IPAddressOption
    {
        public string Description { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public bool IsIPv6 { get; set; }
    }
}
