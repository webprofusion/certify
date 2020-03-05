namespace Certify.Models
{
    public class ContactRegistration
    {
        public string EmailAddress { get; set; }

        public bool AgreedToTermsAndConditions { get; set; }

        public string CertificateAuthorityId { get; set;}
        public bool IsStaging { get; set; }
    }
}
