using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public enum CertAuthorityType
    {
        LetsEncrypt
    }

    public class CertificateAuthority
    {
        public static List<CertificateAuthority> CertificateAuthorities = new List<CertificateAuthority> {
            new CertificateAuthority{
                Id=CertAuthorityType.LetsEncrypt,
                Title ="Let's Encrypt",
                WebsiteURL ="https://letsencrypt.org/",
                PrivacyPolicyURL ="https://letsencrypt.org/privacy/"
            }
        };

        public CertAuthorityType Id { get; set; }
        public string Title { get; set; }

        public string WebsiteURL { get; set; }
        public string PrivacyPolicyURL { get; set; }
    }
}