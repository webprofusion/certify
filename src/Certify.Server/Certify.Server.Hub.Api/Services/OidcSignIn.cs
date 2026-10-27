using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Certify.Models.Hub;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Protocol side of OIDC sign in: the authorization code flow with PKCE, using the endpoints, issuer and signing keys
    /// published in the provider's discovery document.
    /// </summary>
    internal static class OidcSignIn
    {
        /// <summary>
        /// Placeholder in the issuer published by Entra multi-tenant authorities (common, organizations), where each token is
        /// issued by the signed in user's own tenant.
        /// </summary>
        private const string TenantIdPlaceholder = "{tenantid}";

        /// <summary>
        /// Entra serves v1 metadata at a bare tenant authority (https://sts.windows.net issuer, /oauth2/token), but the hub has
        /// always used the v2 endpoints, so an Entra authority without the /v2.0 suffix is given it for discovery.
        /// </summary>
        private static readonly string[] EntraHosts = ["login.microsoftonline.com", "login.microsoftonline.us", "login.partner.microsoftonline.cn"];

        private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _configurationManagers = new();

        public static string GetDiscoveryEndpoint(OidcProviderConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.DiscoveryEndpoint))
            {
                return config.DiscoveryEndpoint;
            }

            var authority = config.Authority.TrimEnd('/');

            if (Uri.TryCreate(authority, UriKind.Absolute, out var uri)
                && EntraHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
                && !authority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase))
            {
                authority += "/v2.0";
            }

            return $"{authority}/.well-known/openid-configuration";
        }

        /// <summary>
        /// One configuration manager per discovery document, so the document and signing keys are cached and refreshed
        /// periodically instead of being fetched on every sign in.
        /// </summary>
        public static ConfigurationManager<OpenIdConnectConfiguration> GetConfigurationManager(OidcProviderConfig config)
        {
            return _configurationManagers.GetOrAdd(
                GetDiscoveryEndpoint(config),
                url => new ConfigurationManager<OpenIdConnectConfiguration>(url, new OpenIdConnectConfigurationRetriever()));
        }

        public static string GetAuthorizationEndpoint(OidcProviderConfig config, OpenIdConnectConfiguration? discovery)
        {
            return RequireEndpoint(!string.IsNullOrWhiteSpace(config.AuthorizeEndpoint) ? config.AuthorizeEndpoint : discovery?.AuthorizationEndpoint, "authorization_endpoint");
        }

        public static string GetTokenEndpoint(OidcProviderConfig config, OpenIdConnectConfiguration? discovery)
        {
            return RequireEndpoint(!string.IsNullOrWhiteSpace(config.TokenEndpoint) ? config.TokenEndpoint : discovery?.TokenEndpoint, "token_endpoint");
        }

        private static string RequireEndpoint(string? endpoint, string name)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException($"The OIDC provider has no {name} configured or published in its discovery document.");
            }

            return endpoint;
        }

        public static string CreateCodeVerifier()
        {
            return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        }

        /// <summary>
        /// PKCE S256 code challenge for the given verifier (RFC 7636 section 4.2).
        /// </summary>
        public static string CreateCodeChallenge(string codeVerifier)
        {
            return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        }

        /// <summary>
        /// Authorization request for the code flow. The response comes back in the redirect URI query string, which is
        /// where the hub UI reads it, so the provider's configured response type and mode are not used.
        /// </summary>
        public static string BuildAuthorizationUrl(OidcProviderConfig config, string authorizationEndpoint, string state, string nonce, string codeChallenge)
        {
            var queryParams = new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["response_type"] = "code",
                ["scope"] = string.IsNullOrWhiteSpace(config.Scope) ? "openid profile email" : config.Scope,
                ["redirect_uri"] = config.RedirectUri,
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            return $"{authorizationEndpoint}{(authorizationEndpoint.Contains('?') ? "&" : "?")}{queryString}";
        }

        public static Dictionary<string, string> CreateTokenRequest(OidcProviderConfig config, string code, string codeVerifier)
        {
            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = config.ClientId,
                ["code"] = code,
                ["redirect_uri"] = config.RedirectUri,
                ["code_verifier"] = codeVerifier
            };

            // a public client has no secret and relies on PKCE alone
            if (!string.IsNullOrEmpty(config.ClientSecret))
            {
                tokenRequest["client_secret"] = config.ClientSecret;
            }

            return tokenRequest;
        }

        private const int MaxTokenErrorDescriptionLength = 1000;

        /// <summary>
        /// The error and error_description from a token endpoint error response (RFC 6749 section 5.2), for logging. Other
        /// fields and non-JSON bodies are left out, so nothing else the provider returns ends up in the log.
        /// </summary>
        public static string? DescribeTokenError(string? responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var description = doc.RootElement.TryGetProperty("error_description", out var desc) && desc.ValueKind == JsonValueKind.String
                    ? desc.GetString()
                    : null;

                if (description?.Length > MaxTokenErrorDescriptionLength)
                {
                    description = description[..MaxTokenErrorDescriptionLength] + "...";
                }

                return string.IsNullOrWhiteSpace(description) ? error.GetString() : $"{error.GetString()}: {description}";
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Validate an ID token's signature, issuer, audience, lifetime and nonce, throwing if any check fails.
        /// </summary>
        public static ClaimsPrincipal ValidateIdToken(OidcProviderConfig config, OpenIdConnectConfiguration discovery, string idToken, string nonce)
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(idToken, CreateValidationParameters(config, discovery), out _);

            if (string.IsNullOrEmpty(nonce) || principal.FindFirst("nonce")?.Value != nonce)
            {
                throw new SecurityTokenValidationException("ID token nonce does not match the sign in request.");
            }

            return principal;
        }

        public static TokenValidationParameters CreateValidationParameters(OidcProviderConfig config, OpenIdConnectConfiguration discovery)
        {
            if (string.IsNullOrWhiteSpace(discovery.Issuer))
            {
                throw new InvalidOperationException("The OIDC provider's discovery document does not specify an issuer.");
            }

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = discovery.Issuer,
                ValidateAudience = true,
                ValidAudience = config.ClientId,
                ValidateLifetime = true,
                RequireSignedTokens = true,
                IssuerSigningKeys = discovery.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            if (discovery.Issuer.Contains(TenantIdPlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                var templatedIssuer = discovery.Issuer;
                parameters.IssuerValidator = (issuer, token, _) => ValidateTenantIssuer(issuer, token, templatedIssuer);
            }

            return parameters;
        }

        /// <summary>
        /// Accept a token from an Entra multi-tenant authority only if its issuer is the templated issuer for the tenant
        /// named in its tid claim.
        /// </summary>
        private static string ValidateTenantIssuer(string issuer, SecurityToken token, string templatedIssuer)
        {
            var tenantId = token switch
            {
                JwtSecurityToken jwt => jwt.Payload.TryGetValue("tid", out var tid) ? tid as string : null,
                JsonWebToken jwt => jwt.TryGetPayloadValue<string>("tid", out var tid) ? tid : null,
                _ => null
            };

            if (!Guid.TryParse(tenantId, out _))
            {
                throw new SecurityTokenInvalidIssuerException("ID token has no tenant id to match the provider's multi-tenant issuer.") { InvalidIssuer = issuer };
            }

            var expectedIssuer = templatedIssuer.Replace(TenantIdPlaceholder, tenantId, StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(issuer, expectedIssuer, StringComparison.Ordinal))
            {
                throw new SecurityTokenInvalidIssuerException($"ID token issuer does not match the issuer for tenant {tenantId}.") { InvalidIssuer = issuer };
            }

            return issuer;
        }
    }
}
