using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Certify.Server.Api.Public.Middleware
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

            var key = Encoding.ASCII.GetBytes(secret);
            services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = true;
                x.SaveToken = true;

                x.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true
                };
            });


            return services;
        }
    }
}
