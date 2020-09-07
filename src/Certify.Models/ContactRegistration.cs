namespace Certify.Models
{
    public class ContactRegistration
    {
        public string EmailAddress { get; set; }

        public bool AgreedToTermsAndConditions { get; set; } = false;

        public string CertificateAuthorityId { get; set; } = StandardCertAuthorities.LETS_ENCRYPT;
        public bool IsStaging { get; set; } = false;
    }
}
