using System.Net.Http.Headers;
using System.Linq;
using System.Security.Claims;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Base class for public api controllers
    /// </summary>
    public partial class ApiControllerBase : ControllerBase
    {

        /// <summary>
        /// Special auth context used internally for operations where the requesting user may not be authorized to query system state
        /// </summary>
        internal AuthContext SystemAuthContext = new AuthContext { UserId = StandardSecurityPrincipals.System };

        /// <summary>
        /// Check resource action access for the current user
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="check"></param>
        /// <returns></returns>
        internal async Task<bool> IsAuthorized(ICertifyInternalApiClient internalApiClient, AccessCheck check)
        {
            if (string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return false;
            }

            /// if check does not specify security principal use the current user
            if (check.SecurityPrincipalId == null)
            {
                check.SecurityPrincipalId = CurrentAuthContext.UserId;
            }

            return await internalApiClient.CheckSecurityPrincipalHasAccess(check, CurrentAuthContext);
        }

        /// <summary>
        /// Check resource action access for the given API access token
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="token"></param>
        /// <param name="check"></param>
        /// <returns></returns>
        internal async Task<Certify.Models.Config.ActionResult> IsAccessTokenAuthorized(ICertifyInternalApiClient internalApiClient, AccessToken token, AccessCheck check)
        {
            return await internalApiClient.CheckApiTokenHasAccess(token, check, CurrentAuthContext);
        }

        internal async Task<Certify.Models.Config.ActionResult> CheckRequestAuthorized(ICertifyInternalApiClient internalApiClient, AccessCheck check)
        {
            // check for authorization bearer token first

            var currenAuthContextCheckOK = await IsAuthorized(internalApiClient, check);

            if (currenAuthContextCheckOK)
            {
                return new Certify.Models.Config.ActionResult("Authorized by bearer token", true);
            }

            // check for access token in request headers
            var accessToken = GetAccessTokenFromRequest();

            if (accessToken == null)
            {
                return new Certify.Models.Config.ActionResult("X-Client-ID or X-Client-Secret HTTP header missing in request", false);
            }

            return await IsAccessTokenAuthorized(internalApiClient, accessToken, check);
        }

        internal AccessToken? GetAccessTokenFromRequest()
        {
            var clientId = Request.Headers["X-Client-ID"];
            var secret = Request.Headers["X-Client-Secret"];

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            {
                return null;
            }
            else
            {
                return new AccessToken
                {
                    ClientId = clientId,
                    Secret = secret
                };
            }
        }

        internal async Task<ManagedInstanceRequestAuthValidationResult> ValidateManagedInstanceRequestAuthAsync()
        {
            var validator = HttpContext.RequestServices.GetService<ManagedInstanceRequestAuthValidator>()
                ?? ActivatorUtilities.CreateInstance<ManagedInstanceRequestAuthValidator>(HttpContext.RequestServices);

            return await validator.ValidateAsync(Request, HttpContext.RequestAborted);
        }

        /// <summary>
        /// Get the corresponding auth context to pass to the backend service
        /// </summary>
        /// <returns></returns>
        internal AuthContext? CurrentAuthContext
        {
            get
            {
                var principal = HttpContext?.User;
                if (principal?.Identity?.IsAuthenticated == true)
                {
                    var userIdFromClaims = principal.FindFirst(ClaimTypes.Sid)?.Value;
                    if (!string.IsNullOrWhiteSpace(userIdFromClaims))
                    {
                        var authContext = new AuthContext { UserId = userIdFromClaims };

                        var scopedAssignedRoles = principal
                            .FindAll(ApiKeyAuthenticationDefaults.ScopedAssignedRoleClaimType)
                            .Select(c => c.Value)
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct()
                            .ToList();

                        if (scopedAssignedRoles.Any())
                        {
                            authContext.ScopedAssignedRoles = scopedAssignedRoles;
                        }

                        var authHeaderValue = Request.Headers["Authorization"];
                        if (!string.IsNullOrWhiteSpace(authHeaderValue) && authHeaderValue.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            authContext.Token = AuthenticationHeaderValue.Parse(authHeaderValue!).Parameter;
                        }

                        return authContext;
                    }
                }

                var authHeader = Request.Headers["Authorization"];

                if (string.IsNullOrWhiteSpace(authHeader))
                {
                    return null;
                }

                var authToken = AuthenticationHeaderValue.Parse(authHeader!).Parameter;

                if (string.IsNullOrWhiteSpace(authToken))
                {
                    return null;
                }

                var _cache = HttpContext.RequestServices.GetRequiredService<IMemoryCache>();

                if (_cache.TryGetValue(authToken, out AuthContext? cachedAuthContext))
                {
                    if (cachedAuthContext != null)
                    {
                        return cachedAuthContext;
                    }
                }

                try
                {
                    var _config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                    var jwt = new Hub.Api.Services.JwtService(_config);
                    var claimsIdentity = jwt.ClaimsIdentityFromTokenAsync(authToken, validateTokenLifetime: true).Result;
                    var userId = claimsIdentity.FindFirst(ClaimTypes.Sid)?.Value;

                    var authContext = new AuthContext { Token = authToken, UserId = userId };

                    _cache.Set(authToken, authContext, TimeSpan.FromMinutes(20));
                    return authContext;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
