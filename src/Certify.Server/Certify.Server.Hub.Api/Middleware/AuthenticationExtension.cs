using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Certify.Server.Hub.Api.Middleware
{
    /// <summary>
    /// What a hub issued JWT is for.
    ///
    /// A joining token and a signed in user's token are both bearer tokens signed with the same key, so without
    /// something to tell them apart every endpoint which accepts one accepts the other. That mattered: a joining
    /// token could subscribe to the user interface status hub, which carries managed certificate state for every
    /// connected instance.
    /// </summary>
    public static class HubTokenPurposes
    {
        public const string ClaimType = "certify.token_purpose";

        /// <summary>
        /// Issued to a managed instance so it can open its management hub connection, and good for nothing else.
        /// </summary>
        public const string ManagementHubJoin = "managementhub-join";

        /// <summary>
        /// Authorization policy for the management hub connection: the caller must present a joining token.
        /// </summary>
        public const string ManagementHubJoinPolicy = "certify.managementhub-join";

        /// <summary>
        /// True when a principal was authenticated from a joining token.
        /// </summary>
        public static bool IsManagementHubJoinToken(System.Security.Claims.ClaimsPrincipal? principal)
            => principal?.FindFirst(ClaimType)?.Value == ManagementHubJoin;
    }

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
                // JwtService omits the iss claim entirely when no issuer is configured, which would fail validation
                // here, so this is surfaced at startup rather than as opaque 401s
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

        /// <summary>
        /// Authorization policies separating the two kinds of token this hub issues.
        ///
        /// The default policy is what every [AuthorizedApi] endpoint and RequireAuthorization() call resolves to, so
        /// excluding joining tokens there covers the whole API and the user interface status hub in one place rather
        /// than relying on each endpoint to notice. The management hub connection opts back in explicitly, since a
        /// joining token is exactly what it expects.
        /// </summary>
        public static IServiceCollection AddHubAuthorization(this IServiceCollection services)
        {
            return services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => !HubTokenPurposes.IsManagementHubJoinToken(context.User))
                    .Build();

                options.AddPolicy(HubTokenPurposes.ManagementHubJoinPolicy, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(HubTokenPurposes.ClaimType, HubTokenPurposes.ManagementHubJoin));
            });
        }
    }
}
