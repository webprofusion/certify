namespace Certify.Models
{
    public static class CertIdentifierType
    {
        public static string Dns { get; } = "dns";
        public static string Ip { get; } = "ip";
        public static string TnAuthList { get; } = "TNAuthList";
    }

    public class CertIdentifierItem : IdentifierItem
    {
        public bool IsAuthorizationPending { get; set; }
        public string Status { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Value}";
        }

        public CertIdentifierItem() { }

        public CertIdentifierItem(string type, string domain) : base(type, domain) { }

        public CertIdentifierItem(string domain) : base(domain) { }
    }

    public class IdentifierItem
    {
        public string IdentifierType { get; set; } = CertIdentifierType.Dns;
        public string Value { get; set; } = string.Empty;
        public override string ToString()
        {
            return $"{Value}";
        }
        public IdentifierItem() { }
        public IdentifierItem(string domain)
        {
            Value = domain;
            IdentifierType = CertIdentifierType.Dns;
        }

        public IdentifierItem(string type, string domain)
        {
            IdentifierType = type;
            Value = domain;
        }
    }
}
