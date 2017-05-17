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
            try
            {
                using (var iisManager = new ServerManager())
                {
                    var sites = GetSites(iisManager, includeOnlyStartedSites);

                    foreach (var site in sites)
                    {
                        var b = new SiteBindingItem()
                        {
                            SiteId = site.Id.ToString(),
                            SiteName = site.Name,

                            PhysicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath,
                        };
                        result.Add(b);
                    }
                }
            }
            catch (Exception) { }

            return result.OrderBy(s => s.SiteName).ToList();
        }

        public List<SiteBindingItem> GetSiteBindingList(bool includeOnlyStartedSites, string siteId = null)
        {
            var result = new List<SiteBindingItem>();
            try
            {
                using (var iisManager = new ServerManager())
                {
                    var sites = GetSites(iisManager, includeOnlyStartedSites);

                    if (siteId != null) sites = sites.Where(s => s.Id.ToString() == siteId);
                    foreach (var site in sites)
                    {
                        foreach (var binding in site.Bindings.OrderByDescending(b => b?.EndPoint?.Port))
                        {
                            if (string.IsNullOrEmpty(binding.Host)) continue;
                            if (result.Any(r => r.Host == binding.Host)) continue;

                            result.Add(GetSiteBinding(site, binding));
                        }
                    }
                }
            }
            catch (Exception)
            {
                ;//can't query IIS
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
            domain = _idnMapping.GetUnicode(domain);
            using (var iisManager = new ServerManager())
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

        #endregion IIS

        #region Certificates

        public X509Certificate2 StoreCertificate(string host, string pfxFile)
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            //TODO: remove old cert?
            var certificate = new X509Certificate2(pfxFile, "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            certificate.GetExpirationDateString();
            certificate.FriendlyName = host + " [Certify] - " + certificate.GetEffectiveDateString() + " to " + certificate.GetExpirationDateString();

            store.Add(certificate);
            store.Close();
            return certificate;
        }

        public void CleanupCertificateDuplicates(X509Certificate2 certificate, string hostPrefix)
        {
            bool requireCertifySpecificCerts = false;

            if (certificate.FriendlyName.Length < 10) return;

            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            var certsToRemove = new List<X509Certificate2>();
            foreach (var c in store.Certificates)
            {
                if (requireCertifySpecificCerts)
                {
                    if (c.FriendlyName.StartsWith(hostPrefix, StringComparison.InvariantCulture) && c.GetCertHashString() != certificate.GetCertHashString())
                    {
                        //going to remove certs with same friendly name
                        certsToRemove.Add(c);
                    }
                }
                else
                {
                    if (c.FriendlyName.StartsWith(hostPrefix, StringComparison.InvariantCulture) && c.GetCertHashString() != certificate.GetCertHashString())
                    {
                        //going to remove certs with same friendly name
                        certsToRemove.Add(c);
                    }
                }
            }
            foreach (var oldCert in certsToRemove)
            {
                try
                {
                    store.Remove(oldCert);
                }
                catch (Exception exp)
                {
                    // Couldn't remove it
                    System.Diagnostics.Debug.WriteLine("Could not remove cert:" + oldCert.FriendlyName + " " + exp.ToString());
                }
            }

            store.Close();
        }

        /// <summary>
        /// Creates or updates the htttps bindinds associated with the dns names in the current request config, using the requested port/ips or autobinding
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
            var storedCert = StoreCertificate(requestConfig.PrimaryDomain, pfxPath);

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
                            //TODO: make use SNI optional in request config.
                            InstallCertificateforBinding(site, storedCert, hostname, sslPort: !String.IsNullOrEmpty(requestConfig.BindingPort) ? int.Parse(requestConfig.BindingPort) : 443, useSNI: true, ipAddress: requestConfig.BindingIPAddress);
                        }
                    }
                }

                if (cleanupCertStore)
                {
                    //remove old certs for this primary domain
                    CleanupCertificateDuplicates(storedCert, requestConfig.PrimaryDomain);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// creates or updates the https binding for the dns host name specified, assigning the given certificate selected from the certificate store
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
            using (var iisManager = new ServerManager())
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
                var storedCert = StoreCertificate(hostDnsName, pfxPath);
                if (storedCert != null)
                {
                    if (!skipBindings)
                    {
                        InstallCertificateforBinding(site, storedCert, hostDnsName);
                    }
                    if (cleanupCertStore)
                    {
                        CleanupCertificateDuplicates(storedCert, hostDnsName);
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Add/update registry keys to disable insecure SSL/TLS protocols
        /// </summary>
        public void PerformSSLProtocolLockdown()
        {
            DisableSSLViaRegistry("SSL 2.0");
            DisableSSLViaRegistry("SSL 3.0");

            DisableSSLCipherViaRegistry("DES 56/56");
            DisableSSLCipherViaRegistry("RC2 40/128");
            DisableSSLCipherViaRegistry("RC2 56/128");
            DisableSSLCipherViaRegistry("RC4 128/128");
            DisableSSLCipherViaRegistry("RC4 40/128");
            DisableSSLCipherViaRegistry("RC4 56/128");
            DisableSSLCipherViaRegistry("RC4 64/128");
            DisableSSLCipherViaRegistry("RC4 128/128");

            //TODO: enable other SSL
        }

        #endregion Certificates

        #region Registry

        private RegistryKey GetRegistryBaseKey(RegistryHive hiveType)
        {
            if (Environment.Is64BitOperatingSystem)
            {
                return RegistryKey.OpenBaseKey(hiveType, RegistryView.Registry64);
            }
            else
            {
                return RegistryKey.OpenBaseKey(hiveType, RegistryView.Registry32);
            }
        }

        private void DisableSSLViaRegistry(string protocolKey)
        {
            //check if client key exists, if not create it
            //set \Client\DisabledByDefault=1

            //RegistryKey SSLProtocolsKey =  Registry.LocalMachine..OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\", true);
            RegistryKey SSLProtocolsKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\", true);
            RegistryKey SSLProtocolKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\" + protocolKey, true);

            //create key for protocol if it doesn't exist
            if (SSLProtocolKey == null)
            {
                SSLProtocolKey = SSLProtocolsKey.CreateSubKey(protocolKey);
            }

            //create Client key if required
            RegistryKey clientKey = SSLProtocolKey.OpenSubKey("Client", true);
            if (clientKey == null)
            {
                clientKey = SSLProtocolKey.CreateSubKey("Client");
            }

            //DisabledByDefault=1

            clientKey.SetValue("DisabledByDefault", 1, RegistryValueKind.DWord);
            clientKey.Close();
            //set \Server\Enabled=0
            RegistryKey serverKey = SSLProtocolKey.OpenSubKey("Server", true);
            if (serverKey == null)
            {
                serverKey = SSLProtocolKey.CreateSubKey("Server");
            }

            serverKey.SetValue("Enabled", 0, RegistryValueKind.DWord);
            serverKey.Close();
        }

        private void DisableSSLCipherViaRegistry(string cipher)
        {
            RegistryKey cipherTypesKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers\", true);

            RegistryKey cipherKey = GetRegistryBaseKey(RegistryHive.LocalMachine).OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers\" + cipher, true);

            if (cipherKey == null)
            {
                cipherKey = cipherTypesKey.CreateSubKey(cipher);
            }

            cipherKey.SetValue("Enabled", 0, RegistryValueKind.DWord);
            cipherKey.Close();
        }

        #endregion Registry
    }
}