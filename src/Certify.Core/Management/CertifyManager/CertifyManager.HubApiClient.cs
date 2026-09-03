using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        private sealed class HubApiRequestContext
        {
            public required string ClientId { get; init; }
            public required string Secret { get; init; }
            public string? HubAssignedInstanceId { get; init; }
            public string? InstanceVersion { get; init; }
            public string? RequestAuthSecret { get; init; }
            public string? IfNoneMatch { get; init; }
            public string? TraceInstanceName { get; init; }
        }

        private static HttpClientHandler CreateHubApiMessageHandler()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            if (Environment.GetEnvironmentVariable("CERTIFY_MANAGEMENT_HUB_ALLOW_UNTRUSTED") == "true")
            {
                handler.ServerCertificateCustomValidationCallback = null;
            }

            return handler;
        }

        private Certify.Server.Hub.Api.Client GetHubApiClient(string hubApiBase)
        {
            var normalizedBaseUrl = hubApiBase.TrimEnd('/') + "/";

            if (_hubApiHttpClient == null || _hubApiClient == null)
            {
                // a supplied handler is owned by whoever supplied it, so it is not disposed along with the client
                _hubApiHttpClient = _hubApiMessageHandler != null
                    ? new HttpClient(_hubApiMessageHandler, disposeHandler: false)
                    : new HttpClient(CreateHubApiMessageHandler());
                _hubApiClient = new Certify.Server.Hub.Api.Client(_hubApiHttpClient)
                {
                    BaseUrl = normalizedBaseUrl
                };
            }
            else if (!string.Equals(_hubApiClient.BaseUrl, normalizedBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                _hubApiClient.BaseUrl = normalizedBaseUrl;
            }

            return _hubApiClient;
        }

        private async Task<T> UseHubApiClient<T>(string hubApiBase, HubApiRequestContext requestContext, Func<Certify.Server.Hub.Api.Client, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            // we lock the client so that each request can optionally use different credentials.

            await _hubApiClientLock.WaitAsync(cancellationToken);

            try
            {
                var client = GetHubApiClient(hubApiBase);

                _hubApiHttpClient!.DefaultRequestHeaders.Remove("X-Client-ID");
                _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Client-Secret");
                _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Certify-HubAssignedId");
                _hubApiHttpClient.DefaultRequestHeaders.Remove(Models.Hub.ManagedInstanceRequestAuth.InstanceVersionHeaderName);
                _hubApiHttpClient.DefaultRequestHeaders.Remove("If-None-Match");
                _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Certify-Trace-InstanceName");

                _hubApiHttpClient.DefaultRequestHeaders.Add("X-Client-ID", requestContext.ClientId);
                _hubApiHttpClient.DefaultRequestHeaders.Add("X-Client-Secret", requestContext.Secret);

                client.ManagedInstanceRequestAuthInstanceId = requestContext.HubAssignedInstanceId;
                client.ManagedInstanceRequestAuthSecret = requestContext.RequestAuthSecret;

                if (!string.IsNullOrWhiteSpace(requestContext.HubAssignedInstanceId))
                {
                    _hubApiHttpClient.DefaultRequestHeaders.Add("X-Certify-HubAssignedId", requestContext.HubAssignedInstanceId);
                }

                if (!string.IsNullOrWhiteSpace(requestContext.InstanceVersion))
                {
                    _hubApiHttpClient.DefaultRequestHeaders.TryAddWithoutValidation(Models.Hub.ManagedInstanceRequestAuth.InstanceVersionHeaderName, requestContext.InstanceVersion);
                }

                if (!string.IsNullOrWhiteSpace(requestContext.IfNoneMatch))
                {
                    _hubApiHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("If-None-Match", requestContext.IfNoneMatch);
                }

                if (!string.IsNullOrWhiteSpace(requestContext.TraceInstanceName))
                {
                    _hubApiHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Certify-Trace-InstanceName", requestContext.TraceInstanceName);
                }

                return await action(client, cancellationToken);
            }
            finally
            {
                if (_hubApiClient != null)
                {
                    _hubApiClient.ManagedInstanceRequestAuthInstanceId = null;
                    _hubApiClient.ManagedInstanceRequestAuthSecret = null;
                }

                if (_hubApiHttpClient != null)
                {
                    _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Client-ID");
                    _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Client-Secret");
                    _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Certify-HubAssignedId");
                    _hubApiHttpClient.DefaultRequestHeaders.Remove(Models.Hub.ManagedInstanceRequestAuth.InstanceVersionHeaderName);
                    _hubApiHttpClient.DefaultRequestHeaders.Remove("If-None-Match");
                    _hubApiHttpClient.DefaultRequestHeaders.Remove("X-Certify-Trace-InstanceName");
                }

                _hubApiClientLock.Release();
            }
        }

        private async Task<byte[]> ReadHubApiFileResponse(Certify.Server.Hub.Api.FileResponse response, CancellationToken cancellationToken)
        {
            await using var stream = response.Stream;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            return ms.ToArray();
        }

        private static string? GetHubApiHeaderValue(Certify.Server.Hub.Api.FileResponse response, string headerName)
        {
            if (response.Headers.TryGetValue(headerName, out var values))
            {
                return values?.FirstOrDefault()?.Replace("\"", string.Empty);
            }

            return null;
        }
    }
}
