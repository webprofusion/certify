using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Certify.Models
{
    public class AccountDetails
    {
        public string? StorageKey { get; set; }
        public string? ID { get; set; }
        public string Title { get; set; } = string.Empty;

        public string? CertificateAuthorityId { get; set; }
        public bool IsStagingAccount { get; set; }

        public string Email { get; set; } = string.Empty;
        public string AccountURI { get; set; } = string.Empty;
        public string AccountKey { get; set; } = string.Empty;
        public string AccountFingerprint { get; set; } = string.Empty;
        public string? EabKeyId { get; set; }
        public string? EabKey { get; set; }
        public string? EabKeyAlgorithm { get; set; }
        public string? PreferredChain { get; set; }

        /// <summary>
        /// If true, account was selected based on fallback/failover criteria
        /// </summary>
        [JsonIgnore]
        public bool IsFailoverSelection { get; set; }

        /// <summary>
        /// Clear the account private key and EAB key, which only the instance holding the account needs
        /// </summary>
        public void ClearSecrets()
        {
            AccountKey = string.Empty;
            EabKey = null;
        }

        /// <summary>
        /// The account a certificate request uses for each CA, in production and staging: the first account held for
        /// each, as the request picks it. Accounts for a CA which is not configured are left out, as no request uses them.
        /// </summary>
        /// <param name="knownCertificateAuthorityIds">the configured CAs, or null or empty to keep every CA</param>
        public static List<AccountDetails> GetRequestAccounts(IEnumerable<AccountDetails>? accounts, IEnumerable<string>? knownCertificateAuthorityIds)
        {
            var known = new HashSet<string>(knownCertificateAuthorityIds?.Where(id => !string.IsNullOrEmpty(id)) ?? Enumerable.Empty<string>());

            return (accounts ?? Enumerable.Empty<AccountDetails>())
                .Where(a => !string.IsNullOrEmpty(a.AccountURI) && (known.Count == 0 || known.Contains(a.CertificateAuthorityId ?? string.Empty)))
                .GroupBy(a => (a.CertificateAuthorityId, a.IsStagingAccount))
                .Select(g => g.First())
                .ToList();
        }
    }
}
