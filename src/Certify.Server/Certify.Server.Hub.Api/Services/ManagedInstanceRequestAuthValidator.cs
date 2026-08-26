using System;
using System.IO;
using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Http;

namespace Certify.Server.Hub.Api.Services
{
    public sealed class ManagedInstanceRequestAuthValidationResult
    {
        public bool IsSuccess { get; init; }
        public int StatusCode { get; init; } = StatusCodes.Status401Unauthorized;
        public string Message { get; init; } = "Managed instance request authentication failed.";
        public ManagedInstanceInfo? ManagedInstance { get; init; }
    }

    public class ManagedInstanceRequestAuthValidator
    {
        /// <summary>
        /// Configuration key which re-enables acceptance of unsigned requests from instances registered without a
        /// request auth secret. Off unless explicitly set, because when it is on an instance id is the only thing
        /// needed to authenticate as that instance.
        /// </summary>
        public const string AllowLegacyUnsignedRequestsConfigKey = "ManagedInstanceRequestAuth:AllowLegacyUnsignedRequests";

        private readonly ICertifyInternalApiClient _client;
        private readonly ILogger<ManagedInstanceRequestAuthValidator> _logger;
        private readonly bool _allowLegacyUnsignedRequests;
        private static readonly AuthContext _systemAuthContext = new AuthContext { UserId = StandardSecurityPrincipals.System };
        private static int _legacyFallbackCount;

        public ManagedInstanceRequestAuthValidator(ICertifyInternalApiClient client, ILogger<ManagedInstanceRequestAuthValidator> logger, IConfiguration? configuration = null)
        {
            _client = client;
            _logger = logger;
            _allowLegacyUnsignedRequests = configuration?.GetValue<bool>(AllowLegacyUnsignedRequestsConfigKey) == true;
        }

        public async Task<ManagedInstanceRequestAuthValidationResult> ValidateAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            var instanceId = request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                _logger.LogWarning("Managed instance request auth failed: missing {headerName} header.", ManagedInstanceRequestAuth.HubAssignedIdHeaderName);
                return Fail("X-Certify-HubAssignedId header is required.");
            }

            var instance = await _client.GetHubManagedInstance(instanceId, _systemAuthContext);
            if (instance == null)
            {
                _logger.LogWarning("Managed instance request auth failed: managed instance {instanceId} is not registered.", instanceId);
                return Fail("Managed instance is not registered.");
            }

            if (string.IsNullOrWhiteSpace(instance.RequestAuthSecretHash))
            {
                // Without a secret there is nothing to verify a signature against, so accepting the request would
                // reduce authentication to presenting the instance id in a header. Instances are issued a secret when
                // they check in with the hub, so this state should resolve itself; until it does, fail closed.
                if (!_allowLegacyUnsignedRequests)
                {
                    _logger.LogWarning(
                        "Managed instance request auth failed for {instanceId}: no request auth secret is configured for this instance. The instance should re-check in with the hub to be issued one. Unsigned requests can be temporarily re-enabled with {configKey}.",
                        instanceId,
                        AllowLegacyUnsignedRequestsConfigKey);

                    return Fail("Managed instance has no request auth secret configured. Re-register the instance with the hub to be issued one.");
                }

                var legacyFallbackCount = Interlocked.Increment(ref _legacyFallbackCount);

                _logger.LogWarning("Managed instance request auth secret is not configured for {instanceId}, allowing legacy request authentication because {configKey} is enabled. This permits any caller which knows the instance id to authenticate as that instance. Legacy fallback count: {legacyFallbackCount}", instanceId, AllowLegacyUnsignedRequestsConfigKey, legacyFallbackCount);

                return new ManagedInstanceRequestAuthValidationResult
                {
                    IsSuccess = true,
                    Message = "Managed instance request authentication secret is not configured. Allowing legacy request authentication.",
                    ManagedInstance = instance
                };
            }

            var timestamp = request.Headers[ManagedInstanceRequestAuth.TimestampHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(timestamp))
            {
                _logger.LogWarning("Managed instance request auth failed for {instanceId}: missing {headerName} header.", instanceId, ManagedInstanceRequestAuth.TimestampHeaderName);
                return Fail("X-Certify-Timestamp header is required.");
            }

            var signature = request.Headers[ManagedInstanceRequestAuth.SignatureHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(signature))
            {
                _logger.LogWarning("Managed instance request auth failed for {instanceId}: missing {headerName} header.", instanceId, ManagedInstanceRequestAuth.SignatureHeaderName);
                return Fail("X-Certify-Signature header is required.");
            }

            if (!ManagedInstanceRequestAuth.TryParseTimestamp(timestamp, out var timestampValue))
            {
                _logger.LogWarning("Managed instance request auth failed for {instanceId}: invalid timestamp {timestamp}.", instanceId, timestamp);
                return Fail("X-Certify-Timestamp header is invalid.");
            }

            if (Math.Abs((DateTimeOffset.UtcNow - timestampValue).TotalMinutes) > ManagedInstanceRequestAuth.DefaultAllowedClockSkew.TotalMinutes)
            {
                _logger.LogWarning("Managed instance request auth failed for {instanceId}: timestamp {timestamp} is outside the allowed clock skew window.", instanceId, timestamp);
                return Fail("X-Certify-Timestamp is outside the allowed clock skew window.");
            }

            var bodyHash = await ResolveBodyHashAsync(request, cancellationToken);
            var requestPathAndQuery = request.Path.HasValue ? request.Path.Value! : "/";
            if (request.QueryString.HasValue)
            {
                requestPathAndQuery += request.QueryString.Value;
            }

            var expectedSignature = ManagedInstanceRequestAuth.ComputeSignatureFromSecretHash(
                instance.RequestAuthSecretHash,
                instance.InstanceId,
                timestamp,
                request.Method,
                requestPathAndQuery,
                bodyHash);

            if (!ManagedInstanceRequestAuth.FixedTimeEquals(signature, expectedSignature))
            {
                _logger.LogWarning("Managed instance request signature validation failed for {instanceId} {method} {path}", instanceId, request.Method, requestPathAndQuery);
                return Fail("Managed instance request signature is invalid.");
            }

            return new ManagedInstanceRequestAuthValidationResult
            {
                IsSuccess = true,
                Message = "Managed instance request authentication succeeded.",
                ManagedInstance = instance
            };
        }

        private async Task<string> ResolveBodyHashAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (request.HttpContext.Items.TryGetValue(ManagedInstanceRequestAuth.CachedBodyHashItemKey, out var cachedHash)
                && cachedHash is string bodyHash
                && !string.IsNullOrWhiteSpace(bodyHash))
            {
                return bodyHash;
            }

            // bounded for the same reason as the body hash middleware, this path runs when that middleware did not
            // already cache a hash for the request
            request.EnableBuffering(
                Middleware.ManagedInstanceRequestBodyLimits.MemoryBufferThresholdBytes,
                Middleware.ManagedInstanceRequestBodyLimits.MaxBodyBytes);

            request.Body.Position = 0;

            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, cancellationToken);
            request.Body.Position = 0;

            return ManagedInstanceRequestAuth.ComputeBodyHash(ms.ToArray());
        }

        private static ManagedInstanceRequestAuthValidationResult Fail(string message)
        {
            return new ManagedInstanceRequestAuthValidationResult
            {
                IsSuccess = false,
                Message = message
            };
        }
    }
}
