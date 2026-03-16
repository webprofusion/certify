using System;
using System.Globalization;
using System.Net.Http;
using Certify.Models.Hub;

namespace Certify.Server.Hub.Api
{
    public partial class Client
    {
        public string? ManagedInstanceRequestAuthInstanceId { get; set; }
        public string? ManagedInstanceRequestAuthSecret { get; set; }

        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
        {
            if (string.IsNullOrWhiteSpace(ManagedInstanceRequestAuthInstanceId)
                || string.IsNullOrWhiteSpace(ManagedInstanceRequestAuthSecret))
            {
                return;
            }

            var bodyBytes = request.Content != null
                ? request.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult()
                : Array.Empty<byte>();

            var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash(bodyBytes);
            var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var requestPathAndQuery = GetRequestPathAndQuery(request, url);
            var signature = ManagedInstanceRequestAuth.ComputeSignatureFromSecret(
                ManagedInstanceRequestAuthSecret,
                ManagedInstanceRequestAuthInstanceId,
                timestamp,
                request.Method.Method,
                requestPathAndQuery,
                bodyHash);

            request.Headers.Remove(ManagedInstanceRequestAuth.TimestampHeaderName);
            request.Headers.Remove(ManagedInstanceRequestAuth.SignatureHeaderName);
            request.Headers.TryAddWithoutValidation(ManagedInstanceRequestAuth.TimestampHeaderName, timestamp);
            request.Headers.TryAddWithoutValidation(ManagedInstanceRequestAuth.SignatureHeaderName, signature);
        }

        partial void PrepareRequest(HttpClient client, HttpRequestMessage request, System.Text.StringBuilder urlBuilder)
        {
        }

        private static string GetRequestPathAndQuery(HttpRequestMessage request, string url)
        {
            var requestUri = request.RequestUri;
            if (requestUri != null)
            {
                return requestUri.IsAbsoluteUri ? requestUri.PathAndQuery : EnsureLeadingSlash(requestUri.ToString());
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.PathAndQuery;
            }

            return EnsureLeadingSlash(url);
        }

        private static string EnsureLeadingSlash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "/";
            }

            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value.TrimStart('/');
        }
    }
}
