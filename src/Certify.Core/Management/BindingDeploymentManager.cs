using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Core.Management
{
    public class BindingDeploymentManager
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();

        private bool _enableCertDoubleImportBehaviour { get; set; } = true;

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

                dnsHosts = dnsHosts
                    .Distinct()
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList();

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
            else
            {
                if (requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment)
                {
                    actions.Add(new ActionStep { HasError = true, Title = "Certificate Storage", Description = "Certificate could not be stored." });
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
                if (!string.IsNullOrEmpty(managedCertificate.ServerSiteId))
                {
                    var site = await deploymentTarget.GetTargetItem(managedCertificate.ServerSiteId);
                    if (site != null) targetSites.Add(site);
                }
            }

            // or add all sites (if required)
            if (requestConfig.DeploymentSiteOption == DeploymentOption.AllSites || requestConfig.DeploymentSiteOption == DeploymentOption.Auto)
            {
                targetSites.AddRange(await deploymentTarget.GetAllTargetItems());
            }

            // for each sites we want to target, identify bindings to add/update as required
            foreach (var site in targetSites)
            {
                try
                {
                    var existingBindings = await deploymentTarget.GetBindings(site.Id);

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
                            if (string.IsNullOrEmpty(hostname) && requestConfig.DeploymentBindingBlankHostname)
                            {
                                updateBinding = true;
                            }
                            else
                            {
                                if (requestConfig.DeploymentBindingMatchHostname)
                                {
                                    updateBinding = ManagedCertificate.IsDomainOrWildcardMatch(dnsHosts, hostname);
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
                            var stepActions = await UpdateBinding(
                                deploymentTarget,
                                site,
                                existingBindings,
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

        public static bool HasExistingBinding(List<BindingInfo> bindings, BindingInfo spec)
        {
            string[] unassignedIPs = new string[] {
                "",
                "0.0.0.0",
                "*"
            };
            foreach (var b in bindings)
            {
                if (b.Protocol == spec.Protocol && (
                    (b.Host == spec.Host) &&
                    (
                       (unassignedIPs.Contains(b.IP) && unassignedIPs.Contains(spec.IP))
                       ||
                       (spec.IP == b.IP)
                    )
                ) && b.Port == spec.Port)
                {
                    return true;
                }
            }

            return false;
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
        public async Task<List<ActionStep>> UpdateBinding(
                                                                IBindingDeploymentTarget deploymentTarget,
                                                                IBindingDeploymentTargetItem site,
                                                                List<BindingInfo> existingBindings,
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
            var steps = new List<ActionStep>();

            var internationalHost = host == "" ? "" : ToUnicodeString(host);

            var bindingSpecString = $"{(!string.IsNullOrEmpty(ipAddress) ? ipAddress : "*")}:{sslPort}:{internationalHost}";

            var bindingSpec = new BindingInfo
            {
                Host = internationalHost,
                Protocol = "https",
                Port = sslPort,
                IP = !string.IsNullOrEmpty(ipAddress) ? ipAddress : "*",
                SiteId = site.Id,
                CertificateStore = "MY",
                CertificateHashBytes = certificateHash,
                IsSNIEnabled = useSNI
            };

            if (!HasExistingBinding(existingBindings, bindingSpec))
            {
                //there are no existing https bindings to update for this domain
                //add new https binding at default port "<ip>:port:hostDnsName";

                var action = new ActionStep
                {
                    Title = "Install Certificate For Binding",
                    Category = "Deployment.AddBinding",
                    Description = $"Add https binding | {site.Name} | **{bindingSpecString}**",
                    Key = $"[{site.Id}]:{bindingSpecString}:{useSNI}"
                };

                if (!isPreviewOnly)
                {
                    var result = await deploymentTarget.AddBinding(bindingSpec);
                    if (result.HasError)
                    {
                        // failed to add
                        action.HasError = true;
                        action.Description += $" Failed to add binding. [{result.Description}]";
                    }
                    else
                    {
                        if (result.HasWarning)
                        {
                            action.HasWarning = true;
                            action.Description += $" [{result.Description}]";
                        }
                    }
                }

                steps.Add(action);
            }
            else
            {
                // update one or more existing https bindings with new cert
                var action = new ActionStep
                {
                    Title = "Install Certificate For Binding",
                    Category = "Deployment.UpdateBinding",
                    Description = $"Update https binding | {site.Name} | **{bindingSpecString}**"
                };

                if (!isPreviewOnly)
                {
                    // Update existing https Binding
                    var result = await deploymentTarget.UpdateBinding(bindingSpec);

                    if (result.HasError)
                    {
                        // failed to update
                        action.HasError = true;
                        action.Description += $" Failed to update binding. [{result.Description}]";
                    }
                    else
                    {
                        if (result.HasWarning)
                        {
                            // has update warning
                            action.HasWarning = true;
                            action.Description += $" [{result.Description}]";
                        }
                    }
                }

                steps.Add(action);
            }

            return steps;
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
            if (string.IsNullOrEmpty(input)) return input;
            //if string already has (non-ascii range) unicode characters return original
            if (input.Any(c => c > 255)) return input;

            return _idnMapping.GetUnicode(input);
        }
    }
}
