using System.Security.Claims;
using System.Text.Encodings.Web;
using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Certify.Server.Hub.Api.Middleware
{
    internal class AuthorizedApiAttribute : AuthorizeAttribute
    {
        public AuthorizedApiAttribute()
        {
            AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme + "," + ApiKeyAuthenticationDefaults.AuthenticationScheme;
        }
    }

    /// <summary>
    /// Marks an <see cref="AuthorizedApiAttribute"/> endpoint which deliberately performs no resource action check,
    /// because it exposes no stored resource. Authentication alone is the whole requirement for these.
    ///
    /// Every other authenticated endpoint has to check the action it needs: being authenticated says who the caller
    /// is, not what they may do, and a JWT is issued to any principal which can sign in regardless of its roles.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class NoResourceActionRequiredAttribute : Attribute
    {
        public NoResourceActionRequiredAttribute(string reason)
        {
            Reason = reason;
        }

        /// <summary>
        /// Why this endpoint needs no resource action, so the choice is reviewable rather than assumed.
        /// </summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Api key authentication options
    /// </summary>
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
    }

    /// <summary>
    /// Default values related to API key authentication
    /// </summary>
    public static class ApiKeyAuthenticationDefaults
    {
        public const string AuthenticationScheme = "ApiToken";
        public const string ScopedAssignedRoleClaimType = "certify.scoped_assigned_role";
    }

    /// <summary>
    /// Handler to process API key authentication
    /// </summary>
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <param name="logger"></param>
        /// <param name="encoder"></param>
        /// <param name="client"></param>
        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ICertifyInternalApiClient client
     )
            : base(options, logger, encoder)
        {
            _client = client;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            //check if api key is present on request headers
            if (!Request.Headers.ContainsKey("X-Client-ID") || !Request.Headers.ContainsKey("X-Client-Secret"))
            {
                return AuthenticateResult.NoResult();
            }

            var token = new AccessToken
            {
                ClientId = Request.Headers["X-Client-ID"]!,
                Secret = Request.Headers["X-Client-Secret"]!
            };

            // Resolve the token to the principal it belongs to. This is authentication and asks only whether the
            // token is valid: it used to also require the principal to hold a specific action, which made that one
            // action an invisible prerequisite for every API token and locked out tokens for roles which do not
            // grant it. What the resolved principal may do is checked by each endpoint against its own action.
            var result = await _client.ResolveApiToken(token, default!);

            if (!result.IsSuccess)
            {
                return AuthenticateResult.Fail("API credentials invalid");
            }

            var tokenAuthContext = AccessTokenAuthorization.FromCheckResult(result.Result);

            var claims = new List<Claim>
            {
                new(ClaimTypes.Sid, string.IsNullOrWhiteSpace(tokenAuthContext?.SecurityPrincipalId) ? "api-client" : tokenAuthContext.SecurityPrincipalId),
                new("certify.api_client_id", token.ClientId)
            };

            if (tokenAuthContext?.ScopedAssignedRoles != null)
            {
                foreach (var scopedAssignedRoleId in tokenAuthContext.ScopedAssignedRoles.Where(r => !string.IsNullOrWhiteSpace(r)))
                {
                    claims.Add(new Claim(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType, scopedAssignedRoleId));
                }
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }

    /// <summary>
    /// Reads the security principal an API access token resolved to from an access check result. The result crosses
    /// the internal API boundary, so it arrives already typed from an in-process backend and as deserialized JSON
    /// from a remote one.
    /// </summary>
    internal static class AccessTokenAuthorization
    {
        public static AccessTokenAuthorizationContext? FromCheckResult(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is AccessTokenAuthorizationContext typed)
            {
                return typed;
            }

            if (value is JObject jObject)
            {
                return jObject.ToObject<AccessTokenAuthorizationContext>();
            }

            if (value is IDictionary<string, object> dict)
            {
                var principalId = dict.TryGetValue(nameof(AccessTokenAuthorizationContext.SecurityPrincipalId), out var principalObj)
                    ? principalObj?.ToString()
                    : null;

                var scopedRoles = new List<string>();
                if (dict.TryGetValue(nameof(AccessTokenAuthorizationContext.ScopedAssignedRoles), out var rolesObj) && rolesObj is IEnumerable<object> roleObjs)
                {
                    scopedRoles.AddRange(roleObjs.Select(r => r?.ToString()).Where(r => !string.IsNullOrWhiteSpace(r))!);
                }

                return new AccessTokenAuthorizationContext
                {
                    SecurityPrincipalId = principalId ?? string.Empty,
                    ScopedAssignedRoles = scopedRoles
                };
            }

            return null;
        }
    }
}
