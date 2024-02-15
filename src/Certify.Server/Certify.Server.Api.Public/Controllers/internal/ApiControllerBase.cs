using System.Net.Http.Headers;
using Certify.Client;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Base class for public api controllers
    /// </summary>
    public partial class ApiControllerBase : ControllerBase
    {
        /// <summary>
        /// Get the corresponding auth context to pass to the backend service
        /// </summary>
        /// <returns></returns>
        internal AuthContext CurrentAuthContext
        {
            get
            {
                var _config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var jwt = new Api.Public.Services.JwtService(_config);

                var authHeader = Request.Headers["Authorization"];
                if (string.IsNullOrWhiteSpace(authHeader))
                {
                    return null;
                }

                var authToken = AuthenticationHeaderValue.Parse(authHeader).Parameter;

                try
                {
                    var claimsIdentity = jwt.ClaimsIdentityFromToken(authToken, false);
                    var username = claimsIdentity.Name;

                    var authContext = new AuthContext { Token = authToken, Username = username };

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
