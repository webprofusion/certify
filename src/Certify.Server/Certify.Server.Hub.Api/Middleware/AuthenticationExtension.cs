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
        /// https://github.com/fscopel/token-based-authentication
        /// </summary>
        /// <param name="services"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public static IServiceCollection AddTokenAuthentication(this IServiceCollection services, IConfiguration config)
        {
            var secret = config.GetSection("JwtSettings").GetSection("secret").Value;

            if (secret == null)
            {
                throw new ArgumentNullException("Token authentication requires JwtSettings > Secret to be set in order to perform JWT operations");
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
