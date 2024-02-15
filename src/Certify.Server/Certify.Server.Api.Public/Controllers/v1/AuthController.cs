using System.Net.Http.Headers;
using Certify.Client;
using Certify.Models.API;
using Certify.Models.Config.AccessControl;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Provides auth related operations
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class AuthController : ApiControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly ICertifyInternalApiClient _client;
        private IConfiguration _config;

        /// <summary>
        /// Controller for Auth operations
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="config"></param>
        public AuthController(ILogger<AuthController> logger, ICertifyInternalApiClient client, IConfiguration config)
        {
            _logger = logger;
            _client = client;
            _config = config;
        }

        /// <summary>
        /// Operations to check current auth status for the given presented authentication tokens
        /// </summary>
        /// <returns></returns>
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet]
        [Route("status")]
        public async Task<IActionResult> CheckAuthStatus()
        {
            return await Task.FromResult(new OkResult());
        }

        /// <summary>
        /// Perform login using username and password
        /// </summary>
        /// <param name="login">Login credentials</param>
        /// <returns>Response contains access token and refresh token for API operations.</returns>
        [HttpPost]
        [Route("login")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        public async Task<IActionResult> Login(AuthRequest login)
        {

            // check users login, if valid issue new JWT access token and refresh token based on their identity
            var validation = await _client.ValidateSecurityPrinciplePassword(new SecurityPrinciplePasswordCheck() { Username = login.Username, Password = login.Password }, CurrentAuthContext);

            if (validation.IsSuccess)
            {
                // TODO: get user details from API and return as part of response instead of returning as json

                var jwt = new Api.Public.Services.JwtService(_config);

                var authResponse = new AuthResponse
                {
                    Detail = "OK",
                    AccessToken = jwt.GenerateSecurityToken(login.Username, double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"] ?? "20")),
                    RefreshToken = jwt.GenerateRefreshToken(),
                    SecurityPrinciple = validation.SecurityPrinciple,
                    RoleStatus = await _client.GetSecurityPrincipleRoleStatus(validation.SecurityPrinciple.Id, CurrentAuthContext)
                };


                // TODO: Refresh token should be stored or hashed for later use

                return Ok(authResponse);
            }
            else
            {
                return Unauthorized();
            }
        }

        /// <summary>
        /// Refresh users current auth token
        /// </summary>
        /// <param name="refreshToken"></param>
        /// <returns></returns>
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost]
        [Route("refresh")]
        [ProducesResponseType(typeof(AuthResponse), 200)]
        public IActionResult Refresh(string refreshToken)
        {
            // validate token and issue new one
            var jwt = new Api.Public.Services.JwtService(_config);

            var authToken = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]).Parameter;

            try
            {
                var claimsIdentity = jwt.ClaimsIdentityFromToken(authToken, false);
                var username = claimsIdentity.Name;

                if (username == null)
                {
                    return Unauthorized();
                }

                var newJwtToken = jwt.GenerateSecurityToken(username, double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"] ?? "20"));
                var newRefreshToken = jwt.GenerateRefreshToken();

                // invalidate old refresh token and store new one
                // DeleteRefreshToken(username, refreshToken);
                // SaveRefreshToken(username, newRefreshToken);

                var authResponse = new AuthResponse
                {
                    Detail = "OK",
                    AccessToken = newJwtToken,
                    RefreshToken = newRefreshToken
                };

                return Ok(authResponse);
            }
            catch
            {
                return Unauthorized();
            }
        }
    }
}
