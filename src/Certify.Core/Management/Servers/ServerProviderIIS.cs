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

            try
            {
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
            }
            catch (Exception)
            {
                configChecks.Add(new ActionStep
                {
                    HasWarning = true,
                    Title = "IIS Administration API not available",
                    Description = "Querying the state of IIS failed. This is usually because IIS is not installed or is not fully configured."
                });
            }
            return configChecks;
        }

        private async Task<ServerManager> GetDefaultServerManager()
        {
            ServerManager srv = null;
            try
            {
                srv = new ServerManager();

                // checking sites collection will throw a com exception if IIS is not installed
                if (srv.Sites.Count < 0) throw new Exception("IIS is not installed");
            }
            catch (Exception)
            {
                srv = null;
                try
                {
                    srv = new ServerManager(@"C:\Windows\System32\inetsrv\config\applicationHost.config");
                    if (srv.Sites.Count < 0) throw new Exception("IIS is not installed");
                }
                catch (Exception)
                {
                    // IIS is probably not installed
                    srv = null;
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
        /// <param name="includeOnlyStartedSites">  </param>
        /// <returns>  </returns>
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

        public async Task<bool> IsSNISupported()
        {
            var version = await GetServerVersion();
            if (version.Major < 8)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Add or Update domain binding as required (http or https)
        /// </summary>
        /// <param name="bindingSpec">  </param>
        /// <param name="addNew">  </param>
        /// <returns>  </returns>

        public async Task<ActionStep> AddOrUpdateSiteBinding(BindingInfo bindingSpec, bool addNew)
        {
            if (string.IsNullOrEmpty(bindingSpec.SiteId)) throw new Exception("IIS.AddOrUpdateSiteBinding: SiteId not specified");

            var result = new ActionStep { };
            int remainingAttempts = 3;
            bool isCompleted = false;
            while (!isCompleted && remainingAttempts > 0)
            {
                try
                {
                    using (var iisManager = await GetDefaultServerManager())
                    {
                        var isSNISupported = await IsSNISupported();

                        lock (_iisAPILock)
                        {
                            var site = iisManager.Sites.FirstOrDefault(s => s.Id == long.Parse(bindingSpec.SiteId));

                            if (site != null)
                            {
                                var binding = site.Bindings.CreateElement();
                                var bindingSpecString = "";
                                var bindingError = false;
                                List<ConfigurationAttribute> existingBindingAttributes = null;

                                if (addNew)
                                {
                                    bindingSpecString = $"{(!string.IsNullOrEmpty(bindingSpec.IP) ? bindingSpec.IP : "*")}:{bindingSpec.Port}:{bindingSpec.Host}";
                                }
                                else
                                {
                                    var existingBinding = site.Bindings
                                            .FirstOrDefault(b =>
                                                    b.Host?.ToLower() == bindingSpec.Host?.ToLower()
                                                    && b.Protocol.ToLower() == bindingSpec.Protocol.ToLower()
                                                );

                                    if (existingBinding != null)
                                    {
                                        // copy previous binding information
                                        bindingSpecString = existingBinding.BindingInformation;
                                        existingBindingAttributes = existingBinding.Attributes.ToList();

                                        // remove old binding (including all shared SSL bindings)

                                        // if removing a binding with an IP:Port association and
                                        // there are other non-sni bindings using the same cert this
                                        // will also invalidate those bindings
                                        // TODO: pre-validate shared bindings
                                        site.Bindings.Remove(existingBinding, removeConfigOnly: true);

                                        result = new ActionStep { HasWarning = false, Description = $"Existing binding removed : {bindingSpec}" };
                                    }
                                    else
                                    {
                                        result = new ActionStep { HasError = true, Description = $"Existing binding not found : {bindingSpec}" };
                                        bindingError = true;
                                    }
                                }

                                // If there are no errors, proceed with adding new binding
                                if (!bindingError)
                                {
                                    // Set binding values
                                    if (bindingSpec.IsHTTPS)
                                    {
                                        binding.Protocol = "https";
                                        binding.BindingInformation = bindingSpecString;
                                        binding.CertificateStoreName = bindingSpec.CertificateStore;
                                        binding.CertificateHash = bindingSpec.CertificateHashBytes;
                                    }
                                    else
                                    {
                                        binding.Protocol = bindingSpec.Protocol ?? "http";
                                        binding.BindingInformation = bindingSpecString;
                                    }

                                    if (existingBindingAttributes != null)
                                    {
                                        //clone existing binding attributes

                                        var skippedAttributes = new[] {
                                    "protocol",
                                    "bindingInformation",
                                    "certificateStoreName",
                                    "certificateHash"
                                };

                                        foreach (var a in existingBindingAttributes)
                                        {
                                            try
                                            {
                                                if (!skippedAttributes.Contains(a.Name) && a.Value != null)
                                                {
                                                    binding.SetAttributeValue(a.Name, a.Value);
                                                }
                                            }
                                            catch
                                            {
                                                return new ActionStep { HasError = true, Description = $"Failed to set binding attribute on updated IIS Binding: {a.Name}" };
                                            }
                                        }
                                    }
                                    if (bindingSpec.IsHTTPS)
                                    {
                                        if (isSNISupported)
                                        {
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

                                                    return new ActionStep { HasError = true, Description = $"Failed to set SNI flag on IIS Binding: {bindingSpec}" };
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (bindingSpec.IsSNIEnabled && !isSNISupported)
                                            {
                                                result = new ActionStep { HasWarning = true, Description = $"SNI requested on on IIS Binding but is not supported by this version of IIS. Duplicate certificate bindings can occur unless each certificate is bound to a distinct IP address : {bindingSpec}" };
                                            }
                                        }
                                    }

                                    // Add the binding to the site
                                    site.Bindings.Add(binding);

                                    result = new ActionStep { HasWarning = false, Description = $"New binding added : {bindingSpec}" };
                                }
                            }

                            iisManager.CommitChanges();
                        }
                    }
                    isCompleted = true;
                }
                catch (Exception exp)
                {
                    System.Diagnostics.Debug.WriteLine(exp.ToString());
                    // failed to commit binding, possible exception due to config still being written
                    // from another operation
                    isCompleted = false;
                    remainingAttempts--;

                    if (remainingAttempts == 0 && !isCompleted)
                    {
                        // failed to create this binding, possible due to settings serialisation conflict
                        throw exp;
                    }
                    else
                    {
                        var delayMS = 20000 / remainingAttempts; // gradually wait longer between attempts
                        await Task.Delay(delayMS); // pause to give IIS config time to write to disk before attempting more writes
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Simple binding creation for integrations test only
        /// </summary>
        /// <param name="siteId">  </param>
        /// <param name="domains">  </param>
        /// <param name="port">  </param>
        /// <returns>  </returns>
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

        private string HashBytesToThumprint(byte[] bytes)
        {
            // inspired by:
            // dotnet core System.Security.Cryptography.X509Certificates/src/Internal/Cryptography/Helpers.cs
            // CertificateHash is stored as 2 nibbles (4-bits) per byte, so convert to bytes > hex
            string output = "";
            foreach (byte b in bytes)
            {
                output += ((byte)(b >> 4)).ToString("X");
                output += ((byte)(b & 0xF)).ToString("X");
            }
            return output;
        }

        private static char NibbleToHex(byte b)
        {           
            return (char)(b >= 0 && b <= 9 ?
                '0' + b :
                'A' + (b - 10));
        }


        private BindingInfo GetSiteBinding(Site site, Binding binding)
        {
            var siteInfo = Map(site);

            return new BindingInfo()
            {
                SiteId = siteInfo.Id,
                SiteName = siteInfo.Name,
                PhysicalPath = siteInfo.Path,
                Host = binding.Host.ToLower(),
                IP = binding.EndPoint?.Address?.ToString(),
                Port = binding.EndPoint?.Port ?? 0,
                IsHTTPS = binding.Protocol.ToLower() == "https",
                Protocol = binding.Protocol.ToLower(),
                HasCertificate = (binding.CertificateHash != null),
                CertificateHash = binding.CertificateHash != null ? HashBytesToThumprint(binding.CertificateHash): null,
                CertificateHashBytes = binding.CertificateHash
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
        /// <param name="siteName">  </param>
        /// <param name="hostname">  </param>
        /// <param name="phyPath">  </param>
        /// <param name="appPoolName">  </param>
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
        /// <param name="siteName">  </param>
        /// <returns>  </returns>
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
                if (iisManager != null)
                {
                    Site siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
                    return Map(siteDetails);
                }
                else
                {
                    return null;
                }
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
                site = Map(await GetIISSiteByDomain(managedCertificate.RequestConfig.PrimaryDomain));
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
        /// <param name="siteId">  </param>
        /// <param name="host">  </param>
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
                            b.Protocol.ToLower() == "https"
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

        public void Dispose()
        {
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

        public async Task<ActionStep> AddBinding(BindingInfo targetBinding)
        {
            return await _iisManager.AddOrUpdateSiteBinding(targetBinding, true);
        }

        public async Task<ActionStep> UpdateBinding(BindingInfo targetBinding)
        {
            return await _iisManager.AddOrUpdateSiteBinding(targetBinding, false);
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
