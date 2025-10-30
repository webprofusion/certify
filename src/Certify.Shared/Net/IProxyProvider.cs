using System.Net;

namespace Certify.Shared.Net
{
    /// <summary>
    /// Provides proxy configuration for outbound HTTP requests
    /// </summary>
    public interface IProxyProvider
    {
        /// <summary>
        /// Get the configured proxy, or null if no proxy should be used
        /// </summary>
        /// <returns>IWebProxy instance or null</returns>
        IWebProxy GetProxy();

        /// <summary>
        /// Check if proxy is currently enabled
        /// </summary>
        bool IsProxyEnabled { get; }
    }
}
