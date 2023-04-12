﻿namespace Certify.Models
{
    public class ContactRegistration
    {
        public string? StorageKey { get; set; }

        public string? EmailAddress { get; set; } = string.Empty;

        public bool AgreedToTermsAndConditions { get; set; }

        public string CertificateAuthorityId { get; set; } = StandardCertAuthorities.LETS_ENCRYPT;
        public bool IsStaging { get; set; }

        public string? EabKeyId { get; set; }
        public string? EabKey { get; set; }
        public string? EabKeyAlgorithm { get; set; }
        public string? PreferredChain { get; set; }
    }
}
