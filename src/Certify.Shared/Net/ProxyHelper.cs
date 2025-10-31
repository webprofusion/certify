using System;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Certify.Shared.Net
{
    /// <summary>
    /// Provides simple proxy application from environment variables for outbound HTTP.
    /// Does not touch service-to-service HttpClient factory registrations.
    /// </summary>
    public static class ProxyHelper
    {
        public static IWebProxy GetProxyFromEnvironment()
        {

            var certifyProxy = Environment.GetEnvironmentVariable("CERTIFY_PROXY") ?? Environment.GetEnvironmentVariable("certify_proxy");

            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");

            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");

            var noProxy = Environment.GetEnvironmentVariable("NO_PROXY") ?? Environment.GetEnvironmentVariable("no_proxy");

            var proxyUrl = certifyProxy ?? httpsProxy ?? httpProxy;

            if (string.IsNullOrWhiteSpace(proxyUrl))
            {
                return null;
            }

            try
            {
                var uri = new Uri(proxyUrl);
                var webProxy = new WebProxy(uri)
                {
                    BypassProxyOnLocal = true
                };

                if (!string.IsNullOrWhiteSpace(noProxy))
                {
                    var list = noProxy.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    if (list != null && list.Any())
                    {
                        webProxy.BypassList = list.ToArray();
                    }
                }

                return webProxy;
            }
            catch
            {
                return null;
            }
        }

        public static void ApplyProxyIfConfigured(HttpMessageHandler handler)
        {
            var proxy = GetProxyFromEnvironment();
            if (proxy == null)
            {
                return;
            }

            var http = handler as HttpClientHandler;
            if (http != null)
            {
                http.UseProxy = true;
                http.Proxy = proxy;
            }
        }

        /// <summary>
        /// Apply proxy from an IProxyProvider to an HttpMessageHandler
        /// </summary>
        public static void ApplyProxy(HttpMessageHandler handler, IProxyProvider proxyProvider)
        {
            if (proxyProvider == null || !proxyProvider.IsProxyEnabled)
            {
                return;
            }

            var proxy = proxyProvider.GetProxy();
            if (proxy == null)
            {
                return;
            }

            var http = handler as HttpClientHandler;
            if (http != null)
            {
                http.UseProxy = true;
                http.Proxy = proxy;

                if (proxy.Credentials != null)
                {
                    http.DefaultProxyCredentials = proxy.Credentials;
                }
            }
        }
    }
}
