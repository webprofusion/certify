#nullable disable
namespace Certify.Models
{
    public class ManagedCertificateFilter
    {
        public enum SortMode
        {
            /// <summary>
            /// Sort by Name, Ascending
            /// </summary>
            NAME_ASC,
            /// <summary>
            /// Sort by Date Last Renewed or Last Renewal Attempt if not yet renewed, Ascending
            /// </summary>
            RENEWAL_ASC
        }

        public static readonly ManagedCertificateFilter ALL = new() { MaxResults = -1 };

        // optional filter on Id 
        public string Id { get; set; }

        // optional filter on specific name
        public string Name { get; set; }

        // optional keyword to filter name or domains
        public string Keyword { get; set; }

        // filter results to just those sites which will be included in the next auto renewal
        public bool IncludeOnlyNextAutoRenew { get; set; }

        public int MaxResults { get; set; }

        // filter results to items with the given challenge type
        public string ChallengeType { get; set; }

        // filter results to items with the given challenge provider (DNS helper API etc)
        public string ChallengeProvider { get; set; }

        // filter results to items with the given challenge provider credentials (API key usage etc)
        public string StoredCredentialKey { get; set; }

        /// <summary>
        /// Optional page index for paging results
        /// </summary>
        public int? PageIndex { get; set; }

        /// <summary>
        /// Optional page size for paging results
        /// </summary>
        public int? PageSize { get; set; }

        /// <summary>
        /// If true, include items from external cert managers
        /// </summary>
        public bool IncludeExternal { get; set; }

        /// <summary>
        /// If set, returns items with last OCSP check is greater than N hrs.
        /// </summary>
        public int? LastOCSPCheckMins { get; set; }

        /// <summary>
        /// If set, return items with last ACME ARI check greater than N hrs.
        /// </summary>
        public int? LastRenewalInfoCheckMins { get; set; }

        /// <summary>
        /// Optonal description for the current set of filters (e.g. specific tests)
        /// </summary>
        public string FilterDescription { get; set; }

        public SortMode OrderBy { get; set; } = ManagedCertificateFilter.SortMode.NAME_ASC;
    }
}
