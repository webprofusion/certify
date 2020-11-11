using System;
using System.Collections.Generic;
using System.Text;

namespace Certify.Models
{
    public class AccountDetails
    {
        public string StorageKey { get; set; }
        public string ID { get; set; }
        public string Title { get; set; }

        public string CertificateAuthorityId { get; set; }
        public bool IsStagingAccount { get; set; } = false;

        public string Email { get; set; }
        public string AccountURI { get; set; }
        public string AccountKey { get; set; }
        public string EabKeyId { get; set; }
        public string EabKey { get; set; }
        public string EabKeyAlgorithm { get; set; }
        public string PreferredChain { get; set; }
    }
}
