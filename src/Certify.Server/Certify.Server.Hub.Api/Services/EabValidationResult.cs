using System.Collections.Generic;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Result of an External Account Binding (EAB) validation attempt
    /// </summary>
    public class EabValidationResult
    {
        /// <summary>
        /// True if the external account binding was successfully validated
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// If validation failed, a description of the reason for the failure
        /// </summary>
        public string? FailureReason { get; set; }

        /// <summary>
        /// Internal id of the matching access token (when valid)
        /// </summary>
        public string? TokenInternalId { get; set; }

        /// <summary>
        /// Id of the security principal owning the matched access token (when valid)
        /// </summary>
        public string? SecurityPrincipalId { get; set; }

        /// <summary>
        /// Assigned-role ids scoped on the matched access token (when valid)
        /// </summary>
        public List<string> ScopedAssignedRoles { get; set; } = [];

        public static EabValidationResult Success(string tokenInternalId, string securityPrincipalId, List<string>? scopedAssignedRoles = null) => new EabValidationResult
        {
            IsValid = true,
            TokenInternalId = tokenInternalId,
            SecurityPrincipalId = securityPrincipalId,
            ScopedAssignedRoles = scopedAssignedRoles ?? []
        };

        public static EabValidationResult Failed(string failureReason) => new EabValidationResult
        {
            IsValid = false,
            FailureReason = failureReason
        };
    }
}
