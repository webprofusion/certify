using System.IO;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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

        /// <summary>
        /// The client id of the API access token a request authenticated with. Lets a later stage recognise that a
        /// credential it holds is the one the request already authenticated as, rather than resolving it again.
        /// </summary>
        public const string ApiClientIdClaimType = "certify.api_client_id";

        /// <summary>
        /// Headers carrying an API access token's client id and secret.
        /// </summary>
        public const string ClientIdHeaderName = "X-Client-ID";
        public const string ClientSecretHeaderName = "X-Client-Secret";

        /// <summary>
        /// JSON body fields carrying the same credential. The managed challenge API has always accepted the client
        /// id and secret this way, and the Certify managed DNS provider shipped in released agents sends them only
        /// this way, so the hub has to keep accepting it.
        /// </summary>
        public const string InlineClientIdFieldName = "AuthKey";
        public const string InlineClientSecretFieldName = "AuthSecret";
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
        /// Largest request body which will be buffered and parsed looking for inline credentials.
        ///
        /// This path runs for callers who have not authenticated yet, so it has to be bounded: without a limit an
        /// anonymous request could make the hub buffer and parse an arbitrarily large body before anything has
        /// established who sent it. A credential carrying request is a few hundred bytes.
        /// </summary>
        private const long MaxInlineCredentialBodyBytes = 64 * 1024;

        /// <summary>
        /// Authenticate an API access token presented either as request headers or as fields inside a JSON request
        /// body. Both transports carry the same credential, so both are resolved here rather than in the endpoints
        /// which happen to accept the second one - that is what lets those endpoints authenticate through the
        /// middleware like every other endpoint instead of reading credentials off the request themselves.
        /// </summary>
        /// <returns></returns>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = ReadHeaderCredentials() ?? await ReadInlineCredentialsAsync();

            if (token == null)
            {
                return AuthenticateResult.NoResult();
            }

            // Resolve the token to the principal it belongs to. This is authentication and asks only whether the
            // token is valid: it used to also require the principal to hold a specific action, which made that one
            // action an invisible prerequisite for every API token and locked out tokens for roles which do not
            // grant it. What the resolved principal may do is checked by each endpoint against its own action.
            var result = await _client.ResolveApiToken(token, default!);

            if (!result.IsSuccess)
            {
                return AuthenticateResult.Fail("API credentials invalid");
            }

            var tokenAuthContext = result.Result;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Sid, string.IsNullOrWhiteSpace(tokenAuthContext?.SecurityPrincipalId) ? "api-client" : tokenAuthContext.SecurityPrincipalId),
                new(ApiKeyAuthenticationDefaults.ApiClientIdClaimType, token.ClientId)
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

        /// <summary>
        /// Credentials presented as request headers, the normal transport.
        /// </summary>
        private AccessToken? ReadHeaderCredentials()
        {
            var clientId = Request.Headers[ApiKeyAuthenticationDefaults.ClientIdHeaderName].ToString();
            var secret = Request.Headers[ApiKeyAuthenticationDefaults.ClientSecretHeaderName].ToString();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }

            return new AccessToken { ClientId = clientId, Secret = secret };
        }

        /// <summary>
        /// Credentials presented as AuthKey/AuthSecret fields inside a JSON request body, as the managed challenge
        /// API accepts them.
        ///
        /// The body is buffered and rewound so that model binding still sees it, and nothing is parsed unless the
        /// request declares a JSON content type and a length small enough to be a credential carrying request.
        /// A body which is not JSON, or carries neither field, simply yields no credential.
        /// </summary>
        private async Task<AccessToken?> ReadInlineCredentialsAsync()
        {
            if (!HttpMethods.IsPost(Request.Method) && !HttpMethods.IsPut(Request.Method))
            {
                return null;
            }

            var contentLength = Request.ContentLength;

            if (contentLength is null or <= 0 or > MaxInlineCredentialBodyBytes)
            {
                return null;
            }

            if (Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            {
                return null;
            }

            try
            {
                Request.EnableBuffering();

                if (!Request.Body.CanSeek)
                {
                    return null;
                }

                Request.Body.Position = 0;

                using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: Context.RequestAborted);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var clientId = ReadStringProperty(document.RootElement, ApiKeyAuthenticationDefaults.InlineClientIdFieldName);
                var secret = ReadStringProperty(document.RootElement, ApiKeyAuthenticationDefaults.InlineClientSecretFieldName);

                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
                {
                    return null;
                }

                return new AccessToken { ClientId = clientId, Secret = secret };
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
                // a body which cannot be read or is not valid JSON carries no credential, and this is not the
                // layer which reports that to the caller - model binding will fail the request on its own terms
                return null;
            }
            finally
            {
                if (Request.Body.CanSeek)
                {
                    Request.Body.Position = 0;
                }
            }
        }

        /// <summary>
        /// Read a string property by name, ignoring case. The field names travel over the wire and are serialized
        /// by clients we do not control, so a casing difference must not decide whether a caller authenticates.
        /// </summary>
        private static string? ReadStringProperty(JsonElement element, string propertyName)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }

            return null;
        }
    }

}
