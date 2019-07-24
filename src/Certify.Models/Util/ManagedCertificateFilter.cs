namespace Certify.Models
{
    public class ManagedCertificateFilter
    {
        // optional file on specific name
        public string Name { get; set; }

        // optional keyword to filter name or domains
        public string Keyword { get; set; }

        // filter results to just those sites which will be included in the next auto renewal
        public bool IncludeOnlyNextAutoRenew { get; set; }

        public int MaxResults { get; set; } = 0;

        // filter results to items with the given challenge type
        public string ChallengeType { get; set; }

        // filter results to items with the given challenge provider (DNS helper API etc)
        public string ChallengeProvider { get; set; }

        // filter results to items with the given challenge provider credentials (API key usage etc)
        public string StoredCredentialKey { get; set; }
    }
}
