using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Certify.Server.Hub.Api.Services
{
    // https://www.c-sharpcorner.com/article/implement-jwt-in-asp-net-core-3-1/
    // https://www.blinkingcaret.com/2018/05/30/refresh-tokens-in-asp-net-core-web-api/

    /// <summary>
    /// Provides JWT related operations
    /// </summary>
    public class JwtService
    {
        private readonly string _secret = default!;
        private readonly string _issuer = default!;
        private readonly string _expMinutes = default!;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        public JwtService(IConfiguration config)
        {
            _secret = config.GetSection("JwtSettings").GetSection("secret").Value ?? "";
            _issuer = config.GetSection("JwtSettings").GetSection("issuer").Value ?? "";
            _expMinutes = config.GetSection("JwtSettings").GetSection("authTokenExpirationInMinutes").Value ?? "5";

        }

        /// <summary>
        /// Generate a new refresh token
        /// </summary>
        /// <returns></returns>
        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        /// <summary>
        /// Generate a new auth token
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="expiryMinutes"></param>
        /// <param name="additionalClaims"></param>
        /// <returns></returns>

        public string GenerateSecurityToken(string identifier, double? expiryMinutes = null, IEnumerable<Claim>? additionalClaims = null)
        {
            var tokenHandler = new JsonWebTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Sid, identifier)
                }),
                Issuer = _issuer,
                Expires = expiryMinutes != null ? DateTime.UtcNow.AddMinutes((double)expiryMinutes) : DateTime.UtcNow.AddMinutes(double.Parse(_expMinutes)), //token expiry could be role specific - e.g. 1 yr vs 1 month
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            if (additionalClaims != null)
            {
                foreach (var c in additionalClaims)
                {
                    tokenDescriptor.Subject.AddClaim(c);
                }
            }

            return tokenHandler.CreateToken(tokenDescriptor);
        }

        // Token validation deliberately lives only in the JWT bearer middleware (see AuthenticationExtension).
        // This class used to also expose a ClaimsIdentityFromTokenAsync used by controllers which authenticated
        // themselves, which meant two implementations of the same validation with their own caching and lifetime
        // handling. Endpoints which are reachable anonymously now call ApiControllerBase.IdentifyOptionalCallerAsync,
        // which runs the same handlers the middleware would have.
    }
}
