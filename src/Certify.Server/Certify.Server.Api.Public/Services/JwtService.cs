using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Certify.Server.Api.Public.Services
{
    // https://www.c-sharpcorner.com/article/implement-jwt-in-asp-net-core-3-1/
    // https://www.blinkingcaret.com/2018/05/30/refresh-tokens-in-asp-net-core-web-api/

    /// <summary>
    /// Provides JWT related operations
    /// </summary>
    public class JwtService
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _expDate;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        public JwtService(IConfiguration config)
        {
            _secret = config.GetSection("JwtSettings").GetSection("secret").Value;
            _issuer = config.GetSection("JwtSettings").GetSection("issuer").Value;
            _expDate = config.GetSection("JwtSettings").GetSection("expirationInDays").Value;
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
        /// <returns></returns>

        public string GenerateSecurityToken(string identifier, double? expiryMinutes)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, identifier),
                    new Claim(ClaimTypes.NameIdentifier, identifier)
                }),
                Issuer = _issuer,
                Expires = expiryMinutes != null ? DateTime.UtcNow.AddHours((double)expiryMinutes) : DateTime.UtcNow.AddDays(double.Parse(_expDate)), //token expiry could be role specific - e.g. 1 yr vs 1 month
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);

        }
        /// <summary>
        /// Parse a provided token and extract claims
        /// </summary>
        /// <param name="token"></param>
        /// <param name="validateTokenLifetime"></param>
        /// <returns></returns>
        /// <exception cref="SecurityTokenException"></exception>
        public ClaimsPrincipal GetClaimsPrincipalFromToken(string token, bool validateTokenLifetime)
        {
            var key = Encoding.UTF8.GetBytes(_secret);

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateLifetime = validateTokenLifetime,
                ValidIssuer = _issuer
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            var jwtSecurityToken = securityToken as JwtSecurityToken;

            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException("Invalid token");
            }

            // principal.Identity.Name = jwtSecurityToken.Claims.First(c => c.Type == "name").Value;
            return principal;
        }
    }
}
