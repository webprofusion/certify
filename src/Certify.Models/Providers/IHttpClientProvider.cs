using System.Net;
using System.Net.Http;

namespace Certify.Models.Providers
{
    /// <summary>
    /// Factory for creating HttpClient and HttpMessageHandler instances with proxy support
    /// </summary>
    public interface IHttpClientProvider
    {
        /// <summary>
        /// Create an HttpClient configured with proxy settings for external/internet requests
        /// </summary>
        HttpClient CreateClient();

        /// <summary>
        /// Create an HttpClient without proxy for service-to-service communication
        /// </summary>
        HttpClient CreateInternalClient();

        /// <summary>
        /// Create an HttpMessageHandler configured with proxy settings
        /// </summary>
        HttpMessageHandler CreateHandler();

        /// <summary>
        /// Create an HttpMessageHandler without proxy
        /// </summary>
        HttpMessageHandler CreateInternalHandler();
    }
}
