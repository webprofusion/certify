using System.Security.Claims;
using System.Text.Encodings.Web;
using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Certify.Server.Hub.Api.Middleware
{
    internal class AuthorizedApiAttribute : AuthorizeAttribute
    {
        public AuthorizedApiAttribute()
        {
            AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme + "," + ApiKeyAuthenticationDefaults.AuthenticationScheme;
        }
    }

    internal class FeatureAuthorizeAttribute : AuthorizeAttribute
    {

        private string _resourceType = "";
        private string _resourceAction = "";

        public string ResourceType
        {
            get
            {
                return _resourceType;
            }
            set
            {
                _resourceType = value;
            }
        }

        public string ResourceAction
        {
            get
            {
                return _resourceAction;
            }
            set
            {
                _resourceAction = value;
            }
        }
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

            var endpoint = Context.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<FeatureAuthorizeAttribute>() != null)
            {
                // could apply check directly here if needed
            }

            var token = new AccessToken
            {
                ClientId = Request.Headers["X-Client-ID"]!,
                Secret = Request.Headers["X-Client-Secret"]!
            };

            var check = new AccessCheck
            {
                ResourceType = ResourceTypes.SecurityPrincipal,
                ResourceActionId = StandardResourceActions.SecurityPrincipalCheckAccess
            };

            // check api key is valid for general access
            var result = await _client.CheckApiTokenHasAccess(token, check, default!);

            if (!result.IsSuccess)
            {
                return AuthenticateResult.Fail("API credentials invalid");
            }

            var claims = new Claim[]
            {
                new(ClaimTypes.Sid, "api-client"),
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
