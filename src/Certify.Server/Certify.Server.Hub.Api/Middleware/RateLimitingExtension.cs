using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Certify.Server.Hub.Api.Middleware
{
    /// <summary>
    /// Rate limiting for the endpoints which are reachable without an existing session, so that credential guessing
    /// and request floods are bounded. Limits are per calling IP address and are configurable under a RateLimiting
    /// section, because the right ceiling depends on how many instances and ACME clients a hub serves.
    /// </summary>
    public static class RateLimitingExtension
    {
        /// <summary>
        /// Login and the OIDC login endpoints. Deliberately tight: this is the credential guessing surface, and a
        /// legitimate user hits it a couple of times per session.
        /// </summary>
        public const string AuthPolicy = "hub-auth";

        /// <summary>
        /// Token refresh. Separated from the login policy and set higher, because refresh is a normal background
        /// operation for every signed in session, and many users can share one apparent address behind NAT or a
        /// proxy. Sharing a tight bucket with login would risk interrupting working sessions.
        /// </summary>
        public const string TokenRefreshPolicy = "hub-token-refresh";

        /// <summary>
        /// The instance join and join check endpoints.
        /// </summary>
        public const string HubJoinPolicy = "hub-join";

        /// <summary>
        /// The ACME server endpoints. More permissive, as an ACME client makes many requests per certificate order.
        /// </summary>
        public const string AcmePolicy = "hub-acme";

        private const string ConfigSection = "RateLimiting";

        public static IServiceCollection AddHubRateLimiting(this IServiceCollection services, IConfiguration config)
        {
            var section = config.GetSection(ConfigSection);

            if (section.GetValue<bool?>("enabled") == false)
            {
                // still register the services so the middleware and the policy attributes resolve, but apply no limit
                return services.AddRateLimiter(options =>
                {
                    foreach (var policyName in new[] { AuthPolicy, TokenRefreshPolicy, HubJoinPolicy, AcmePolicy })
                    {
                        options.AddPolicy(policyName, _ => RateLimitPartition.GetNoLimiter("disabled"));
                    }
                });
            }

            var authPermitsPerMinute = section.GetValue<int?>("authRequestsPerMinute") ?? 20;
            var refreshPermitsPerMinute = section.GetValue<int?>("tokenRefreshRequestsPerMinute") ?? 120;
            var joinPermitsPerMinute = section.GetValue<int?>("hubJoinRequestsPerMinute") ?? 60;
            var acmePermitsPerMinute = section.GetValue<int?>("acmeRequestsPerMinute") ?? 300;

            return services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = (context, cancellationToken) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                    }

                    return ValueTask.CompletedTask;
                };

                AddFixedWindowPolicy(options, AuthPolicy, authPermitsPerMinute);
                AddFixedWindowPolicy(options, TokenRefreshPolicy, refreshPermitsPerMinute);
                AddFixedWindowPolicy(options, HubJoinPolicy, joinPermitsPerMinute);
                AddFixedWindowPolicy(options, AcmePolicy, acmePermitsPerMinute);
            });
        }

        private static void AddFixedWindowPolicy(RateLimiterOptions options, string policyName, int permitsPerMinute)
        {
            options.AddPolicy(policyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{policyName}|{GetPartitionKey(httpContext)}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),

                    // requests over the limit are rejected rather than held, so a flood cannot tie up server resources
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
        }

        /// <summary>
        /// Partition by calling IP address.
        ///
        /// Note that behind a reverse proxy every request appears to come from the proxy unless forwarded headers are
        /// configured for the host, in which case the whole partition shares one bucket. Requests with no remote
        /// address are grouped together rather than being left unlimited.
        /// </summary>
        private static string GetPartitionKey(HttpContext httpContext)
        {
            return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
