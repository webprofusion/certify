using System.Net.Http.Headers;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.API;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    /// <summary>
    /// Provides auth related operations
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthController : ControllerBase
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
        public AuthResponse Login(AuthRequest login)
        {
            // TODO: check users login, if valid issue new JWT access token and refresh token based on their identity
            // Refresh token should be stored or hashed for later use

            var jwt = new Api.Public.Services.JwtService(_config);

            var authResponse = new AuthResponse
            {
                Detail = "OK",
                AccessToken = jwt.GenerateSecurityToken(login.Username, double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"])),
                RefreshToken = jwt.GenerateRefreshToken()
            };

            return authResponse;
        }

        /// <summary>
        /// Refresh users current auth token
        /// </summary>
        /// <param name="refreshToken"></param>
        /// <returns></returns>
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost]
        [Route("refresh")]
        public AuthResponse Refresh(string refreshToken)
        {
            // validate token and issue new one
            var jwt = new Api.Public.Services.JwtService(_config);

            var authToken = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]).Parameter;
            var claimsIdentity = jwt.ClaimsIdentityFromToken(authToken, false);
            var username = claimsIdentity.Name;

            // var savedRefreshToken = GetRefreshToken(username); //retrieve the refresh token from a data store
            // if (savedRefreshToken != refreshToken)
            // throw new SecurityTokenException("Invalid refresh token");

            var newJwtToken = jwt.GenerateSecurityToken(username, double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"]));
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

            return authResponse;
        }
    }
}
