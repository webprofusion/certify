using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

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
        [AuthorizedApi]
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
                    IsSuccess = true,
                    Detail = "OK",
                    AccessToken = newJwt,
                    RefreshToken = refreshToken,
                    SecurityPrincipal = validation.SecurityPrincipal,
                    RoleStatus = await _client.GetSecurityPrincipalRoleStatus(validation.SecurityPrincipal.Id, authContext)
                };

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
                        IsSuccess = true,
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

        /// <summary>
        /// Initiate OIDC login flow
        /// </summary>
        /// <param name="provider">OIDC provider identifier</param>
        /// <param name="returnUrl">URL to return to after authentication</param>
        /// <returns>Authorization URL for client redirect</returns>
        [HttpGet]
        [Route("oidc/login")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(OidcLoginResponse))]
        public async Task<IActionResult> BeginOidcLogin(string? provider = "default", string? returnUrl = null)
        {
            try
            {
                var oidcConfig = await GetOidcProviderWithSecret(provider);
                if (oidcConfig == null)
                {
                    return BadRequest($"OIDC provider '{provider}' not configured");
                }

                // Generate state and nonce for security
                var state = GenerateSecureToken();
                var nonce = GenerateSecureToken();

                // Store state and nonce for validation (using session for simplicity)
                _memoryCache.Set($"oidc_state_{state}", JsonSerializer.Serialize(new OidcState
                {
                    Provider = provider,
                    Nonce = nonce,
                    ReturnUrl = returnUrl,
                    Timestamp = DateTimeOffset.UtcNow
                }), TimeSpan.FromMinutes(2));

                // Build authorization URL
                var authUrl = BuildAuthorizationUrl(oidcConfig, state, nonce);

                return Ok(new OidcLoginResponse { AuthUrl = authUrl, State = state });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating OIDC login for provider {Provider}", provider);
                return StatusCode(500, "Failed to initiate OIDC login");
            }
        }

        /// <summary>
        /// Handle OIDC callback and complete authentication
        /// </summary>
        /// <param name="code">Authorization code from OIDC provider</param>
        /// <param name="state">State parameter for CSRF protection</param>
        /// <param name="id_token">ID token (for implicit flow)</param>
        /// <param name="error">Error from OIDC provider</param>
        /// <param name="error_description">Error description</param>
        /// <returns>Authentication result</returns>
        [HttpPost]
        [Route("oidc/login-callback")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AuthResponse))]
        public async Task<IActionResult> CompleteOidcLogin([FromBody] OidcCallbackBody msg)
        {
            try
            {

                _logger.LogInformation("OIDC authentication CompleteOidcLogin: {msg} - {Description}", msg);

                // Handle OIDC errors
                if (!string.IsNullOrEmpty(msg.error))
                {
                    _logger.LogWarning("OIDC authentication error: {Error} - {Description}", msg.error, msg.error_description);
                    return BadRequest(new AuthResponse
                    {
                        IsSuccess = false,
                        Detail = msg.error_description
                    });
                }

                // Validate state parameter
                if (string.IsNullOrEmpty(msg.state))
                {
                    _logger.LogWarning("OIDC authentication error: callback body message missing state");
                    return BadRequest(new AuthResponse { IsSuccess = false, Detail = "missing_state" });
                }

                var stateJson = _memoryCache.Get($"oidc_state_{msg.state}") as string;

                if (string.IsNullOrEmpty(stateJson))
                {
                    _logger.LogWarning("OIDC authentication error: callback body message missing state");
                    return BadRequest(new AuthResponse { IsSuccess = false, Detail = "invalid_state" });
                }
                else
                {
                    // cleanup used state
                    _memoryCache.Remove($"oidc_state_{msg.state}");
                }

                var oidcState = JsonSerializer.Deserialize<OidcState>(stateJson);
                if (oidcState == null || oidcState.Timestamp.AddMinutes(10) < DateTimeOffset.UtcNow)
                {
                    return BadRequest(new OidcCallbackResponse { Success = false, Error = "expired_state" });
                }

                var oidcConfig = await GetOidcProviderWithSecret(oidcState.Provider);
                if (oidcConfig == null)
                {
                    return BadRequest(new AuthResponse { IsSuccess = false, Detail = "invalid_provider" });
                }

                ClaimsPrincipal? userClaims = null;

                // Handle different OIDC flows
                if (!string.IsNullOrEmpty(msg.code))
                {
                    // Authorization Code flow
                    userClaims = await HandleAuthorizationCodeFlow(oidcConfig, msg.code, oidcState.Nonce);
                }
                else if (!string.IsNullOrEmpty(msg.id_token))
                {
                    // Implicit flow
                    userClaims = await HandleImplicitFlow(oidcConfig, msg.id_token, oidcState.Nonce);
                }

                if (userClaims == null)
                {
                    return BadRequest(new AuthResponse { IsSuccess = false, Detail = "authentication_failed" });
                }

                // Create or validate user in your system
                var authResult = await ProcessUserAuthentication(userClaims, oidcState.Provider);

                return Ok(authResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing OIDC login");
                return StatusCode(500, new AuthResponse { IsSuccess = false, Detail = "internal_error" });
            }
        }

        private async Task<OidcProviderConfig?> GetOidcProviderWithSecret(string? provider)
        {
            return await _client.GetOidcProviderWithSecret(provider, CurrentAuthContext);
        }

        private string BuildAuthorizationUrl(OidcProviderConfig config, string state, string nonce)
        {
            var queryParams = new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["response_type"] = config.ResponseType ?? "code",
                ["scope"] = config.Scope ?? "openid profile email",
                ["redirect_uri"] = config.RedirectUri,
                ["state"] = state,
                ["nonce"] = nonce
            };

            if (!string.IsNullOrEmpty(config.ResponseMode))
            {
                // queryParams["response_mode"] = config.ResponseMode;
            }

            var queryString = string.Join("&", queryParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var authEndpoint = config.AuthorizeEndpoint ?? $"{config.Authority.TrimEnd('/')}/oauth2/v2.0/authorize";
            return $"{authEndpoint}?{queryString}";
        }

        private async Task<ClaimsPrincipal?> HandleAuthorizationCodeFlow(OidcProviderConfig config, string code, string nonce)
        {
            using var httpClient = new HttpClient();

            // Exchange code for tokens
            var tokenRequest = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = config.RedirectUri
            };

            var tokenEndpoint = config.TokenEndpoint ?? $"{config.Authority.TrimEnd('/')}/oauth2/v2.0/token";
            var tokenResponse = await httpClient.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(tokenRequest));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {StatusCode}", tokenResponse.StatusCode);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);

            if (!tokenData.TryGetProperty("id_token", out var idTokenElement))
            {
                _logger.LogError("No id_token in token response");
                return null;
            }

            return await ValidateIdToken(config, idTokenElement.GetString()!, nonce);
        }

        private async Task<ClaimsPrincipal?> HandleImplicitFlow(OidcProviderConfig config, string idToken, string nonce)
        {
            return await ValidateIdToken(config, idToken, nonce);
        }

        private async Task<ClaimsPrincipal?> ValidateIdToken(OidcProviderConfig config, string idToken, string nonce)
        {
            try
            {
                var discoveryEndpoint = config.DiscoveryEndpoint ?? $"{config.Authority.TrimEnd('/')}/.well-known/openid-configuration";
                var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    discoveryEndpoint,
                    new OpenIdConnectConfigurationRetriever());

                var oidcConfig = await configManager.GetConfigurationAsync();

                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidIssuers = new[] { oidcConfig.Issuer },
                    ValidateAudience = true,
                    ValidAudience = config.ClientId,
                    ValidateLifetime = true,
                    IssuerSigningKeys = oidcConfig.SigningKeys,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };

                var principal = tokenHandler.ValidateToken(idToken, validationParameters, out _);

                // Validate nonce
                var tokenNonce = principal.FindFirst("nonce")?.Value;
                if (tokenNonce != nonce)
                {
                    _logger.LogError("Nonce validation failed");
                    return null;
                }

                return principal;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ID token validation failed");
                return null;
            }
        }

        private async Task<AuthResponse> ProcessUserAuthentication(ClaimsPrincipal claims, string provider)
        {
            // Extract user information from claims
            var email = claims.FindFirst(ClaimTypes.Email)?.Value ??
                       claims.FindFirst("email")?.Value;
            var name = claims.FindFirst(ClaimTypes.Name)?.Value ??
                      claims.FindFirst("name")?.Value;
            var subject = claims.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         claims.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(subject))
            {
                throw new InvalidOperationException("No subject claim found in token");
            }

            // Create external identifier
            var externalId = $"{provider}:{subject}";

            // Check if user exists in your system by external identifier

            var spList = await _client.GetSecurityPrincipals(CurrentAuthContext);
            var sp = spList.SingleOrDefault(s => s.ExternalIdentifier == externalId && s.Provider == provider);

            if (sp == null)
            {
                sp = new SecurityPrincipal
                {
                    Id = Guid.NewGuid().ToString(),
                    Username = email ?? externalId,
                    Email = email,
                    Title = name ?? email ?? externalId,
                    ExternalIdentifier = externalId,
                    Provider = provider,
                    IsBuiltIn = false,
                    PrincipalType = SecurityPrincipalType.User,
                    Password = Guid.NewGuid().ToString()
                };

                // If user does not exist, create a new user with no permissions
                var added = await _client.AddSecurityPrincipal(sp, SystemAuthContext);

                if (!added.IsSuccess)
                {
                    throw new InvalidOperationException("Failed to create user account");
                }
            }

            var jwtExpiryMinutes = double.Parse(_config["JwtSettings:authTokenExpirationInMinutes"] ?? "20");

            var jwt = new Hub.Api.Services.JwtService(_config);

            var accessToken = jwt.GenerateSecurityToken(sp.Id, jwtExpiryMinutes);
            var refreshToken = jwt.GenerateRefreshToken();
            CacheRefreshToken(sp.Id, refreshToken);

            return new AuthResponse
            {
                IsSuccess = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                SecurityPrincipal = sp,
                RoleStatus = await _client.GetSecurityPrincipalRoleStatus(sp.Id, CurrentAuthContext)
            };
        }

        private static string GenerateSecureToken()
        {
            return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }
}
