using System;
using System.Collections.Generic;

namespace Certify.Models.Hub
{

    // Helper classes for the component
    public class OidcLoginResponse
    {
        public string AuthUrl { get; set; } = "";
        public string State { get; set; } = "";
    }

    public class OidcCallbackBody
    {
        public string? code { get; set; }
        public string? state { get; set; }
        public string? id_token { get; set; }
        public string? error { get; set; }
        public string? error_description { get; set; }
    }

    public class OidcCallbackResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? ReturnUrl { get; set; }
        public OidcSecurityPrincipal? SecurityPrincipal { get; set; }
    }

    public class OidcAuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public OidcSecurityPrincipal? User { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // Placeholder SecurityPrincipal class - replace with your actual model
    public class OidcSecurityPrincipal
    {
        public string Id { get; set; } = "";
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? Title { get; set; }
        public string? Provider { get; set; }
    }

    // Supporting classes for OIDC
    public class OidcProviderConfig : ConfigurationStoreItem
    {

        public string Authority { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RedirectUri { get; set; } = "";
        public string? ResponseType { get; set; }
        public string? ResponseMode { get; set; }
        public string? Scope { get; set; }
        public string? AuthorizeEndpoint { get; set; }
        public string? TokenEndpoint { get; set; }
        public string? DiscoveryEndpoint { get; set; }
    }

    /// <summary>
    /// Sign in options the hub currently offers, as reported to an unauthenticated client so that it can present
    /// only the methods which will actually work.
    /// </summary>
    public class AuthProviderInfo
    {
        /// <summary>
        /// Configured OpenID Connect providers, keyed by provider id with the display title as the value.
        /// </summary>
        public Dictionary<string, string> OidcProviders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// If false the hub has been configured to accept external (OIDC) sign in only, and the username/password
        /// login endpoint will reject all requests.
        /// </summary>
        public bool IsPasswordLoginEnabled { get; set; } = true;
    }

    public class OidcState
    {
        public string Provider { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string? ReturnUrl { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
