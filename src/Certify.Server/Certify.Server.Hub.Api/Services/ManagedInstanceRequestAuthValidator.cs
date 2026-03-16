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
        private readonly ICertifyInternalApiClient _client;
        private readonly ILogger<ManagedInstanceRequestAuthValidator> _logger;
        private static readonly AuthContext _systemAuthContext = new AuthContext { UserId = "system" };
        private static int _legacyFallbackCount;

        public ManagedInstanceRequestAuthValidator(ICertifyInternalApiClient client, ILogger<ManagedInstanceRequestAuthValidator> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<ManagedInstanceRequestAuthValidationResult> ValidateAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            var instanceId = request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return Fail("X-Certify-HubAssignedId header is required.");
            }

            var instance = await _client.GetHubManagedInstance(instanceId, _systemAuthContext);
            if (instance == null)
            {
                return Fail("Managed instance is not registered.");
            }

            if (string.IsNullOrWhiteSpace(instance.RequestAuthSecretHash))
            {
                var legacyFallbackCount = Interlocked.Increment(ref _legacyFallbackCount);

                _logger.LogWarning("Managed instance request auth secret is not configured for {instanceId}, allowing legacy request authentication. Legacy fallback count: {legacyFallbackCount}", instanceId, legacyFallbackCount);

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
                return Fail("X-Certify-Timestamp header is required.");
            }

            var signature = request.Headers[ManagedInstanceRequestAuth.SignatureHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(signature))
            {
                return Fail("X-Certify-Signature header is required.");
            }

            if (!ManagedInstanceRequestAuth.TryParseTimestamp(timestamp, out var timestampValue))
            {
                return Fail("X-Certify-Timestamp header is invalid.");
            }

            if (Math.Abs((DateTimeOffset.UtcNow - timestampValue).TotalMinutes) > ManagedInstanceRequestAuth.DefaultAllowedClockSkew.TotalMinutes)
            {
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

            request.EnableBuffering();
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
