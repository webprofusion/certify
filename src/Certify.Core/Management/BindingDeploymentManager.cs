using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Core.Management
{
    public interface IBindingDeploymentTarget
    {
        string GetTargetName();

        Task<IBindingDeploymentTargetItem> GetTargetItem(string id);

        Task<List<IBindingDeploymentTargetItem>> GetAllTargetItems();

        ICertifiedServer GetDeploymentManager();

        bool HasExistingBinding(string targetItemid, string protocol, string ipAddress, int port, string hostname);

        bool AddBinding(string targetItemid, string protocol, string ipAddress, int port, string hostname, string certStoreName, byte[] certificateHash);

        bool UpdateBinding(string targetItemid, string protocol, string ipAddress, int port, string hostname, string certStoreName, byte[] certificateHash);

        List<BindingInfo> GetBindings(string targetItemId);
    }

    public interface IBindingDeploymentTargetItem
    {
        string Id { get; set; }
        string Name { get; set; }
    }

    public class MockBindingDeploymentTargetItem : IBindingDeploymentTargetItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class MockBindingDeploymentTarget : IBindingDeploymentTarget
    {
        public List<BindingInfo> AllBindings { get; set; } = new List<BindingInfo>();

        public async Task<IBindingDeploymentTargetItem> GetTargetItem(string id)
        {
            var firstMatch = AllBindings.FirstOrDefault(f => f.Id == id);
            if (firstMatch != null)
            {
                return new MockBindingDeploymentTargetItem { Id = firstMatch.Id, Name = firstMatch.Name };
            }
            else
            {
                return null;
            }
        }

        public async Task<List<IBindingDeploymentTargetItem>> GetAllTargetItems()
        {
            var all = new List<IBindingDeploymentTargetItem>();

            foreach (var b in AllBindings)
            {
                if (!all.Any(site => site.Id == b.Id))
                {
                    all.Add(new MockBindingDeploymentTargetItem { Id = b.Id, Name = b.Name });
                }
            }

            return await Task.FromResult(all);
        }

        public List<BindingInfo> GetBindings(string targetItemId)
        {
            return AllBindings.Where(b => b.Id == targetItemId).ToList();
        }

        public ICertifiedServer GetDeploymentManager()
        {
            return new MockServerManager();
        }

        public string GetTargetName()
        {
            return "Mock Binding Target";
        }

        public void Dispose()
        {
        }

        public bool AddBinding(string targetItemid, string protocol, string ipAddress, int port, string hostname, string certStoreName, byte[] certificateHash)
        {
            return true;
        }

        public bool HasExistingBinding(string targetItemid, string protocol, string ipAddress, int port, string hostname)
        {
            if (AllBindings.Any(b => b.Id == targetItemid && b.Protocol == protocol && b.Host == hostname && b.Port == port && b.IP == ipAddress))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool UpdateBinding(string targetItemid, string protocol, string ipAddress, int port, string hostname, string certStoreName, byte[] certificateHash)
        {
            return true;
        }
    }

    public class MockServerManager : ICertifiedServer
    {
        public Task<bool> CommitChanges()
        {
            return Task.FromResult(true);
        }

        public Task<bool> CreateManagementContext()
        {
            return Task.FromResult(true);
        }

        public void Dispose()
        {
        }

        public Task<List<BindingInfo>> GetPrimarySites(bool ignoreStoppedSites)
        {
            throw new NotImplementedException();
        }

        public Task<Version> GetServerVersion()
        {
            throw new NotImplementedException();
        }

        public Task<List<BindingInfo>> GetSiteBindingList(bool ignoreStoppedSites, string siteId = null)
        {
            throw new NotImplementedException();
        }

        public Task<SiteInfo> GetSiteById(string siteId)
        {
            throw new NotImplementedException();
        }

        public Task<List<ActionStep>> InstallCertForRequest(ManagedCertificate managedCertificate, string pfxPath, bool cleanupCertStore, bool isPreviewOnly)
        {
            throw new NotImplementedException();
        }

        public Task<List<ActionStep>> InstallCertificateforBinding(string certStoreName, byte[] certificateHash, ManagedCertificate managedCertificate, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsAvailable()
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsSiteRunning(string id)
        {
            throw new NotImplementedException();
        }

        public Task RemoveHttpsBinding(ManagedCertificate managedCertificate, string sni)
        {
            throw new NotImplementedException();
        }
    }

    public class BindingDeploymentManager
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();

        /// <summary>
        /// Creates or updates the https bindings associated with the dns names in the current
        /// request config, using the requested port/ips or autobinding
        /// </summary>
        /// <param name="requestConfig"></param>
        /// <param name="pfxPath"></param>
        /// <param name="cleanupCertStore"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> StoreAndDeployManagedCertificate(IBindingDeploymentTarget deploymentTarget, ManagedCertificate managedCertificate, string pfxPath, bool cleanupCertStore, bool isPreviewOnly)
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
            //FIXME:
            string certStoreName = "MY";// CertificateManager.GetDefaultStore().Name;
            X509Certificate2 storedCert = null;
            byte[] certHash = null;

            // unless user has opted not to store cert, store it now
            if (requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment)
            {
                if (!isPreviewOnly)
                {
                    // FIXME:
                    //storedCert = await CertificateManager.StoreCertificate(requestConfig.PrimaryDomain, pfxPath, isRetry: false, enableRetryBehaviour: _enableCertDoubleImportBehaviour);
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

                var deployments = await DeployToAllTargetBindings(deploymentTarget, managedCertificate, requestConfig, certStoreName, certHash, dnsHosts, isPreviewOnly);

                actions.AddRange(deployments);

                // if required, cleanup old certs we are replacing. Only applied if we have deployed
                // the certificate, otherwise we keep the old one

                // FIXME: need strategy to analyse if there are any users of cert we haven't
                //        accounted for (manually added etc) otherwise we are disposing of a cert
                //        which could still be in use
                if (!isPreviewOnly)
                {
                    if (cleanupCertStore
                        && requestConfig.DeploymentSiteOption != DeploymentOption.DeploymentStoreOnly
                         && requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment
                        )
                    {
                        //remove old certs for this primary domain
                        //FIXME:
                        //CertificateManager.CleanupCertificateDuplicates(storedCert, requestConfig.PrimaryDomain);
                    }
                }
            }

            // deployment tasks completed
            return actions;
        }

        private async Task<List<ActionStep>> DeployToAllTargetBindings(IBindingDeploymentTarget deploymentTarget,
                                                                    ManagedCertificate managedCertificate,
                                                                    CertRequestConfig requestConfig,
                                                                    string certStoreName,
                                                                    byte[] certHash,
                                                                    List<string> dnsHosts,
                                                                    bool isPreviewOnly = false
                                                                )
        {
            List<ActionStep> actions = new List<ActionStep>();
            var targetSites = new List<IBindingDeploymentTargetItem>();

            // if single site, add that
            if (requestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
            {
                var site = await deploymentTarget.GetTargetItem(managedCertificate.ServerSiteId);
                if (site != null) targetSites.Add(site);
            }

            // or add all sites (if required)
            if (requestConfig.DeploymentSiteOption == DeploymentOption.AllSites)
            {
                targetSites.AddRange(await deploymentTarget.GetAllTargetItems());
            }

            // for each sites we want to target, identify bindings to add/update as required
            foreach (var site in targetSites)
            {
                try
                {
                    var existingBindings = deploymentTarget.GetBindings(site.Id);

                    var existingHttps = existingBindings.Where(e => e.Protocol == "https").ToList();

                    //remove https bindings which already have an https equivalent (specific hostname or blank)
                    existingBindings.RemoveAll(b => existingHttps.Any(e => e.Host == b.Host) && b.Protocol == "http");

                    existingBindings = existingBindings.OrderBy(b => b.Protocol).ThenBy(b => b.Host).ToList();

                    // for each binding create or update an https binding
                    foreach (var b in existingBindings)
                    {
                        var updateBinding = false;

                        //if binding is http and there is no https binding, create one
                        var hostname = b.Host;

                        // install the cert for this binding if the hostname matches, or we have a
                        // matching wildcard, or if there is no hostname specified in the binding

                        if (requestConfig.DeploymentBindingReplacePrevious)
                        {
                            // if replacing previous, check if current binding cert hash matches
                            // previous cert hash
                            if (b.CertificateHash != null && managedCertificate.CertificatePreviousThumbprintHash != null)
                            {
                                if (string.Equals(b.CertificateHash, managedCertificate.CertificatePreviousThumbprintHash))
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
                            var stepActions = await AddOrUpdateBinding(deploymentTarget,
                                site,
                                certStoreName,
                                certHash,
                                hostname,
                                sslPort: !string.IsNullOrWhiteSpace(requestConfig.BindingPort) ? int.Parse(requestConfig.BindingPort) : 443,
                                useSNI: (requestConfig.BindingUseSNI != null ? (bool)requestConfig.BindingUseSNI : true),
                                ipAddress: !string.IsNullOrWhiteSpace(requestConfig.BindingIPAddress) ? requestConfig.BindingIPAddress : null,
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
        /*   public async Task<List<ActionStep>> InstallCertificateforBinding(IBindingDeploymentTarget deploymentTarget, string certStoreName, byte[] certificateHash, ManagedCertificate managedCertificate, string host, int sslPort = 443, bool useSNI = true, string ipAddress = null, bool alwaysRecreateBindings = false, bool isPreviewOnly = false)
           {
               var site = await deploymentTarget.GetTargetItem(managedCertificate.ServerSiteId);
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

               return await InstallCertificateforBinding(deploymentTarget, site, certStoreName, certificateHash, site, host, sslPort, useSNI, ipAddress, alwaysRecreateBindings, isPreviewOnly);
           }*/

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
        public async Task<List<ActionStep>> AddOrUpdateBinding(
                                                                IBindingDeploymentTarget deploymentTarget,
                                                                IBindingDeploymentTargetItem site,
                                                                string certStoreName,
                                                                byte[] certificateHash,
                                                                string host,
                                                                int sslPort = 443,
                                                                bool useSNI = true,
                                                                string ipAddress = null,
                                                                bool alwaysRecreateBindings = false,
                                                                bool isPreviewOnly = false
                                                                )
        {
            List<ActionStep> steps = new List<ActionStep>();

            // servers managers like IIS need to operate in a single current context

            using (var serverManager = deploymentTarget.GetDeploymentManager())
            {
                var internationalHost = host == "" ? "" : ToUnicodeString(host);
                var bindingSpec = $"{(!string.IsNullOrEmpty(ipAddress) ? ipAddress : "*")}:{sslPort}:{internationalHost}";

                var hasExistingBinding = deploymentTarget.HasExistingBinding(site.Id, "https", ipAddress, sslPort, internationalHost); //. //from b in siteToUpdate.Bindings where b.Host == internationalHost && b.Protocol == "https" select b;

                if (!hasExistingBinding)
                {
                    //there are no existing https bindings to update for this domain
                    //add new https binding at default port "<ip>:port:hostDnsName";

                    var action = new ActionStep
                    {
                        Title = "Install Certificate For Binding",
                        Category = "Deployment.AddBinding",
                        Description = $"* Add new https binding: [{site.Name}] **{bindingSpec}**",
                        Key = $"[{site.Id}]:{bindingSpec}:{useSNI}"
                    };

                    if (!isPreviewOnly)
                    {
                        deploymentTarget.AddBinding(site.Id, "https", ipAddress, sslPort, internationalHost, certStoreName, certificateHash);
                        /*var binding = siteToUpdate.Bindings.CreateElement();

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
                        siteToUpdate.Bindings.Add(binding);*/
                    }

                    steps.Add(action);
                }
                else
                {
                    // update one or more existing https bindings with new cert
                    deploymentTarget.UpdateBinding(site.Id, "https", ipAddress, sslPort, internationalHost, certStoreName, certificateHash);
                    /*foreach (var existingBinding in existingHttpsBindings)
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
                    }*/
                }

                if (!isPreviewOnly)
                {
                    await serverManager.CommitChanges();
                }
            }
            return steps;
        }

        public static bool IsDomainOrWildcardMatch(List<string> dnsNames, string hostname)
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

        private string ByteToHex(byte[] ba)
        {
            var sb = new System.Text.StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                sb.AppendFormat("{0:x2}", b);
            }
            return sb.ToString();
        }

        private string ToUnicodeString(string input)
        {
            //if string already has (non-ascii range) unicode characters return original
            if (input.Any(c => c > 255)) return input;

            return _idnMapping.GetUnicode(input);
        }
    }
}
