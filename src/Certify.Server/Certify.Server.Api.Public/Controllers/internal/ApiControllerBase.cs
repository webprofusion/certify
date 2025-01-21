using System.Net.Http.Headers;
using System.Security.Claims;
using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Base class for public api controllers
    /// </summary>
    public partial class ApiControllerBase : ControllerBase
    {
        /// <summary>
        /// Check resource action access for the current user
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="resourceType"></param>
        /// <param name="resourceAction"></param>
        /// <returns></returns>
        internal async Task<bool> IsAuthorized(ICertifyInternalApiClient internalApiClient, string resourceType, string resourceAction)
        {
            if (string.IsNullOrWhiteSpace(CurrentAuthContext?.UserId))
            {
                return false;
            }

            return await IsAuthorized(internalApiClient, new AccessCheck(CurrentAuthContext?.UserId, resourceType, resourceAction));
        }

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

            return await internalApiClient.CheckSecurityPrincipleHasAccess(check, CurrentAuthContext);
        }

        /// <summary>
        /// Check resource action access for the given API access token
        /// </summary>
        /// <param name="internalApiClient"></param>
        /// <param name="token"></param>
        /// <param name="check"></param>
        /// <returns></returns>
        internal async Task<Models.Config.ActionResult> IsAccessTokenAuthorized(ICertifyInternalApiClient internalApiClient, AccessToken token, AccessCheck check)
        {
            return await internalApiClient.CheckApiTokenHasAccess(token, check, CurrentAuthContext);
        }
        /// <summary>
        /// Get the corresponding auth context to pass to the backend service
        /// </summary>
        /// <returns></returns>
        internal AuthContext? CurrentAuthContext
        {
            get
            {
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
                    var jwt = new Api.Public.Services.JwtService(_config);
                    var claimsIdentity = jwt.ClaimsIdentityFromTokenAsync(authToken, false).Result;
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
