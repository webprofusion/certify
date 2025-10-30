using System;
using System.Linq;
using System.Net;
using Certify.Models;

namespace Certify.Shared.Net
{
    /// <summary>
    /// Default implementation of IProxyProvider that reads from Preferences and environment
    /// </summary>
    public class ProxyProvider : IProxyProvider
    {
        private readonly Func<Preferences> _getPreferences;

        /// <summary>
        /// Create a proxy provider with a function to get preferences
        /// </summary>
        /// <param name="getPreferences">Function to retrieve current proxy preferences</param>
        public ProxyProvider(Func<Preferences> getPreferences = null)
        {
            _getPreferences = getPreferences ?? (() => new Preferences { ProxyMode = ProxyMode.Environment, ProxyEnabled = true });
        }

        public bool IsProxyEnabled
        {
            get
            {
                var prefs = _getPreferences();
                return prefs?.ProxyEnabled == true && prefs.ProxyMode != ProxyMode.None;
            }
        }

        public IWebProxy GetProxy()
        {
            var prefs = _getPreferences();
            if (prefs == null || !prefs.ProxyEnabled || prefs.ProxyMode == ProxyMode.None)
            {
                return null;
            }

            IWebProxy proxy = null;

            switch (prefs.ProxyMode)
            {
                case ProxyMode.System:
                    proxy = WebRequest.DefaultWebProxy;
                    break;

                case ProxyMode.Environment:
                    proxy = ProxyHelper.GetProxyFromEnvironment();
                    break;

                case ProxyMode.Custom:
                    if (!string.IsNullOrWhiteSpace(prefs.ProxyUri))
                    {
                        try
                        {
                            var uri = new Uri(prefs.ProxyUri);
                            var webProxy = new WebProxy(uri)
                            {
                                BypassProxyOnLocal = prefs.ProxyBypassOnLocal
                            };

                            if (!string.IsNullOrWhiteSpace(prefs.ProxyBypassList))
                            {
                                var bypassList = prefs.ProxyBypassList
                                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim())
                                         .Where(s => !string.IsNullOrEmpty(s))
                                    .ToArray();

                                if (bypassList.Length > 0)
                                {
                                    webProxy.BypassList = bypassList.ToArray();
                                }
                            }

                            proxy = webProxy;
                        }
                        catch
                        {
                            // Invalid proxy URI, return null
                            return null;
                        }
                    }

                    break;
            }

            if (proxy != null)
            {
                // Apply credentials if configured
                if (prefs.ProxyUseDefaultCredentials)
                {
                    proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                }

                // Note: Credentials from StoredCredential would need to be loaded async
                // For now, proxy auth via credentials requires DefaultNetworkCredentials
                // TODO: Add support for custom credentials from StoredCredential
            }

            return proxy;
        }
    }
}
