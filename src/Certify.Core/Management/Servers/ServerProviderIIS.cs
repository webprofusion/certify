using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;
using Microsoft.Web.Administration;
using Microsoft.Win32;

namespace Certify.Management.Servers
{
    /// <summary>
    /// Model to work with IIS site details. 
    /// </summary>
    public class ServerProviderIIS : ICertifiedServer
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();

        private bool _isIISAvailable { get; set; }

        /// <summary>
        /// We use a lock on any method that uses CommitChanges, to avoid writing changes at the same time
        /// </summary>
        private static readonly object _iisAPILock = new object();

        public ServerProviderIIS()
        {
        }

        public IBindingDeploymentTarget GetDeploymentTarget()
        {
            return new IISBindingDeploymentTarget();
        }

        public Task<bool> IsAvailable()
        {
            if (!_isIISAvailable)
            {
                try
                {
                    using (var srv = GetDefaultServerManager())
                    {
                        // _isIISAvaillable will be updated by query against server manager
                        if (srv != null) return Task.FromResult(_isIISAvailable);
                    }
                }
                catch
                {
                    // IIS not available
                    return Task.FromResult(false);
                }
            }
            return Task.FromResult(_isIISAvailable);
        }

        public Task<Version> GetServerVersion()
        {
            //http://stackoverflow.com/questions/446390/how-to-detect-iis-version-using-c
            Version result = null;

            using (RegistryKey componentsKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\InetStp", false))
            {
                if (componentsKey != null)
                {
                    _isIISAvailable = true;

                    int majorVersion = (int)componentsKey.GetValue("MajorVersion", -1);
                    int minorVersion = (int)componentsKey.GetValue("MinorVersion", -1);

                    if (majorVersion != -1 && minorVersion != -1)
                    {
                        result = new Version(majorVersion, minorVersion);
                    }
                }
            }

            if (result == null) result = new Version(0, 0);

            return Task.FromResult(result);
        }

        public async Task<List<ActionStep>> RunConfigurationDiagnostics(string siteId)
        {
            List<ActionStep> configChecks = new List<ActionStep>();
            using (var serverManager = await GetDefaultServerManager())
            {
                var config = serverManager.GetApplicationHostConfiguration();

                if ((bool?)serverManager.ApplicationPoolDefaults["enableConfigurationOverride"] == false)
                {
                    configChecks.Add(new ActionStep
                    {
                        HasWarning = true,
                        Title = "Application Pool: Configuration Override Disabled",
                        Description = "Configuration warning: enableConfigurationOverride in system.applicationHost / applicationPools is set to false. This may prevent auto configuration from clearing or rearranging static file handler mappings."
                    });
                }
                else
                {
                    configChecks.Add(new ActionStep
                    {
                        HasError = false,
                        Title = "Application Pool: Configuration Override Enabled",
                        Description = "Application Pool: Configuration Override Enabled"
                    });
                }
            }
            return configChecks;
        }

        private async Task<ServerManager> GetDefaultServerManager()
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
                var v = await GetServerVersion();
                if (v.Major < 7)
                {
                    _isIISAvailable = false;
                }
            }

            // may be null if could not create server manager
            return srv;
        }

        private IEnumerable<Site> GetSites(ServerManager iisManager, bool includeOnlyStartedSites, string siteId = null)
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
        public async Task<List<BindingInfo>> GetPrimarySites(bool includeOnlyStartedSites)
        {
            var result = new List<BindingInfo>();

            try
            {
                using (var iisManager = await GetDefaultServerManager())
                {
                    if (iisManager != null)
                    {
                        var sites = GetSites(iisManager, includeOnlyStartedSites);

                        foreach (var site in sites)
                        {
                            if (site != null)
                            {
                                var b = new BindingInfo()
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

        public async Task AddOrUpdateSiteBinding(BindingInfo bindingSpec, bool addNew)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
                {
                    var site = iisManager.Sites.FirstOrDefault(s => s.Id == long.Parse(bindingSpec.SiteId));

                    if (site != null)
                    {
                        if (addNew)
                        {
                            var binding = site.Bindings.CreateElement();

                            var bindingSpecString = $"{(!string.IsNullOrEmpty(bindingSpec.IP) ? bindingSpec.IP : "*")}:{bindingSpec.Port}:{bindingSpec.Host}";

                            // Set binding values
                            binding.Protocol = "https";
                            binding.BindingInformation = bindingSpecString;
                            binding.CertificateStoreName = bindingSpec.CertificateStore;
                            binding.CertificateHash = bindingSpec.CertificateHashBytes;

                            if (!string.IsNullOrEmpty(bindingSpec.Host) && bindingSpec.IsSNIEnabled)
                            {
                                try
                                {
                                    binding["sslFlags"] = 1; // enable SNI
                                }
                                catch (Exception)
                                {
                                    //failed to set requested SNI flag
                                    //TODO: log
                                    // action.Description += $" Failed to set SNI attribute";
                                    return;
                                }
                            }

                            // Add the binding to the site
                            site.Bindings.Add(binding);
                        }
                        else
                        {
                            var existingBinding = site.Bindings.FirstOrDefault(b =>
                                            b.Host == bindingSpec.Host
                                            && b.Protocol == bindingSpec.Protocol
                                        );

                            if (existingBinding != null)
                            {
                                existingBinding.CertificateHash = bindingSpec.CertificateHashBytes;
                                existingBinding.CertificateStoreName = bindingSpec.CertificateStore;
                            }
                        }
                    }

                    iisManager.CommitChanges();
                }
            }
            await Task.Delay(250); // pause to give IIS config time to write to disk before attempting more writes
        }

        public async Task AddSiteBindings(string siteId, List<string> domains, int port = 80)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
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

        public async Task<List<BindingInfo>> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
        {
            var result = new List<BindingInfo>();

            using (var iisManager = await GetDefaultServerManager())
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
                                result.Add(bindingDetails);
                            }
                        }
                    }
                }
            }

            return result.OrderBy(r => r.SiteName).ToList();
        }

        private BindingInfo GetSiteBinding(Site site, Binding binding)
        {
            var siteInfo = Map(site);

            return new BindingInfo()
            {
                SiteId = siteInfo.Id,
                SiteName = siteInfo.Name,
                PhysicalPath = siteInfo.Path,
                Host = binding.Host,
                IP = binding.EndPoint?.Address?.ToString(),
                Port = binding.EndPoint?.Port ?? 0,
                IsHTTPS = binding.Protocol.ToLower() == "https",
                Protocol = binding.Protocol,
                HasCertificate = (binding.CertificateHash != null)
            };
        }

        private async Task<Site> GetIISSiteByDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain)) return null;

            domain = _idnMapping.GetUnicode(domain);
            using (var iisManager = await GetDefaultServerManager())
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

        public async Task<BindingInfo> GetSiteBindingByDomain(string domain)
        {
            domain = _idnMapping.GetUnicode(domain);

            var site = await GetIISSiteByDomain(domain);
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
        public async Task<Site> CreateSite(string siteName, string hostname, string phyPath, string appPoolName, string protocol = "http", string ipAddress = "*", int? port = 80)
        {
            Site result = null;

            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
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
        public async Task<bool> SiteExists(string siteName)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                return (iisManager.Sites[siteName] != null);
            }
        }

        public async Task DeleteSite(string siteName)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
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

        private SiteInfo Map(Site site)
        {
            if (site != null)
            {
                var s = new SiteInfo
                {
                    Id = site.Id.ToString(),
                    Name = site.Name,

                    ServerType = StandardServerTypes.IIS
                };

                try
                {
                    s.Path = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                }
                catch { }

                return s;
            }
            else
            {
                return null;
            }
        }

        public async Task<SiteInfo> GetSiteById(string id)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                Site siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
                return Map(siteDetails);
            }
        }

        public async Task<Site> GetIISSiteById(string id)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                Site siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
                return siteDetails;
            }
        }

        public async Task<bool> IsSiteRunning(string id)
        {
            using (var iisManager = await GetDefaultServerManager())
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
        /// Finds the IIS <see cref="Site" /> corresponding to a <see cref="ManagedCertificate" />. 
        /// </summary>
        /// <param name="managedCertificate"> Configured site. </param>
        /// <returns> The matching IIS Site if found, otherwise null. </returns>
        private async Task<SiteInfo> FindManagedCertificate(ManagedCertificate managedCertificate)
        {
            if (managedCertificate == null)
                throw new ArgumentNullException(nameof(managedCertificate));

            var site = await GetSiteById(managedCertificate.GroupId);

            if (site != null)
            {
                //TODO: ? check site has bindings for given domains, otherwise set back to null
            }

            if (site == null)
            {
                site = Map(
                    await GetIISSiteByDomain(managedCertificate.RequestConfig.PrimaryDomain)
                    );
            }

            return site;
        }

        private string ToUnicodeString(string input)
        {
            //if string already has (non-ascii range) unicode characters return original
            if (input.Any(c => c > 255)) return input;

            return _idnMapping.GetUnicode(input);
        }

        /// <summary>
        /// removes the sites https binding for the dns host name specified 
        /// </summary>
        /// <param name="siteId"></param>
        /// <param name="host"></param>
        public async Task RemoveHttpsBinding(string siteId, string host)
        {
            if (string.IsNullOrEmpty(siteId)) throw new Exception("RemoveHttpsBinding: No siteId for IIS Site");

            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
                {
                    var site = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == siteId);

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
        /// Creates or updates the https bindings associated with the dns names in the current
        /// request config, using the requested port/ips or autobinding
        /// </summary>
        /// <param name="requestConfig"></param>
        /// <param name="pfxPath"></param>
        /// <param name="cleanupCertStore"></param>
        /// <returns></returns>
       /* public async Task<List<ActionStep>> InstallCertForRequest(ManagedCertificate managedCertificate, string pfxPath, bool cleanupCertStore, bool isPreviewOnly)
        {
            List<ActionStep> actions = new List<ActionStep>();

            var requestConfig = managedCertificate.RequestConfig;

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

                var deployments = await DeployToBindings(managedCertificate, requestConfig, certStoreName, certHash, dnsHosts, isPreviewOnly);

                actions.AddRange(deployments);

                // if required, cleanup old certs we are replacing. Only applied if we have deployed
                // the certificate, otherwise we keep the old one

                // FIXME: need strategy to analyse if there are any users of cert we haven't
                //        accounted for (manually added etc) otherwise we are disposing of a cert
                // which could still be in use
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
            }

            // deployment tasks completed
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

        private async Task<List<ActionStep>> DeployToBindings(ManagedCertificate managedCertificate, CertRequestConfig requestConfig, string certStoreName, byte[] certHash, List<string> dnsHosts, bool isPreviewOnly = false)
        {
            List<ActionStep> actions = new List<ActionStep>();
            List<Site> targetSites = new List<Site>();

            // if single site, add that
            if (requestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
            {
                var site = await GetIISSiteById(managedCertificate.ServerSiteId);
                if (site != null) targetSites.Add(site);
            }

            // or add all sites (if required)
            if (requestConfig.DeploymentSiteOption == DeploymentOption.AllSites)
            {
                using (ServerManager serverManager = await GetDefaultServerManager())
                {
                    targetSites.AddRange(GetSites(serverManager, true));
                }
            }

            // for each sites we want to target, identify bindings to add/update as required
            foreach (var site in targetSites)
            {
                try
                {
                    var existingBindings = site.Bindings.ToList();

                    var existingHttps = existingBindings.Where(e => e.Protocol == "https").ToList();

                    //remove https bindings which already have an https equivalent (specific hostname or blank)
                    existingBindings.RemoveAll(b => existingHttps.Any(e => e.Host == b.Host) && b.Protocol == "http");

                    existingBindings = existingBindings.OrderBy(b => b.Protocol).ThenBy(b => b.Host).ToList();

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
                            // if replacing previous, check if current binding cert hash matches
                            // previous cert hash
                            if (b.CertificateHash != null && managedCertificate.CertificatePreviousThumbprintHash != null)
                            {
                                if (String.Equals(ByteToHex(b.CertificateHash), managedCertificate.CertificatePreviousThumbprintHash))
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
                            // update existing bindings only, so only update if this is already an
                            // https binding
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
                            var stepActions = await InstallCertificateforBinding(
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

                            actions.AddRange(stepActions);
                        }
                    }
                    //
                }
                catch (Exception exp)
                {
                    actions.Add(new ActionStep { Title = site.Name, HasError = true, Description = exp.ToString() });
                }
            }

            return actions;
        }

        /// <summary>
        /// creates or updates the https binding for the dns host name specified, assigning the given
        /// certificate selected from the certificate store
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <param name="certificate"></param>
        /// <param name="host"></param>
        /// <param name="sslPort"></param>
        /// <param name="useSNI"></param>
        /// <param name="ipAddress"></param>
        public async Task<List<ActionStep>> InstallCertificateforBinding(string certStoreName, byte[] certificateHash, ManagedCertificate managedCertificate, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false)
        {
            var site = await GetIISSiteById(managedCertificate.ServerSiteId);
            if (site == null)
            {
                return new List<ActionStep>{
                    new ActionStep {
                        Title = "Install Certificate For Binding",
                        Description = "Managed site not found",
                        HasError = true
                    }
                };
            }

            return await InstallCertificateforBinding(certStoreName, certificateHash, site, host, sslPort, useSNI, ipAddress, alwaysRecreateBindings, isPreviewOnly);
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
        public async Task<List<ActionStep>> InstallCertificateforBinding(string certStoreName, byte[] certificateHash, Site site, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false)
        {
            List<ActionStep> steps = new List<ActionStep>();

            using (var iisManager = await GetDefaultServerManager())
            {
                var v = await GetServerVersion();

                lock (_iisAPILock)
                {
                    if (v.Major < 8)
                    {
                        // IIS ver < 8 doesn't support SNI - default to host/SNI-less bindings
                        useSNI = false;
                        host = "";
                    }

                    var siteToUpdate = iisManager.Sites.FirstOrDefault(s => s.Id == site.Id);

                    if (siteToUpdate != null)
                    {
                        string internationalHost = host == "" ? "" : ToUnicodeString(host);
                        string bindingSpec = $"{(!String.IsNullOrEmpty(ipAddress) ? ipAddress : "*")}:{sslPort}:{internationalHost}";

                        var existingHttpsBindings = from b in siteToUpdate.Bindings where b.Host == internationalHost && b.Protocol == "https" select b;
                        if (!existingHttpsBindings.Any())
                        {
                            //there are no existing https bindings to update for this domain

                            //add new https binding at default port "<ip>:port:hostDnsName";

                            var action = new ActionStep
                            {
                                Title = "Install Certificate For Binding",
                                Category = "Deployment.AddBinding",
                                Description = $"* Add new https binding: [{siteToUpdate.Name}] **{bindingSpec}**",
                                Key = $"[{site.Id}]:{bindingSpec}:{useSNI}"
                            };

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

                                        steps.Add(action);
                                        return steps;
                                    }
                                }

                                // Add the binding to the site
                                siteToUpdate.Bindings.Add(binding);
                            }

                            steps.Add(action);
                        }
                        else
                        {
                            // update one or more existing https bindings with new cert
                            foreach (var existingBinding in existingHttpsBindings)
                            {
                                if (!isPreviewOnly)
                                {
                                    // Update existing https Binding
                                    existingBinding.CertificateHash = certificateHash;
                                    existingBinding.CertificateStoreName = certStoreName;
                                }

                                steps.Add(new ActionStep
                                {
                                    Title = "Install Certificate For Binding",
                                    Category = "Deployment.UpdateBinding",
                                    Description = $"* Update existing binding: [{siteToUpdate.Name}] **{existingBinding.BindingInformation}** \r\n"
                                });
                            }
                        }
                    }
                    else
                    {
                        //could not match site to bind to

                        steps.Add(new ActionStep
                        {
                            Title = "No Deployment Target Match",
                            Category = "Deployment.UmatchedBinding",
                            Description = $"* Site not found, could not update bindings."
                        });
                        return steps;
                    }

                    if (!isPreviewOnly)
                    {
                        iisManager.CommitChanges();
                    }

                    return steps;
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
        */

        public void Dispose()
        {
        }

        public Task<bool> CreateManagementContext()
        {
            throw new NotImplementedException();
        }

        public Task<bool> CommitChanges()
        {
            throw new NotImplementedException();
        }
    }

    public class IISBindingTargetItem : IBindingDeploymentTargetItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class IISBindingDeploymentTarget : IBindingDeploymentTarget

    {
        private ServerProviderIIS _iisManager;

        public IISBindingDeploymentTarget()
        {
            _iisManager = new ServerProviderIIS();
        }

        public async Task<bool> AddBinding(BindingInfo targetBinding)
        {
            await _iisManager.AddOrUpdateSiteBinding(targetBinding, true);
            return true;
        }

        public async Task<bool> UpdateBinding(BindingInfo targetBinding)
        {
            await _iisManager.AddOrUpdateSiteBinding(targetBinding, false);
            return true;
        }

        public async Task<List<IBindingDeploymentTargetItem>> GetAllTargetItems()
        {
            var sites = await _iisManager.GetPrimarySites(true);

            return sites.Select(s =>
                (IBindingDeploymentTargetItem)new IISBindingTargetItem
                {
                    Id = s.SiteId,
                    Name = s.SiteName
                }).ToList();
        }

        public async Task<List<BindingInfo>> GetBindings(string targetItemId)
        {
            return await _iisManager.GetSiteBindingList(true, targetItemId);
        }

        public async Task<IBindingDeploymentTargetItem> GetTargetItem(string id)
        {
            var site = await _iisManager.GetSiteById(id);
            if (site != null)
            {
                return new IISBindingTargetItem { Id = site.Id, Name = site.Name };
            }
            else
            {
                return null;
            }
        }

        public string GetTargetName()
        {
            return "IIS";
        }
    }
}
