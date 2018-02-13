using Certify.Models;
using Certify.Models.Providers;
using Microsoft.Web.Administration;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Certify.Management.Servers
{
    /// <summary>
    /// Model to work with IIS site details. 
    /// </summary>
    public class ServerProviderIIS : ICertifiedServer
    {
        #region IIS

        private readonly IdnMapping _idnMapping = new IdnMapping();

        private bool _isIISAvailable { get; set; }

        private bool _enableCertDoubleImportBehaviour { get; set; } = true;

        /// <summary>
        /// We use a lock on any method that uses CommitChanges, to avoid writing changes at the same time
        /// </summary>
        private static readonly object _iisAPILock = new object();

        public bool IsAvailable()
        {
            if (!_isIISAvailable)
            {
                try
                {
                    using (var srv = GetDefaultServerManager())
                    {
                        // _isIISAvaillable will be updated by query against server manager
                        if (srv != null) return _isIISAvailable;
                    }
                }
                catch
                {
                    // IIS not available
                    return false;
                }
            }

            return _isIISAvailable;
        }

        public async Task<bool> IsAvailableAsync()
        {
            // FIXME: blocking async
            var isAvailable = false;
            try
            {
                using (var srv = GetDefaultServerManager())
                {
                    if (srv != null) isAvailable = true;
                }
            }
            catch
            {
                // iis not available
            }
            return await Task.FromResult(isAvailable);
        }

        public async Task<Version> GetServerVersionAsync()
        {
            // FIXME: blocking async

            return await Task.FromResult(GetServerVersion());
        }

        public Version GetServerVersion()
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
                var v = GetServerVersion();
                if (v.Major < 7)
                {
                    _isIISAvailable = false;
                }
            }
            // may be null if could not create server manager
            return srv;
        }

        public IEnumerable<Site> GetSites(ServerManager iisManager, bool includeOnlyStartedSites, string siteId = null)
        {
            try
            {
                var siteList = iisManager.Sites.AsQueryable();
                if (siteId != null) siteList = siteList.Where(s => s.Id.ToString() == siteId);

                if (includeOnlyStartedSites)
                {
                    //s.State may throw a com exception for sites in an invalid state.
                    Func<Site, bool> isSiteStarted = (s) =>
                         {
                             try
                             {
                                 return s.State == ObjectState.Started;
                             }
                             catch (Exception)
                             {
                                 // if we get an exception testing state, assume site is running
                                 return true;
                             }
                         };

                    return siteList.Where(s => isSiteStarted(s));
                }
                else
                {
                    return siteList;
                }
            }
            catch
            {
                //failed to enumerate sites (IIS not available?)
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

            try
            {
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
            }
            catch
            {
                //IIS not available
            }

            return result.OrderBy(s => s.SiteName).ToList();
        }

        public void AddSiteBindings(string siteId, List<string> domains, int port = 80)
        {
            lock (_iisAPILock)
            {
                using (var iisManager = GetDefaultServerManager())
                {
                    var site = iisManager.Sites.FirstOrDefault(s => s.Id == long.Parse(siteId));

                    var currentBindings = site.Bindings.ToList();

                    foreach (var d in domains)
                    {
                        if (!currentBindings.Any(c => c.Host == d))
                        {
                            site.Bindings.Add("*:" + port + ":" + d, "http");
                        }
                    }
                    iisManager.CommitChanges();
                }
            }
        }

        public List<SiteBindingItem> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
        {
            var result = new List<SiteBindingItem>();

            using (var iisManager = GetDefaultServerManager())
            {
                if (iisManager != null)
                {
                    var sites = GetSites(iisManager, ignoreStoppedSites, siteId);

                    foreach (var site in sites)
                    {
                        foreach (var binding in site.Bindings.OrderByDescending(b => b?.EndPoint?.Port))
                        {
                            var bindingDetails = GetSiteBinding(site, binding);

                            //ignore bindings which are not http or https
                            if (bindingDetails.Protocol?.ToLower().StartsWith("http") == true)
                            {
                                if (!String.IsNullOrEmpty(bindingDetails.Host) && bindingDetails.Host.Contains("."))
                                {
                                    result.Add(bindingDetails);
                                }
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
        public Site CreateSite(string siteName, string hostname, string phyPath, string appPoolName, string protocol = "http", string ipAddress = "*", int? port = 80)
        {
            Site result = null;

            lock (_iisAPILock)
            {
                using (var iisManager = GetDefaultServerManager())
                {
                    // usual binding format is ip:port:dnshostname but can also be *:port,
                    // *:port:hostname or just hostname
                    string bindingInformation = (ipAddress != null ? (ipAddress + ":") : "")
                        + (port != null ? (port + ":") : "")
                        + hostname;

                    result = iisManager.Sites.Add(siteName, protocol, bindingInformation, phyPath);
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

            return result;
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
            lock (_iisAPILock)
            {
                using (var iisManager = GetDefaultServerManager())
                {
                    Site siteToRemove = iisManager.Sites[siteName];
                    if (siteToRemove != null)
                    {
                        iisManager.Sites.Remove(siteToRemove);
                    }
                    iisManager.CommitChanges();
                }
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

        private string ToUnicodeString(string input)
        {
            //if string already has (non-ascii range) unicode characters return original
            if (input.Any(c => c > 255)) return input;

            return _idnMapping.GetUnicode(input);
        }

        /// <summary>
        /// Creates or updates the htttps bindings associated with the dns names in the current
        /// request config, using the requested port/ips or autobinding
        /// </summary>
        /// <param name="requestConfig"></param>
        /// <param name="pfxPath"></param>
        /// <param name="cleanupCertStore"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> InstallCertForRequest(ManagedSite managedSite, string pfxPath, bool cleanupCertStore, bool isPreviewOnly)
        {
            List<ActionStep> actions = new List<ActionStep>();

            var requestConfig = managedSite.RequestConfig;

            if (!isPreviewOnly)
            {
                if (new System.IO.FileInfo(pfxPath).Length == 0)
                {
                    throw new ArgumentException("InstallCertForRequest: Invalid PFX File");
                }
            }

            //store cert against primary domain
            string certStoreName = CertificateManager.GetDefaultStore().Name;
            X509Certificate2 storedCert = null;
            byte[] certHash = null;

            // unless user has opted not to store cert, store it now
            if (requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment)
            {
                if (!isPreviewOnly)
                {
                    storedCert = await CertificateManager.StoreCertificate(requestConfig.PrimaryDomain, pfxPath, isRetry: false, enableRetryBehaviour: _enableCertDoubleImportBehaviour);
                    if (storedCert != null) certHash = storedCert.GetCertHash();
                }
                else
                {
                    //fake cert for preview only
                    storedCert = new X509Certificate2();
                    certHash = new byte[] { 0x00, 0x01, 0x02 };
                }
            }

            if (storedCert != null)
            {
                //get list of domains we need to create/update https bindings for
                List<string> dnsHosts = new List<string> {
                    ToUnicodeString(requestConfig.PrimaryDomain)
                };

                if (requestConfig.SubjectAlternativeNames != null)
                {
                    foreach (var san in requestConfig.SubjectAlternativeNames)
                    {
                        dnsHosts.Add(ToUnicodeString(san));
                    }
                }

                dnsHosts = dnsHosts.Distinct().ToList();

                // depending on our deployment mode we decide which sites/bindings to update:
                actions.AddRange(
                    DeployToBindings(managedSite, requestConfig, certStoreName, certHash, dnsHosts, isPreviewOnly)
                );

                // if required, cleanup old certs we are replacing. Only applied if we have deployed
                // the certificate, otherwise we keep the old one
                if (!isPreviewOnly)
                {
                    if (cleanupCertStore
                        && requestConfig.DeploymentSiteOption != DeploymentOption.DeploymentStoreOnly
                         && requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment
                        )
                    {
                        //remove old certs for this primary domain
                        CertificateManager.CleanupCertificateDuplicates(storedCert, requestConfig.PrimaryDomain);
                    }
                }
                // deployment tasks completed
            }
            return actions;
        }

        private string ByteToHex(byte[] ba)
        {
            var sb = new System.Text.StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                sb.AppendFormat("{0:x2}", b);
            }
            return sb.ToString();
        }

        private List<ActionStep> DeployToBindings(ManagedSite managedSite, CertRequestConfig requestConfig, string certStoreName, byte[] certHash, List<string> dnsHosts, bool isPreviewOnly = false)
        {
            List<ActionStep> actions = new List<ActionStep>();
            List<Site> targetSites = new List<Site>();

            // if single site, add that
            if (requestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
            {
                var site = FindManagedSite(managedSite);
                if (site != null) targetSites.Add(site);
            }

            // or add all sites (if required)
            if (requestConfig.DeploymentSiteOption == DeploymentOption.AllSites)
            {
                using (ServerManager serverManager = GetDefaultServerManager())
                {
                    targetSites.AddRange(GetSites(serverManager, true));
                }
            }

            // for each sites we want to target, identify bindings to add/update as required
            foreach (var site in targetSites)
            {
                var existingBindings = site.Bindings.ToList();

                // for each binding create or update an https binding
                foreach (var b in existingBindings)
                {
                    bool updateBinding = false;

                    //if binding is http and there is no https binding, create one
                    string hostname = b.Host;

                    // install the cert for this binding if the hostname matches, or we have a
                    // matching wildcard, or if there is no hostname specified in the binding

                    if (requestConfig.DeploymentBindingReplacePrevious)
                    {
                        // if replacing previous, check if current binding cert hash matches previous
                        // cert hash
                        if (b.CertificateHash != null && managedSite.CertificatePreviousThumbprintHash != null)
                        {
                            if (String.Equals(ByteToHex(b.CertificateHash), managedSite.CertificatePreviousThumbprintHash))
                            {
                                updateBinding = true;
                            }
                        }
                    }

                    if (updateBinding == false)
                    {
                        // TODO: add wildcard match
                        if (String.IsNullOrEmpty(hostname) && requestConfig.DeploymentBindingBlankHostname)
                        {
                            updateBinding = true;
                        }
                        else
                        {
                            if (requestConfig.DeploymentBindingMatchHostname)
                            {
                                updateBinding = IsDomainOrWildcardMatch(dnsHosts, hostname);
                            }
                        }
                    }

                    if (requestConfig.DeploymentBindingOption == DeploymentBindingOption.UpdateOnly)
                    {
                        // update existing bindings only, so only update if this is already an https binding
                        if (b.Protocol != "https") updateBinding = false;
                    }

                    if (b.Protocol != "http" && b.Protocol != "https")
                    {
                        // skip bindings for other service types
                        updateBinding = false;
                    }

                    if (updateBinding)
                    {
                        //create/update binding and associate new cert
                        //if any binding elements configured, use those, otherwise auto bind using defaults and SNI
                        var action = InstallCertificateforBinding(
                            certStoreName,
                            certHash,
                            site,
                            hostname,
                            sslPort: !String.IsNullOrWhiteSpace(requestConfig.BindingPort) ? int.Parse(requestConfig.BindingPort) : 443,
                            useSNI: (requestConfig.BindingUseSNI != null ? (bool)requestConfig.BindingUseSNI : true),
                            ipAddress: !String.IsNullOrWhiteSpace(requestConfig.BindingIPAddress) ? requestConfig.BindingIPAddress : null,
                            alwaysRecreateBindings: requestConfig.AlwaysRecreateBindings,
                            isPreviewOnly: isPreviewOnly
                        );

                        actions.Add(action);
                    }
                }
                //
            }

            return actions;
        }

        /// <summary>
        /// removes the managedSite's https binding for the dns host name specified 
        /// </summary>
        /// <param name="managedSite"></param>
        /// <param name="host"></param>
        public void RemoveHttpsBinding(ManagedSite managedSite, string host)
        {
            if (string.IsNullOrEmpty(managedSite.GroupId)) throw new Exception("RemoveHttpsBinding: Managed site has no GroupID for IIS Site");

            lock (_iisAPILock)
            {
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
        public ActionStep InstallCertificateforBinding(string certStoreName, byte[] certificateHash, ManagedSite managedSite, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false)
        {
            var site = FindManagedSite(managedSite);
            if (site == null) return new ActionStep { Title = "Install Certificate For Binding", Description = "Managed site not found", HasError = true };

            return InstallCertificateforBinding(certStoreName, certificateHash, site, host, sslPort, useSNI, ipAddress, alwaysRecreateBindings, isPreviewOnly);
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
        public ActionStep InstallCertificateforBinding(string certStoreName, byte[] certificateHash, Site site, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false)
        {
            ActionStep action = new ActionStep { Title = "Install Certificate For Binding", Description = "No action" };

            lock (_iisAPILock)
            {
                using (var iisManager = GetDefaultServerManager())
                {
                    if (GetServerVersion().Major < 8)
                    {
                        // IIS ver < 8 doesn't support SNI - default to host/SNI-less bindings
                        useSNI = false;
                        host = "";
                    }

                    var siteToUpdate = iisManager.Sites.FirstOrDefault(s => s.Id == site.Id);

                    if (siteToUpdate != null)
                    {
                        string internationalHost = host == "" ? "" : ToUnicodeString(host);
                        var existingBinding = (from b in siteToUpdate.Bindings where b.Host == internationalHost && b.Protocol == "https" select b).FirstOrDefault();

                        if (existingBinding != null)
                        {
                            if (!isPreviewOnly)
                            {
                                // Update existing https Binding
                                existingBinding.CertificateHash = certificateHash;
                                existingBinding.CertificateStoreName = certStoreName;
                            }
                            action.Description = $"Update existing binding: [{siteToUpdate.Name}] {existingBinding.BindingInformation}";
                        }
                        else
                        {
                            //add new https binding at default port "<ip>:port:hostDnsName";
                            string bindingSpec = $"{(!String.IsNullOrEmpty(ipAddress) ? ipAddress : "*")}:{sslPort}:{internationalHost}";

                            action.Description = $"Add new https binding: [{siteToUpdate.Name}] {bindingSpec}";

                            if (!isPreviewOnly)
                            {
                                var binding = siteToUpdate.Bindings.CreateElement();

                                // Set binding values
                                binding.Protocol = "https";
                                binding.BindingInformation = bindingSpec;
                                binding.CertificateStoreName = certStoreName;
                                binding.CertificateHash = certificateHash;

                                if (!String.IsNullOrEmpty(internationalHost) && useSNI)
                                {
                                    try
                                    {
                                        binding["sslFlags"] = 1; // enable SNI
                                    }
                                    catch (Exception)
                                    {
                                        //failed to set requested SNI flag

                                        action.Description += $" Failed to set SNI attribute";
                                        return action;
                                    }
                                }

                                // Add the binding to the site
                                siteToUpdate.Bindings.Add(binding);
                            }
                        }
                    }
                    else
                    {
                        //could not match site to bind to
                        action.Description = $"Site not found, could not update bindings.";
                        return action;
                    }

                    if (!isPreviewOnly)
                    {
                        iisManager.CommitChanges();
                    }

                    return action;
                }
            }
        }

        private static bool IsDomainOrWildcardMatch(List<string> dnsNames, string hostname)
        {
            var isMatch = false;
            if (!String.IsNullOrEmpty(hostname))
            {
                if (dnsNames.Contains(hostname))
                {
                    isMatch = true;
                }
                else
                {
                    //if any of our dnsHosts are a wildcard, check for a match
                    var wildcards = dnsNames.Where(d => d.StartsWith("*."));
                    foreach (var w in wildcards)
                    {
                        var domain = w.Replace("*.", "");
                        var splitDomain = domain.Split('.');
                        var potentialMatches = dnsNames.Where(h => h.EndsWith("." + domain) || h.Equals(domain));
                        foreach (var m in potentialMatches)
                        {
                            if (m == hostname)
                            {
                                isMatch = true;
                                break;
                            }
                            var splitHost = m.Split('.');

                            // if host is exactly one label more than the wildcard then it is a match
                            if (splitHost.Length == (splitDomain.Length + 1))
                            {
                                isMatch = true;
                                break;
                            }
                        }
                    }
                }
            }

            return isMatch;
        }

        #endregion Certificates
    }
}