using System;
using System.Collections.Generic;

namespace Certify.Models
{
    /// <summary>
    /// Identifies a managed item as the temporary target of a hub Managed ACME order and carries the
    /// ownership/scope information required to fulfil the order (e.g. selecting an accessible managed challenge).
    /// Presence of this object also allows orphaned temporary items to be identified and cleaned up later,
    /// even if the in-memory ACME order state has been lost (e.g. after a service restart).
    /// </summary>
    public class ManagedAcmeOrderInfo
    {
        /// <summary>
        /// The hub ACME order id this managed item was created for
        /// </summary>
        public string? OrderId { get; set; }

        /// <summary>
        /// The ACME account (kid) which submitted the order
        /// </summary>
        public string? AccountKid { get; set; }

        /// <summary>
        /// The security principal which owns the ordering account. Used to determine the scoped access
        /// available when selecting managed challenges etc during a request.
        /// </summary>
        public string? SecurityPrincipalId { get; set; }

        /// <summary>
        /// The assigned role IDs the owning principal is scoped to for this order (e.g. from a scoped API token).
        /// </summary>
        public List<string>? ScopedAssignedRoles { get; set; }

        /// <summary>
        /// When the order (and this temporary managed item) was created
        /// </summary>
        public DateTimeOffset DateCreated { get; set; }
    }
}
