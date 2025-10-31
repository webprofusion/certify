using System.Net;
using System.Net.Http;
using Certify.Models.Providers;
using Certify.Shared.Core.Utils;

namespace Certify.Shared.Net
{
    /// <summary>
    /// Default implementation of IHttpClientProvider
    /// </summary>
    public class HttpClientProvider : IHttpClientProvider
    {
        private readonly IProxyProvider _proxyProvider;

        public HttpClientProvider(IProxyProvider proxyProvider)
        {
            _proxyProvider = proxyProvider;
        }

        public HttpClient CreateClient()
        {
            return new HttpClient(CreateHandler());
        }

        public HttpClient CreateClient(string? userAgent = null, bool allowInvalidTls = false)
        {
            var client = new HttpClient(CreateHandler(allowInvalidTls));

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                client.DefaultRequestHeaders.Remove("User-Agent");
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);
            }

            return client;
        }

        public HttpClient CreateInternalClient()
        {
            return new HttpClient(CreateInternalHandler());
        }

        public HttpMessageHandler CreateHandler(bool allowInvalidTls = false)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (allowInvalidTls)
            {
#if NET9_0_OR_GREATER
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#else
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true;                
#endif
            }

            if (_proxyProvider.IsProxyEnabled)
            {
                var proxy = _proxyProvider.GetProxy();
                if (proxy != null)
                {
                    handler.UseProxy = true;
                    handler.Proxy = proxy;

                    if (proxy.Credentials != null)
                    {
                        handler.DefaultProxyCredentials = proxy.Credentials;
                    }
                }
            }

            return handler;
        }

        public HttpMessageHandler CreateInternalHandler()
        {
            return new HttpClientHandler
            {
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
        }
    }
}
