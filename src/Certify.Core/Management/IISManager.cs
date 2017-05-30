using Microsoft.Web.Administration;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;
using System.Globalization;

namespace Certify.Management
{
    /// <summary>
    /// Model to work with IIS site details.
    /// </summary>

    public class IISManager
    {
        #region IIS

        // private readonly bool _showOnlyStartedWebsites = Properties.Settings.Default.ShowOnlyStartedWebsites;
        private readonly IdnMapping _idnMapping = new IdnMapping();

        public Version GetIisVersion()
        {
            //http://stackoverflow.com/questions/446390/how-to-detect-iis-version-using-c
            using (RegistryKey componentsKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\InetStp", false))
            {
                if (componentsKey != null)
                {
                    int majorVersion = (int)componentsKey.GetValue("MajorVersion", -1);
                    int minorVersion = (int)componentsKey.GetValue("MinorVersion", -1);

                    if (majorVersion != -1 && minorVersion != -1)
                    {
                        return new Version(majorVersion, minorVersion);
                    }
                }

                return new Version(0, 0);
            }
        }

        private ServerManager GetDefaultServerManager()
        {
            return new ServerManager(); //(@"C:\Windows\System32\inetsrv\config\applicationHost.config"
        }

        public IEnumerable<Site> GetSites(ServerManager iisManager, bool includeOnlyStartedSites)
        {
            return includeOnlyStartedSites
                ? iisManager.Sites.Where(s => s.State == ObjectState.Started)
                : iisManager.Sites;
        }

        /// <summary>
        /// Return list of sites (non-specific bindings)
        /// </summary>
        /// <param name="includeOnlyStartedSites"></param>
        /// <returns></returns>
        public List<SiteBindingItem> GetPrimarySites(bool includeOnlyStartedSites)
        {
            var result = new List<SiteBindingItem>();

            using (var iisManager = GetDefaultServerManager())
            {
                var sites = GetSites(iisManager, includeOnlyStartedSites);

                foreach (var site in sites)
                {
                    if (site != null)
                    {
                        var b = new SiteBindingItem()
                        {
                            SiteId = site.Id.ToString(),
                            SiteName = site.Name
                        };

                        b.PhysicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;

                        b.IsEnabled = (site.State == ObjectState.Started);
                        result.Add(b);
                    }
                }
            }

            return result.OrderBy(s => s.SiteName).ToList();
        }

        public List<SiteBindingItem> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
        {
            var result = new List<SiteBindingItem>();

            using (var iisManager = GetDefaultServerManager())
            {
                var sites = GetSites(iisManager, ignoreStoppedSites);

                if (siteId != null) sites = sites.Where(s => s.Id.ToString() == siteId);
                foreach (var site in sites)
                {
                    foreach (var binding in site.Bindings.OrderByDescending(b => b?.EndPoint?.Port))
                    {
                        //if (string.IsNullOrEmpty(binding.Host)) continue;
                        // if (result.Any(r => r.Host == binding.Host)) continue;

                        result.Add(GetSiteBinding(site, binding));
                    }
                }
            }

            return result.OrderBy(r => r.Description).ToList();
        }

        private SiteBindingItem GetSiteBinding(Site site, Binding binding)
        {
            return new SiteBindingItem()
            {
                SiteId = site.Id.ToString(),
                SiteName = site.Name,
                Host = binding.Host,
                PhysicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath,
                Port = binding.EndPoint.Port,
                IsHTTPS = binding.Protocol.ToLower() == "https",
                Protocol = binding.Protocol,
                HasCertificate = (binding.CertificateHash != null)
            };
        }

        private Site GetSite(string siteName, ServerManager iisManager)
        {
            return GetSites(iisManager, false).FirstOrDefault(s => s.Name == siteName);
        }

        public Site GetSiteByDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return null;

            domain = _idnMapping.GetUnicode(domain);
            using (var iisManager = GetDefaultServerManager())
            {
                var sites = GetSites(iisManager, false).ToList();
                foreach (var s in sites)
                {
                    foreach (var b in s.Bindings)
                    {
                        if (b.Host.Equals(domain, StringComparison.InvariantCultureIgnoreCase))
                        {
                            return s;
                        }
                    }
                }
            }
            return null;
        }

        public SiteBindingItem GetSiteBindingByDomain(string domain)
        {
            domain = _idnMapping.GetUnicode(domain);

            var site = GetSiteByDomain(domain);
            if (site != null)
            {
                foreach (var binding in site.Bindings.OrderByDescending(b => b?.EndPoint?.Port))
                {
                    if (binding.Host.Equals(domain, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return GetSiteBinding(site, binding);
                    }
                }
            }
            //no match
            return null;
        }

        /// <summary>
        /// Create a new IIS site with the given default host name, path, app pool
        /// </summary>
        /// <param name="siteName"></param>
        /// <param name="hostname"></param>
        /// <param name="phyPath"></param>
        /// <param name="appPoolName"></param>
        public void CreateSite(string siteName, string hostname, string phyPath, string appPoolName, string protocol = "http", string ipAddress = "*", int? port = 80)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                iisManager.Sites.Add(siteName, protocol, ipAddress + ":" + port + ":" + hostname, phyPath);
                iisManager.Sites[siteName].ApplicationDefaults.ApplicationPoolName = appPoolName;

                foreach (var item in iisManager.Sites[siteName].Applications)
                {
                    item.ApplicationPoolName = appPoolName;
                }

                iisManager.CommitChanges();
            }
        }

        /// <summary>
        /// Check if site with given site name exists
        /// </summary>
        /// <param name="siteName"></param>
        /// <returns></returns>
        public bool SiteExists(string siteName)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                return (iisManager.Sites[siteName] != null);
            }
        }

        public void DeleteSite(string siteName)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                Site siteToRemove = iisManager.Sites[siteName];

                iisManager.Sites.Remove(siteToRemove);
                iisManager.CommitChanges();
            }
        }

        public bool IsSiteRunning(string id)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                Site siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
                if (siteDetails.State == ObjectState.Started)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        #endregion IIS

        #region Certificates

        /// <summary>
        /// Creates or updates the htttps bindings associated with the dns names in the current
        /// request config, using the requested port/ips or autobinding
        /// </summary>
        /// <param name="requestConfig"></param>
        /// <param name="pfxPath"></param>
        /// <param name="cleanupCertStore"></param>
        /// <returns></returns>
        internal bool InstallCertForRequest(CertRequestConfig requestConfig, string pfxPath, bool cleanupCertStore)
        {
            if (new System.IO.FileInfo(pfxPath).Length == 0)
            {
                throw new ArgumentException("InstallCertForRequest: Invalid PFX File");
            }

            //store cert against primary domain
            var storedCert = new CertificateManager().StoreCertificate(requestConfig.PrimaryDomain, pfxPath);

            if (storedCert != null)
            {
                List<string> dnsHosts = new List<string> { requestConfig.PrimaryDomain };
                if (requestConfig.SubjectAlternativeNames != null) dnsHosts.AddRange(requestConfig.SubjectAlternativeNames);
                dnsHosts = dnsHosts.Distinct().ToList();

                foreach (var hostname in dnsHosts)
                {
                    //match dns host to IIS site
                    var site = GetSiteByDomain(hostname);
                    if (site != null)
                    {
                        //create/update binding and associate new cert
                        if (!requestConfig.PerformAutomatedCertBinding)
                        {
                            //create auto binding and use SNI
                            InstallCertificateforBinding(site, storedCert, hostname);
                        }
                        else
                        {
                            //if any binding elements configured, use those, otherwise auto bind using defaults and SNI
                            InstallCertificateforBinding(site, storedCert, hostname,
                                sslPort: !String.IsNullOrEmpty(requestConfig.BindingPort) ? int.Parse(requestConfig.BindingPort) : 443,
                                useSNI: (requestConfig.BindingUseSNI != null ? (bool)requestConfig.BindingUseSNI : true),
                                ipAddress: requestConfig.BindingIPAddress
                                );
                        }
                    }
                }

                if (cleanupCertStore)
                {
                    //remove old certs for this primary domain
                    new CertificateManager().CleanupCertificateDuplicates(storedCert, requestConfig.PrimaryDomain);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// creates or updates the https binding for the dns host name specified, assigning the given
        /// certificate selected from the certificate store
        /// </summary>
        /// <param name="site"></param>
        /// <param name="certificate"></param>
        /// <param name="host"></param>
        /// <param name="sslPort"></param>
        /// <param name="useSNI"></param>
        /// <param name="ipAddress"></param>
        public void InstallCertificateforBinding(Site site, X509Certificate2 certificate, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null)
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            using (var iisManager = GetDefaultServerManager())
            {
                var siteToUpdate = iisManager.Sites.FirstOrDefault(s => s.Id == site.Id);
                if (siteToUpdate != null)
                {
                    string internationalHost = _idnMapping.GetUnicode(host);
                    var existingBinding = (from b in siteToUpdate.Bindings where b.Host == internationalHost && b.Protocol == "https" select b).FirstOrDefault();

                    if (existingBinding != null)
                    {
                        // Update existing https Binding
                        existingBinding.CertificateHash = certificate.GetCertHash();
                        existingBinding.CertificateStoreName = store.Name;
                    }
                    else
                    {
                        //add new https binding at default port "<ip>:port:hostDnsName";
                        string bindingSpec = (ipAddress != null ? ipAddress : "") +
                            ":" + sslPort + ":" + internationalHost;
                        var iisBinding = siteToUpdate.Bindings.Add(bindingSpec, certificate.GetCertHash(), store.Name);

                        iisBinding.Protocol = "https";
                        if (useSNI)
                        {
                            try
                            {
                                iisBinding["sslFlags"] = 1; //enable sni
                            }
                            catch (Exception)
                            {
                                ; ;
                                System.Diagnostics.Debug.WriteLine("Cannot apply SNI SSL Flag");
                            }
                        }
                    }
                }

                iisManager.CommitChanges();
                store.Close();
            }
        }

        public bool InstallCertForDomain(string hostDnsName, string pfxPath, bool cleanupCertStore = true, bool skipBindings = false)
        {
            //gets the IIS site associated with this dns host name (or first, if multiple defined)
            var site = GetSiteByDomain(hostDnsName);
            if (site != null)
            {
                if (new System.IO.FileInfo(pfxPath).Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("InstallCertForDomain: Invalid PFX File");
                    return false;
                }
                var storedCert = new CertificateManager().StoreCertificate(hostDnsName, pfxPath);
                if (storedCert != null)
                {
                    if (!skipBindings)
                    {
                        InstallCertificateforBinding(site, storedCert, hostDnsName);
                    }
                    if (cleanupCertStore)
                    {
                        new CertificateManager().CleanupCertificateDuplicates(storedCert, hostDnsName);
                    }

                    return true;
                }
            }

            return false;
        }

        #endregion Certificates
    }
}