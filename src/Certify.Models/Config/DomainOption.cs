namespace Certify.Models
{
    public class DomainOption : BindableBase
    {
        /// <summary>
        /// Domain/IP/value which is a candidate identifier for inclusion. Still called Domain to allow deserialization of existing config.
        /// </summary>
        public string? Domain { get; set; } = string.Empty;

        /// <summary>
        /// If true, this item is the primary subject for the certificate request 
        /// </summary>
        public bool IsPrimaryDomain { get; set; }

        /// <summary>
        /// If false, we are currently skipping this item for the certificate request 
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// If true, domain is not present in site bindings but is implicit by IP/dns wildcard etc and
        /// is being specified manually
        /// </summary>
        public bool IsManualEntry { get; set; }

        public string? Title { get; set; } = string.Empty;

        public string Type { get; set; } = "dns";
    }
}
