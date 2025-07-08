using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Certify.Server.Hub.Api.Controllers
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

        private readonly IMemoryCache _memoryCache;

        /// <summary>
        /// Controller for Auth operations
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="config"></param>
        /// <param name="memoryCache"></param>
        public AuthController(ILogger<AuthController> logger, ICertifyInternalApiClient client, IConfiguration config, IMemoryCache memoryCache)
        {
            _logger = logger;
            _client = client;
            _config = config;

            _memoryCache = memoryCache;
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

        private void CacheRefreshToken(string userId, string refreshToken)
        {
            var refreshTokenExpiryMinutes = int.Parse(_config["JwtSettings:refreshTokenExpirationInMinutes"] ?? "600");

            var expiry = new TimeSpan(0, refreshTokenExpiryMinutes, 0);

            _memoryCache.Set("RefreshToken_" + refreshToken, userId, expiry);
        }

        /// <summary>
        /// Perform login using username and password
        /// </summary>
        /// <param name="login">Login credentials</param>
        /// <returns>Response contains access token and refresh token for API operations.</returns>
        [HttpPost]
        [Route("login")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login(AuthRequest login)
        {

            // check users login, if valid issue new JWT access token and refresh token based on their identity
            var validation = await _client.ValidateSecurityPrincipalPassword(new SecurityPrincipalPasswordCheck() { Username = login.Username, Password = login.Password }, CurrentAuthContext);

            if (validation.IsSuccess && validation.SecurityPrincipal != null)
            {
                // TODO: get user details from API and return as part of response instead of returning as json

                var jwt = new Hub.Api.Services.JwtService(_config);

                var refreshToken = jwt.GenerateRefreshToken();

                CacheRefreshToken(validation.SecurityPrincipal.Id, refreshToken);

                var jwtExpiryMinutes = double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"] ?? "20");
                var newJwt = jwt.GenerateSecurityToken(validation.SecurityPrincipal.Id, jwtExpiryMinutes);

                var authContext = new AuthContext
                {
                    UserId = validation.SecurityPrincipal.Id,
                    Token = newJwt
                };

                var authResponse = new AuthResponse
                {
                    Detail = "OK",
                    AccessToken = newJwt,
                    RefreshToken = refreshToken,
                    SecurityPrincipal = validation.SecurityPrincipal,
                    RoleStatus = await _client.GetSecurityPrincipalRoleStatus(validation.SecurityPrincipal.Id, authContext)
                };

                // TODO: Refresh token should be stored or hashed for later use

                return Ok(authResponse);
            }
            else
            {
                //return Unauthorized("Invalid username or password");
                return Problem(
       type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
       title: "Login Failed",
       detail: "Invalid username or password",
       statusCode: StatusCodes.Status401Unauthorized);

            }
        }

        /// <summary>
        /// Refresh users current auth token using refresh token
        /// </summary>
        /// <param name="refreshToken"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost]
        [Route("refresh")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Refresh(string refreshToken)
        {
            try
            {
                // validate token and issue new one
                if (_memoryCache.TryGetValue("RefreshToken_" + refreshToken, out string? userId))
                {
                    // we have a valid refresh token, refresh and auth user

                    var spList = await _client.GetSecurityPrincipals(CurrentAuthContext);
                    var sp = spList.Single(s => s.Id == userId);

                    var jwtExpiryMinutes = double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"] ?? "20");
                    var jwt = new Hub.Api.Services.JwtService(_config);
                    var newJwt = jwt.GenerateSecurityToken(sp.Id, jwtExpiryMinutes);

                    // invalidate old refresh token and store new one
                    var newRefreshToken = jwt.GenerateRefreshToken();

                    CacheRefreshToken(sp.Id, newRefreshToken);

                    var authContext = new AuthContext
                    {
                        UserId = sp.Id,
                        Token = newJwt
                    };

                    var authResponse = new AuthResponse
                    {
                        Detail = "OK",
                        AccessToken = newJwt,
                        RefreshToken = newRefreshToken,
                        SecurityPrincipal = sp,
                        RoleStatus = await _client.GetSecurityPrincipalRoleStatus(sp.Id, authContext)
                    };

                    return Ok(authResponse);
                }
                else
                {
                    // no valid refresh token found
                    return Unauthorized();
                }
            }
            catch
            {
                return Unauthorized();
            }
        }
    }
}
