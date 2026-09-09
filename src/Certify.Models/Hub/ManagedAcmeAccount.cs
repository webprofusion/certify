using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Summary of an ACME account an ACME client has registered with the hub Managed ACME service, including
    /// the identity it registered against and when it was last used to sign a request.
    /// </summary>
    public class ManagedAcmeAccountSummary
    {
        /// <summary>
        /// Id of the account, as used in the account resource url
        /// </summary>
        public string AccountId { get; set; } = string.Empty;

        /// <summary>
        /// Full account url (the 'kid' the ACME client signs its requests with)
        /// </summary>
        public string AccountUrl { get; set; } = string.Empty;

        /// <summary>
        /// ACME account status (valid, deactivated etc)
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Contacts supplied by the ACME client when it registered
        /// </summary>
        public List<string> Contacts { get; set; } = [];

        /// <summary>
        /// Id of the security principal which owns this account, resolved from the External Account Binding
        /// used to register it. Null for accounts registered before ownership was tracked.
        /// </summary>
        public string? SecurityPrincipalId { get; set; }

        /// <summary>
        /// Display title for the owning security principal, where it can still be resolved
        /// </summary>
        public string? SecurityPrincipalTitle { get; set; }

        /// <summary>
        /// Type of the owning security principal, where it can still be resolved
        /// </summary>
        public SecurityPrincipalType? PrincipalType { get; set; }

        /// <summary>
        /// Id of the API access token whose External Account Binding key registered this account
        /// </summary>
        public string? AccessTokenId { get; set; }

        /// <summary>
        /// Title of the assigned access token the registering EAB key belongs to, where it still exists
        /// </summary>
        public string? AccessTokenTitle { get; set; }

        /// <summary>
        /// Role assignments this account is limited to, as scoped by the access token which registered it
        /// </summary>
        public List<string> ScopedAssignedRoles { get; set; } = [];

        /// <summary>
        /// When the ACME client registered this account
        /// </summary>
        public DateTimeOffset? DateCreated { get; set; }

        /// <summary>
        /// When this account last signed an accepted ACME request, or null if it has not been used since registration
        /// </summary>
        public DateTimeOffset? DateLastUsed { get; set; }
    }
}
