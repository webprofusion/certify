using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Certify.Server.Hub.Api.Middleware
{
    /// <summary>
    /// Provides authentication related extensions
    /// </summary>
    public static class AuthenticationExtension
    {
        /// <summary>
        /// JWT signing secrets which were shipped in a released appsettings.json and are therefore public.
        /// A configuration still using one of these is rejected at startup rather than quietly trusted.
        /// </summary>
        private static readonly HashSet<string> KnownPublishedJwtSecrets = new(StringComparer.Ordinal)
        {
            "8FdYdFZKb2gQz7c4hpX7BMKpEnrpGhI7APd7GHMdvGg"
        };

        /// <summary>
        /// https://github.com/fscopel/token-based-authentication
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public static IServiceCollection AddTokenAuthentication(this IServiceCollection services, IConfiguration config)
        {
            var secret = config.GetSection("JwtSettings").GetSection("secret").Value;

            if (string.IsNullOrWhiteSpace(secret))
            {
                // No secret is shipped as a fallback, because a published secret lets anyone forge a token for any
                // security principal. Failing startup is deliberate: silently starting with a known key would be worse.
                // A secret is normally generated and saved automatically at startup, so reaching this point means the
                // settings file could not be read or written - check the Hub API JWT Secret system status item.
                throw new InvalidOperationException(
                    "Token authentication requires JwtSettings:secret to be set. This is normally generated automatically into " +
                    "hubservice.json in the service app data path (e.g. C:\\ProgramData\\certify\\hubservice.json), so this usually " +
                    "means that file could not be read or written. Check the service has write access to it, or set " +
                    "JwtSettings:secret manually to a fresh 32 byte random value, base64 encoded.");
            }

            if (KnownPublishedJwtSecrets.Contains(secret))
            {
                // This value was previously shipped in appsettings.json, so it must be treated as public knowledge.
                throw new InvalidOperationException(
                    "The configured JwtSettings:secret is a value which was previously published with the product and can no longer be " +
                    "considered secret. Replace it in hubservice.json with a fresh 32 byte random value, base64 encoded. Any tokens " +
                    "issued using the old secret should be treated as compromised.");
            }

            var issuer = config.GetSection("JwtSettings").GetSection("issuer").Value;

            if (string.IsNullOrWhiteSpace(issuer))
            {
                // JwtService omits the iss claim entirely when no issuer is configured, which would fail validation here
                // and in JwtService.ClaimsIdentityFromTokenAsync, so this is surfaced at startup rather than as opaque 401s
                throw new ArgumentNullException("Token authentication requires JwtSettings > issuer to be set in order to issue and validate tokens");
            }

            // must match the encoding used by JwtService when signing, otherwise any non-ascii character in the
            // configured secret produces a different key here and every issued token fails validation
            var key = Encoding.UTF8.GetBytes(secret);
            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = true;
                x.SaveToken = true;

                x.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrWhiteSpace(accessToken)
                            && (path.StartsWithSegments("/api/internal/status")
                                || path.StartsWithSegments("/api/internal/managementhub")))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };

                x.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true
                };
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.AuthenticationScheme, o =>
            {
                // adds the option to authenticate using API token
            });

            return services;
        }
    }
}
