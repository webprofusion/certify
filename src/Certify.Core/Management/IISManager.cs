using Certify.Models;
using Microsoft.Web.Administration;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

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

        private bool _isIISAvailable { get; set; }

        public bool IsIISAvailable
        {
            get
            {
                if (!_isIISAvailable)
                {
                    using (var srv = GetDefaultServerManager())
                    {
                        // _isIISAvaillable will be updated by query against server manager
                        if (srv != null) return _isIISAvailable;
                    }
                }

                return _isIISAvailable;
            }
        }

        public async Task<bool> IsIISAvailableAsync()
        {
            // FIXME: blocking async
            var isAvailable = false;
            using (var srv = GetDefaultServerManager())
            {
                if (srv != null) isAvailable = true;
            }
            return await Task.FromResult(isAvailable);
        }

        public async Task<Version> GetIisVersionAsync()
        {
            // FIXME: blocking async

            return await Task.FromResult(GetIisVersion());
        }

        public Version GetIisVersion()
        {
            //http://stackoverflow.com/questions/446390/how-to-detect-iis-version-using-c
            using (RegistryKey componentsKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\InetStp", false))
            {
                if (componentsKey != null)
                {
                    _isIISAvailable = true;

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
            ServerManager srv = null;
            try
            {
                srv = new ServerManager();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                try
                {
                    srv = new ServerManager(@"C:\Windows\System32\inetsrv\config\applicationHost.config");
                }
                catch (Exception)
                {
                    // IIS is probably not installed
                }
            }

            _isIISAvailable = false;
            if (srv != null)
            {
                //check iis version
                var v = GetIisVersion();
                if (v.Major < 7)
                {
                    _isIISAvailable = false;
                }
            }
            // may be null if could not create server manager
            return srv;
        }

        public IEnumerable<Site> GetSites(ServerManager iisManager, bool includeOnlyStartedSites)
        {
            try
            {
                if (includeOnlyStartedSites)
                {
                    //s.State may throw a com exception for sites in an invalid state.

                    return iisManager.Sites.Where(s =>
                    {
                        try
                        {
                            return s.State == ObjectState.Started;
                        }
                        catch (Exception)
                        {
                            return false;
                        }
                    });
                }
                else
                {
                    return iisManager.Sites;
                }
            }
            catch (Exception)
            {
                //failed to enumerate sites
                return new List<Site>();
            }
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
                if (iisManager != null)
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

                            try
                            {
                                b.IsEnabled = (site.State == ObjectState.Started);
                            }
                            catch (Exception)
                            {
                                System.Diagnostics.Debug.WriteLine("Exception reading IIS Site state value:" + site.Name);
                            }

                            result.Add(b);
                        }
                    }
                }
            }

            return result.OrderBy(s => s.SiteName).ToList();
        }

        public void AddSiteBindings(string siteId, List<string> domains)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                var site = iisManager.Sites.FirstOrDefault(s => s.Id == long.Parse(siteId));
                foreach (var d in domains)
                {
                    site.Bindings.Add("*:80:" + d, "http");
                }
                iisManager.CommitChanges();
            }
        }

        public List<SiteBindingItem> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
        {
            var result = new List<SiteBindingItem>();

            using (var iisManager = GetDefaultServerManager())
            {
                if (iisManager != null)
                {
                    var sites = GetSites(iisManager, ignoreStoppedSites);

                    if (siteId != null) sites = sites.Where(s => s.Id.ToString() == siteId);
                    foreach (var site in sites)
                    {
                        foreach (var binding in site.Bindings.OrderByDescending(b => b?.EndPoint?.Port))
                        {
                            var bindingDetails = GetSiteBinding(site, binding);

                            //ignore bindings which are not http or https
                            if (bindingDetails.Protocol?.ToLower().StartsWith("http") == true)
                            {
                                result.Add(bindingDetails);
                            }
                        }
                    }
                }
            }

            return result.OrderBy(r => r.SiteName).ToList();
        }

        public string GetSitePhysicalPath(ManagedSite managedSite)
        {
            return GetSitePhysicalPath(FindManagedSite(managedSite));
        }

        private string GetSitePhysicalPath(Site site)
        {
            return site?.Applications["/"].VirtualDirectories["/"].PhysicalPath;
        }

        private SiteBindingItem GetSiteBinding(Site site, Binding binding)
        {
            return new SiteBindingItem()
            {
                SiteId = site.Id.ToString(),
                SiteName = site.Name,
                Host = binding.Host,
                IP = binding.EndPoint?.Address?.ToString(),
                PhysicalPath = GetSitePhysicalPath(site),
                Port = binding.EndPoint?.Port,
                IsHTTPS = binding.Protocol.ToLower() == "https",
                Protocol = binding.Protocol,
                HasCertificate = (binding.CertificateHash != null)
            };
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
                // usual binding format is ip:port:dnshostname but can also be *:port,
                // *:port:hostname or just hostname
                string bindingInformation = (ipAddress != null ? (ipAddress + ":") : "")
                    + (port != null ? (port + ":") : "")
                    + hostname;

                iisManager.Sites.Add(siteName, protocol, bindingInformation, phyPath);
                if (appPoolName != null)
                {
                    iisManager.Sites[siteName].ApplicationDefaults.ApplicationPoolName = appPoolName;

                    foreach (var item in iisManager.Sites[siteName].Applications)
                    {
                        item.ApplicationPoolName = appPoolName;
                    }
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

        public Site GetSiteById(string id)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                Site siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
                return siteDetails;
            }
        }

        public bool IsSiteRunning(string id)
        {
            using (var iisManager = GetDefaultServerManager())
            {
                Site siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);

                if (siteDetails?.State == ObjectState.Started)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Finds the IIS <see cref="Site" /> corresponding to a <see cref="ManagedSite" />. 
        /// </summary>
        /// <param name="managedSite"> Configured site. </param>
        /// <returns> The matching IIS Site if found, otherwise null. </returns>
        private Site FindManagedSite(ManagedSite managedSite)
        {
            if (managedSite == null)
                throw new ArgumentNullException(nameof(managedSite));

            var site = GetSiteById(managedSite.GroupId);

            if (site != null)
            {
                //TODO: check site has bindings for given domains, otherwise set back to null
            }

            if (site == null)
            {
                site = GetSiteByDomain(managedSite.RequestConfig.PrimaryDomain);
            }

            return site;
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
        internal async Task<bool> InstallCertForRequest(ManagedSite managedSite, string pfxPath, bool cleanupCertStore)
        {
            var requestConfig = managedSite.RequestConfig;

            if (new System.IO.FileInfo(pfxPath).Length == 0)
            {
                throw new ArgumentException("InstallCertForRequest: Invalid PFX File");
            }

            //store cert against primary domain
            var storedCert = await CertificateManager.StoreCertificate(requestConfig.PrimaryDomain, pfxPath);

            if (storedCert != null)
            {
                var site = FindManagedSite(managedSite);

                //get list of domains we need to create/update https bindings for
                List<string> dnsHosts = new List<string> { requestConfig.PrimaryDomain };
                if (requestConfig.SubjectAlternativeNames != null)
                {
                    dnsHosts.AddRange(requestConfig.SubjectAlternativeNames);
                }

                dnsHosts = dnsHosts.Distinct().ToList();

                // add/update required bindings for each dns hostname
                foreach (var hostname in dnsHosts)
                {
                    //match dns host to IIS site
                    if (String.IsNullOrWhiteSpace(hostname)) throw new ArgumentException("InstallCertForRequest: Invalid (empty) DNS hostname supplied");

                    if (site != null)
                    {
                        //TODO: if the binding fails we should report it, requires reporting a list of binding results

                        //create/update binding and associate new cert
                        //if any binding elements configured, use those, otherwise auto bind using defaults and SNI
                        InstallCertificateforBinding(site, storedCert, hostname,
                            sslPort: !String.IsNullOrWhiteSpace(requestConfig.BindingPort) ? int.Parse(requestConfig.BindingPort) : 443,
                            useSNI: (requestConfig.BindingUseSNI != null ? (bool)requestConfig.BindingUseSNI : true),
                            ipAddress: !String.IsNullOrWhiteSpace(requestConfig.BindingIPAddress) ? requestConfig.BindingIPAddress : null
                            );
                    }
                }

                if (cleanupCertStore)
                {
                    //remove old certs for this primary domain
                    CertificateManager.CleanupCertificateDuplicates(storedCert, requestConfig.PrimaryDomain);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// removes the managedSite's https binding for the dns host name specified 
        /// </summary>
        /// <param name="managedSite"></param>
        /// <param name="host"></param>
        public void RemoveHttpsBinding(ManagedSite managedSite, string host)
        {
            if (string.IsNullOrEmpty(managedSite.GroupId)) throw new Exception("RemoveHttpsBinding: Managed site has no GroupID for IIS Site");

            using (var iisManager = GetDefaultServerManager())
            {
                var site = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == managedSite.GroupId);
                if (site != null)
                {
                    string internationalHost = host == "" ? "" : _idnMapping.GetUnicode(host);
                    var binding = site.Bindings.Where(b =>
                        b.Host == internationalHost &&
                        b.Protocol == "https"
                    ).FirstOrDefault();

                    if (binding != null)
                    {
                        site.Bindings.Remove(binding);
                        iisManager.CommitChanges();
                    }
                }
            }
        }

        /// <summary>
        /// creates or updates the https binding for the dns host name specified, assigning the given
        /// certificate selected from the certificate store
        /// </summary>
        /// <param name="managedSite"></param>
        /// <param name="certificate"></param>
        /// <param name="host"></param>
        /// <param name="sslPort"></param>
        /// <param name="useSNI"></param>
        /// <param name="ipAddress"></param>
        public bool InstallCertificateforBinding(ManagedSite managedSite, X509Certificate2 certificate, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null)
        {
            var site = FindManagedSite(managedSite);
            if (site == null) return false;

            return InstallCertificateforBinding(site, certificate, host, sslPort, useSNI, ipAddress);
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
        public bool InstallCertificateforBinding(Site site, X509Certificate2 certificate, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null)
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);

            using (var iisManager = GetDefaultServerManager())
            {
                if (GetIisVersion().Major < 8)
                {
                    // IIS ver < 8 doesn't support SNI - default to host/SNI-less bindings
                    useSNI = false;
                    host = "";
                }

                var siteToUpdate = iisManager.Sites.FirstOrDefault(s => s.Id == site.Id);

                if (siteToUpdate != null)
                {
                    string internationalHost = host == "" ? "" : _idnMapping.GetUnicode(host);
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
                        string bindingSpec = (ipAddress != null ? ipAddress : "*") +
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
                else
                {
                    //could not match site to bind to
                    return false;
                }

                iisManager.CommitChanges();
                store.Close();

                return true;
            }
        }

        #endregion Certificates
    }
}