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

        public HttpClient CreateInternalClient()
        {
            return new HttpClient(CreateInternalHandler());
        }

        public HttpMessageHandler CreateHandler()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

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
