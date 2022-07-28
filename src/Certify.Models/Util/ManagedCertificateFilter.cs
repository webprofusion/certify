﻿#nullable disable
namespace Certify.Models
{
    public class ManagedCertificateFilter
    {
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

        public bool IncludeExternal { get; set; }
    }
}
