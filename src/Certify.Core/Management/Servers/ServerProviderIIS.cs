using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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
    public class ServerProviderIIS : ITargetWebServer
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();

        private bool _isIISAvailable { get; set; }

        /// <summary>
        /// We use a lock on any method that uses CommitChanges, to avoid writing changes at the same time
        /// </summary>
        private static readonly Lock _iisAPILock = LockFactory.Create();

        private ILog _log;

        public ServerProviderIIS()
        {

        }
        public ServerProviderIIS(ILog log = null)
        {
            Init(log);
        }

        public void Init(ILog log, string configRoot = null)
        {
            _log = log;
        }

        private IISBindingDeploymentTarget _iisBindingDeploymentTarget = null;
        public IBindingDeploymentTarget GetDeploymentTarget()
        {
            if (_iisBindingDeploymentTarget == null)
            {
                _iisBindingDeploymentTarget = new IISBindingDeploymentTarget(this);
            }

            return _iisBindingDeploymentTarget;
        }

        public Task<bool> IsAvailable()
        {
            if (!_isIISAvailable)
            {
                try
                {
                    using (var srv = GetDefaultServerManager())
                    {
                        // _isIISAvailable is updated by completing the query against server manager
                        if (srv != null)
                        {
                            return Task.FromResult(_isIISAvailable);
                        }
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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using (var componentsKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\InetStp", false))
                    {
                        if (componentsKey != null)
                        {
                            _isIISAvailable = true;

                            var majorVersion = Convert.ToInt32(componentsKey.GetValue("MajorVersion", -1));
                            var minorVersion = Convert.ToInt32(componentsKey.GetValue("MinorVersion", -1));

                            if (majorVersion != -1 && minorVersion != -1)
                            {
                                result = new Version(majorVersion, minorVersion);
                            }
                        }
                    }
                }
                catch { }
            }

            if (result == null)
            {
                result = new Version(0, 0);
            }

            return Task.FromResult(result);
        }

        public async Task<List<ActionStep>> RunConfigurationDiagnostics(string siteId)
        {
            var configChecks = new List<ActionStep>();

            var defaultConfigError = new ActionStep
            {
                HasWarning = true,
                Title = "IIS Administration API not available",
                Description = "Querying the state of IIS failed. This is usually because IIS is not installed or is not fully configured."
            };

            try
            {
                using (var serverManager = await GetDefaultServerManager())
                {
                    if (serverManager == null)
                    {
                        configChecks.Add(defaultConfigError);
                        return configChecks;
                    }

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
                configChecks.Add(defaultConfigError);
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
                if (srv.Sites.Count < 0)
                {
                    throw new Exception("IIS is not installed");
                }
            }
            catch (Exception)
            {
                try
                {
                    srv = new ServerManager(@"C:\Windows\System32\inetsrv\config\applicationHost.config");
                    if (srv.Sites.Count < 0)
                    {
                        throw new Exception("IIS is not installed");
                    }
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

        private bool IsSiteStarted(Site site)
        {
            if (site == null)
            {
                return false;
            }

            try
            {
                if (site.Bindings?.Any(ftp => ftp.Protocol == "ftp") == true)
                {
                    try
                    {
                        if (site.State == ObjectState.Started)
                        {
                            // site has one or more ftp bindings but overall IIS site state is started
                            return true;
                        }
                    }
                    catch { }

                    //site is ftp site but may not be started, have to check for state differently
                    var ftpState = Convert.ToInt32(
                        site.GetChildElement("ftpServer")
                        .GetAttributeValue("state")
                        );

                    return ftpState == (int)ObjectState.Started;
                }

                else
                {
                    return site.State == ObjectState.Started;
                }
            }
            catch (Exception)
            {
                // if we get an exception testing state, assume site is running
                return true;
            }
        }
        private IEnumerable<Site> GetSites(ServerManager iisManager, bool includeOnlyStartedSites, string siteId = null)
        {
            try
            {
                var siteList = iisManager.Sites.AsQueryable();
                if (siteId != null)
                {
                    siteList = siteList.Where(s => s.Id.ToString() == siteId);
                }

                if (includeOnlyStartedSites)
                {

                    Func<Site, bool> isSiteStarted = (site) =>
                         {
                             return IsSiteStarted(site);
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
        public async Task<List<SiteInfo>> GetPrimarySites(bool includeOnlyStartedSites)
        {
            var result = new List<SiteInfo>();

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
                                var b = new SiteInfo()
                                {
                                    ServerType = StandardServerTypes.IIS,
                                    Id = site.Id.ToString(),
                                    Name = site.Name
                                };

                                try
                                {
                                    b.Path = site.Applications["/"]?.VirtualDirectories["/"]?.PhysicalPath;

                                    if (site.Bindings?.Any(ftp => ftp.Protocol == "ftp") == true)
                                    {
                                        //site is ftp site, have to check for state differently
                                        var ftpState = Convert.ToInt32(site.GetChildElement("ftpServer").GetAttributeValue("state"));

                                        b.IsEnabled = (ftpState == (int)ObjectState.Started);
                                    }
                                    else
                                    {
                                        b.IsEnabled = (site.State == ObjectState.Started);
                                    }

                                    // if any binding has a certificate hash assigned, assume this site has one or more certificates
                                    b.HasCertificate = site.Bindings?.Any(bi => bi.CertificateHash != null) ?? false;
                                }
                                catch (Exception exp)
                                {
                                    System.Diagnostics.Debug.WriteLine("Exception reading IIS Site state value:" + site.Name + " :: " + exp.ToString());
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

            return result.OrderBy(s => s.Name).ToList();
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
            if (string.IsNullOrEmpty(bindingSpec.SiteId))
            {
                throw new Exception("IIS.AddOrUpdateSiteBinding: SiteId not specified");
            }

            if (bindingSpec.IsFtpSite)
            {
                return await AddOrUpdateFtpSiteBinding(bindingSpec, addNew);
            }

            var result = new ActionStep { };
            var remainingAttempts = 3;
            var isCompleted = false;
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

                                        binding.CertificateStoreName = bindingSpec.CertificateStore;
                                        binding.CertificateHash = bindingSpec.CertificateHashBytes;
                                        binding.BindingInformation = bindingSpecString;
                                    }
                                    else
                                    {
                                        binding.Protocol = bindingSpec.Protocol.WithDefault("http");
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
                                                    binding.SslFlags = binding.SslFlags | SslFlags.Sni;

                                                }
                                                catch (Exception)
                                                {
                                                    //failed to set requested SNI flag

                                                    return new ActionStep { HasError = true, Description = $"Failed to set SNI flag on IIS Binding: {bindingSpec}" };
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (bindingSpec.IsSNIEnabled && !isSNISupported)
                                            {
                                                result = new ActionStep { HasWarning = true, Description = $"SNI was requested on this IIS Binding but is not supported by this version of IIS. Duplicate certificate bindings can occur unless each certificate is bound to a distinct IP address : {bindingSpec}" };
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
                    _log?.Error(exp, "Exception during AddOrUpdateSiteBinding {exp}", exp);

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

        public async Task<ActionStep> AddOrUpdateFtpSiteBinding(BindingInfo bindingSpec, bool addNew)
        {
            if (string.IsNullOrEmpty(bindingSpec.SiteId))
            {
                throw new Exception("IIS.AddOrUpdateFtpSiteBinding: SiteId not specified");
            }

            var result = new ActionStep { };
            var remainingAttempts = 3;
            var isCompleted = false;
            while (!isCompleted && remainingAttempts > 0)
            {
                try
                {
                    using (var iisManager = await GetDefaultServerManager())
                    {

                        lock (_iisAPILock)
                        {
                            var site = iisManager.Sites.FirstOrDefault(s => s.Id == long.Parse(bindingSpec.SiteId));

                            if (site != null)
                            {

                                var ssl = site.ChildElements["ftpServer"].ChildElements["security"].ChildElements["ssl"];

                                ssl["serverCertHash"] = bindingSpec.CertificateHash;
                                ssl["serverCertStoreName"] = bindingSpec.CertificateStore;

                                result = new ActionStep { HasWarning = false, Description = $"New ftp ssl binding added : {bindingSpec}" };

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
                    // get site binding info. If specific siteId being checked then query regardless of site state
                    var sites = GetSites(iisManager, (siteId != null ? false : ignoreStoppedSites), siteId);

                    foreach (var site in sites)
                    {
                        foreach (var binding in site.Bindings.OrderByDescending(b => b?.EndPoint?.Port))
                        {
                            var bindingDetails = binding.Protocol.StartsWith("ftp") ? GetFtpSiteBinding(site, binding) : GetSiteBinding(site, binding);

                            //ignore bindings which are not http or https
                            if (bindingDetails != null)
                            {
                                if (bindingDetails.Protocol?.ToLower().StartsWith("http") == true || bindingDetails.Protocol?.ToLower().StartsWith("ftp") == true)
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

        private string HashBytesToThumprint(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                // inspired by:
                // dotnet core System.Security.Cryptography.X509Certificates/src/Internal/Cryptography/Helpers.cs
                // CertificateHash is stored as 2 nibbles (4-bits) per byte, so convert bytes > hex
                var output = "";
                foreach (var b in bytes)
                {
                    output += ((byte)(b >> 4)).ToString("X");
                    output += ((byte)(b & 0xF)).ToString("X");
                }

                return output;
            }
            catch
            {
                // failed to convert bytes to hex. Cert hash is probably invalid.
                return null;
            }
        }

        private BindingInfo GetSiteBinding(Site site, Binding binding)
        {
            try
            {
                var siteInfo = Map(site);

                byte[] bindingHash = null;

                try
                {
                    // attempting to read certificate hash for an invalid cert can cause an exception
                    bindingHash = binding.CertificateHash;
                }
                catch { }

                var hasSNI = false;
                try
                {
                    // some bindings may error checking SNI
                    hasSNI = binding.SslFlags.HasFlag(SslFlags.Sni);
                }
                catch { }

                return new BindingInfo()
                {
                    ServerType = StandardServerTypes.IIS.ToString(),
                    SiteId = siteInfo.Id,
                    SiteName = siteInfo.Name,
                    PhysicalPath = siteInfo.Path,
                    Host = binding.Host.ToLower(),
                    IP = binding.EndPoint?.Address?.ToString(),
                    Port = binding.EndPoint?.Port ?? 0,
                    IsHTTPS = binding.Protocol.ToLower() == "https",
                    IsSNIEnabled = hasSNI,
                    Protocol = binding.Protocol.ToLower(),
                    HasCertificate = (bindingHash != null),
                    CertificateHash = bindingHash != null ? HashBytesToThumprint(bindingHash) : null,
                    CertificateHashBytes = bindingHash
                };
            }
            catch (Exception exp)
            {
                _log?.Warning($"IIS - GetSiteBinding failed :: {site.Name} {site.Id} :: {exp}");
                return null;
            }
        }

        private BindingInfo GetFtpSiteBinding(Site site, Binding binding)
        {
            try
            {
                var siteInfo = Map(site);

                byte[] bindingHash = null;

                try
                {
                    // attempting to read certificate hash for an invalid cert can cause an exception
                    bindingHash = binding.CertificateHash;
                }
                catch
                {
                }

                var bindingComponents = binding.BindingInformation.Split(':');

                var port = 21; //default port if we can't parse the configuration

                if (!string.IsNullOrWhiteSpace(bindingComponents[1]))
                {

                    if (int.TryParse(bindingComponents[1].Trim(), out var parsedPort))
                    {
                        port = parsedPort;
                    }
                }

                var hasSNI = false;
                try
                {
                    // some bindings may error checking SNI
                    hasSNI = binding.SslFlags.HasFlag(SslFlags.Sni);
                }
                catch { }

                return new BindingInfo()
                {
                    ServerType = StandardServerTypes.IIS.ToString(),
                    SiteId = siteInfo.Id,
                    SiteName = siteInfo.Name,
                    PhysicalPath = siteInfo.Path,
                    Host = bindingComponents[2],
                    IP = bindingComponents[0],
                    Port = port,
                    IsHTTPS = false,
                    IsSNIEnabled = hasSNI,
                    Protocol = binding.Protocol.ToLower(),
                    HasCertificate = (bindingHash != null),
                    CertificateHash = bindingHash != null ? HashBytesToThumprint(bindingHash) : null,
                    CertificateHashBytes = bindingHash,
                    IsFtpSite = true
                };
            }
            catch (Exception exp)
            {
                _log?.Warning($"IIS - GetFtpSiteBinding failed :: {site.Name} {site.Id} :: {exp}");
                return null;
            }
        }

        private async Task<Site> GetIISSiteByDomain(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return null;
            }

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
                    var bindingInformation = (ipAddress != null ? (ipAddress + ":") : "")
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
                if (iisManager == null)
                {
                    return false;
                }

                return (iisManager.Sites[siteName] != null);
            }
        }

        public async Task DeleteSite(string siteName)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
                {
                    while (iisManager.Sites[siteName] != null)
                    {
                        var siteToRemove = iisManager.Sites[siteName];
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
                    s.Path = site.Applications["/"]?.VirtualDirectories["/"]?.PhysicalPath;
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
                    var siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
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
                var siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);
                return siteDetails;
            }
        }

        public async Task<bool> IsSiteRunning(string id)
        {
            using (var iisManager = await GetDefaultServerManager())
            {
                var siteDetails = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == id);

                return IsSiteStarted(siteDetails);
            }
        }

        /// <summary>
        /// removes the sites https binding for the dns host name specified
        /// </summary>
        /// <param name="siteId">  </param>
        /// <param name="host">  </param>
        public async Task RemoveHttpsBinding(string siteId, string host)
        {
            if (string.IsNullOrEmpty(siteId))
            {
                throw new Exception("RemoveHttpsBinding: No siteId for IIS Site");
            }

            using (var iisManager = await GetDefaultServerManager())
            {
                lock (_iisAPILock)
                {
                    var site = iisManager.Sites.FirstOrDefault(s => s.Id.ToString() == siteId);

                    if (site != null)
                    {
                        var internationalHost = host == "" ? "" : _idnMapping.GetUnicode(host);

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

        public ServerTypeInfo GetServerTypeInfo()
        {
            return new ServerTypeInfo { ServerType = StandardServerTypes.IIS, Title = "IIS" };
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

        public IISBindingDeploymentTarget(ServerProviderIIS iisManager)
        {
            _iisManager = iisManager;
        }

        public async Task<ActionStep> AddBinding(BindingInfo targetBinding) => await _iisManager.AddOrUpdateSiteBinding(targetBinding, true);

        public async Task<ActionStep> UpdateBinding(BindingInfo targetBinding) => await _iisManager.AddOrUpdateSiteBinding(targetBinding, false);

        public async Task<List<IBindingDeploymentTargetItem>> GetAllTargetItems()
        {
            var sites = await _iisManager.GetPrimarySites(true);

            return sites.Select(s =>
                (IBindingDeploymentTargetItem)new IISBindingTargetItem
                {
                    Id = s.Id,
                    Name = s.Name
                }).ToList();
        }

        public async Task<List<BindingInfo>> GetBindings(string targetItemId) => await _iisManager.GetSiteBindingList(true, targetItemId);

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

        public string GetTargetName() => "IIS";
    }
}
