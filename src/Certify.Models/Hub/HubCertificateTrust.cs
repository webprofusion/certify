using System;

namespace Certify.Models.Hub
{
    /// <summary>
    /// Whether this instance validates the management hub's own certificate when it calls the hub (hub API, hub
    /// connection and managed challenges). Off by default, because a hub is commonly reached over a private CA or a
    /// self signed certificate
    /// </summary>
    public static class HubCertificateTrust
    {
        /// <summary>
        /// Environment variable which, set to "true", requires the hub to present a certificate this machine already trusts
        /// </summary>
        public const string RequireTrustedVariable = "CERTIFY_MANAGEMENT_HUB_REQUIRE_TRUSTED";

        public static bool IsTrustedCertificateRequired => Environment.GetEnvironmentVariable(RequireTrustedVariable) == "true";
    }
}
