﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Core.Management
{
    public class BindingDeploymentManager : IBindingDeploymentManager
    {
        private bool _enableCertDoubleImportBehaviour { get; set; } = true;

        private static string[] unassignedIPs = new string[] {
                null,
                "",
                "0.0.0.0",
                "::",
                "*"
            };

        private static string[] nonSpecificHostnames = new string[] {
                null,
                "",
                "*"
            };

        /// <summary>
        /// Creates or updates the https bindings associated with the dns names in the current
        /// request config, using the requested port/ips or autobinding
        /// </summary>
        /// <param name="requestConfig">  </param>
        /// <param name="pfxPath">  </param>
        /// <param name="cleanupCertStore">  </param>
        /// <returns>  </returns>
        public async Task<List<ActionStep>> StoreAndDeploy(IBindingDeploymentTarget deploymentTarget, ManagedCertificate managedCertificate, string pfxPath, string pfxPwd, bool isPreviewOnly, string certStoreName)
        {
            var actions = new List<ActionStep>();

            var requestConfig = managedCertificate.RequestConfig;

            if (!isPreviewOnly)
            {
                if (new System.IO.FileInfo(pfxPath).Length == 0)
                {
                    throw new ArgumentException("InstallCertForRequest: Invalid PFX File");
                }
            }

            //store cert in default store against primary domain
            if (string.IsNullOrEmpty(certStoreName))
            {
                certStoreName = CertificateManager.DEFAULT_STORE_NAME;
            }

            X509Certificate2 storedCert = null;
            byte[] certHash = null;

            // unless user has opted not to store cert, store it now
            if (requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment)
            {
                if (!isPreviewOnly)
                {
                    try
                    {
                        storedCert = await CertificateManager.StoreCertificate(requestConfig.PrimaryDomain, pfxPath, isRetry: false, enableRetryBehaviour: _enableCertDoubleImportBehaviour, pwd: pfxPwd, storeName: certStoreName);
                        if (storedCert != null)
                        {
                            certHash = storedCert.GetCertHash();

                            // TODO: move setting friendly name to cert request manager
                            managedCertificate.CertificateFriendlyName = storedCert.FriendlyName;

                            actions.Add(new ActionStep { HasError = false, Title = "Certificate Stored", Category = "CertificateStorage", Description = "Certificate stored OK" });
                        }
                    }
                    catch (Exception exp)
                    {
                        actions.Add(new ActionStep { HasError = true, Title = "Certificate Storage Failed", Category = "CertificateStorage", Description = "Error storing certificate." + exp.Message });
                        return actions;
                    }
                }
                else
                {
                    //fake cert for preview only
                    storedCert = new X509Certificate2();
                    certHash = new byte[] { 0x00, 0x01, 0x02 };

                    actions.Add(new ActionStep { HasError = false, Title = "Certificate Storage", Category = "CertificateStorage", Description = $"Certificate will be stored in the computer certificate store [{certStoreName}]" });
                }
            }

            if (storedCert != null)
            {
                //get list of domains we need to create/update https bindings for
                var dnsHosts = new List<string> {
                    requestConfig.PrimaryDomain
                };

                if (requestConfig.SubjectAlternativeNames != null)
                {
                    foreach (var san in requestConfig.SubjectAlternativeNames)
                    {
                        dnsHosts.Add(san);
                    }
                }

                dnsHosts = dnsHosts
                    .Distinct()
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList();

                // depending on our deployment mode we decide which sites/bindings to update:
                var deployments = await DeployToAllTargetBindings(deploymentTarget, managedCertificate, requestConfig, certStoreName, certHash, dnsHosts, isPreviewOnly);

                actions.AddRange(deployments);
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
            var actions = new List<ActionStep>();
            var targetSites = new List<IBindingDeploymentTargetItem>();

            // ensure defaults applied for deployment mode
            requestConfig.ApplyDeploymentOptionDefaults();

            // if single site, add that
            if (requestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
            {
                if (!string.IsNullOrEmpty(managedCertificate.ServerSiteId))
                {
                    var site = await deploymentTarget.GetTargetItem(managedCertificate.ServerSiteId);
                    if (site != null)
                    {
                        targetSites.Add(site);
                    }
                }
            }

            // or add all sites (if required)
            if (requestConfig.DeploymentSiteOption == DeploymentOption.AllSites || requestConfig.DeploymentSiteOption == DeploymentOption.Auto)
            {
                targetSites.AddRange(await deploymentTarget.GetAllTargetItems());
            }

            // get all bindings for all potentially applicable sites
            var allBindings = new List<BindingInfo>();
            if (targetSites.Count == 1)
            {
                allBindings = await deploymentTarget.GetBindings(targetSites.FirstOrDefault()?.Id);
            }
            else
            {
                // get all bindings for all sites
                allBindings = await deploymentTarget.GetBindings(null);
            }

            // for each sites we want to target, identify the bindings to add/update as required
            foreach (var site in targetSites)
            {
                try
                {
                    //var existingBindings = await deploymentTarget.GetBindings(site.Id);
                    var existingBindings = allBindings.Where(b => b.SiteId == site.Id).ToList();

                    var existingHttps = existingBindings.Where(e => e.Protocol == "https").ToList();

                    //remove https bindings which already have an https equivalent (specific hostname or blank)
                    existingBindings.RemoveAll(b => existingHttps.Any(e => e.Host == b.Host) && b.Protocol == "http");

                    existingBindings = existingBindings.OrderBy(b => b.Protocol).ThenBy(b => b.Host).ToList();

                    // copy existing bindings so we can add/remove
                    var updatedBindings = existingBindings.ToList();

                    // for each binding create or update an https binding
                    foreach (var b in existingBindings)
                    {
                        var updateBinding = false;

                        //if binding is http and there is no https binding, create one
                        var hostname = b.Host;

                        // install the cert for this binding if the hostname matches, or we have a
                        // matching wildcard, or if there is no hostname specified in the binding

                        if (requestConfig.DeploymentBindingReplacePrevious || requestConfig.DeploymentSiteOption == DeploymentOption.Auto)
                        {
                            // if replacing previous, check if current binding cert hash matches
                            // previous cert hash
                            if (b.CertificateHash != null && (managedCertificate.CertificatePreviousThumbprintHash != null || managedCertificate.CertificateThumbprintHash != null))
                            {
                                if (string.Equals(b.CertificateHash, managedCertificate.CertificatePreviousThumbprintHash))
                                {
                                    updateBinding = true;
                                }
                                else if (string.Equals(b.CertificateHash, managedCertificate.CertificateThumbprintHash))
                                {
                                    updateBinding = true;
                                }
                            }
                        }

                        if (updateBinding == false)
                        {
                            // TODO: add wildcard match
                            if (nonSpecificHostnames.Contains(hostname) && requestConfig.DeploymentBindingBlankHostname)
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
                            if (b.Protocol != "https")
                            {
                                updateBinding = false;
                            }
                        }

                        if (b.Protocol != "http" && b.Protocol != "https" && b.Protocol != "ftp")
                        {
                            // skip bindings for other service types
                            updateBinding = false;
                        }

                        if (updateBinding)
                        {
                            //SSL port defaults to 443 or the config default, unless we already have an https binding, in which case re-use same port
                            var sslPort = b.IsFtpSite ? 21 : 443;
                            var targetIPAddress = "*";

                            if (!string.IsNullOrWhiteSpace(requestConfig.BindingPort))
                            {
                                sslPort = int.Parse(requestConfig.BindingPort);
                            }

                            if (b.Protocol == "https")
                            {
                                if (b.Port > 0)
                                {
                                    sslPort = b.Port;
                                }

                                if (!unassignedIPs.Contains(b.IP))
                                {
                                    targetIPAddress = b.IP;
                                }
                            }
                            else
                            {
                                if (!unassignedIPs.Contains(requestConfig.BindingIPAddress))
                                {
                                    targetIPAddress = requestConfig.BindingIPAddress;
                                }
                            }

                            //create/update binding and associate new cert

                            //if any binding elements configured, use those, otherwise auto bind using defaults and SNI
                            if (!b.IsFtpSite)
                            {
                                var stepActions = await UpdateWebBinding(
                                   deploymentTarget,
                                   site,
                                   updatedBindings,
                                   certStoreName,
                                   certHash,
                                   hostname,
                                   sslPort: sslPort,
                                   useSNI: (requestConfig.BindingUseSNI != null ? (bool)requestConfig.BindingUseSNI : true),
                                   ipAddress: targetIPAddress,
                                   alwaysRecreateBindings: requestConfig.AlwaysRecreateBindings,
                                   isPreviewOnly: isPreviewOnly
                               );

                                actions.AddRange(stepActions);
                            }
                            else
                            {

                                var stepActions = await UpdateFtpBinding(
                                   deploymentTarget,
                                   site,
                                   updatedBindings,
                                   certStoreName,
                                   managedCertificate.CertificateThumbprintHash,
                                   sslPort,
                                   hostname,
                                   ipAddress: targetIPAddress,
                                   isPreviewOnly: isPreviewOnly
                               );

                                actions.AddRange(stepActions);
                            }
                        }
                    }
                }
                catch (Exception exp)
                {
                    actions.Add(new ActionStep { Title = site.Name, Category = "Deploy.AddOrUpdateBindings", HasError = true, Description = exp.ToString() });
                }
            }

            return actions;
        }

        public static bool HasExistingBinding(List<BindingInfo> bindings, BindingInfo spec)
        {
            foreach (var b in bindings)
            {
                // same protocol
                if (b.Protocol == spec.Protocol && b.Port == spec.Port)
                {
                    // same or blank host
                    if (b.Host == spec.Host || (string.IsNullOrEmpty(b.Host) && string.IsNullOrEmpty(spec.Host)))
                    {
                        // same or unassigned IP
                        if (spec.IP == b.IP || (unassignedIPs.Contains(spec.IP) && unassignedIPs.Contains(b.IP)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// creates or updates the https binding for the dns host name specified, assigning the given
        /// certificate selected from the certificate store
        /// </summary>
        /// <param name="site">  </param>
        /// <param name="certificate">  </param>
        /// <param name="host">  </param>
        /// <param name="sslPort">  </param>
        /// <param name="useSNI">  </param>
        /// <param name="ipAddress">  </param>
        public async Task<List<ActionStep>> UpdateWebBinding(
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

            var internationalHost = host ?? "";

            if (unassignedIPs.Contains(ipAddress))
            {
                ipAddress = "*";
            }
            else
            {
                // specific IP address, SNI is not applicable
                useSNI = false;
            }

            // can't use SNI is hostname is blank or wildcard
            if (useSNI && nonSpecificHostnames.Contains(internationalHost))
            {
                useSNI = false;
            }

            var bindingSpecString = $"{ipAddress}:{sslPort}:{internationalHost}";

            var bindingSpec = new BindingInfo
            {
                Host = internationalHost,
                Protocol = "https",
                IsHTTPS = true,
                Port = sslPort,
                IP = ipAddress,
                SiteId = site.Id,
                CertificateStore = certStoreName,
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
                    Description = $"Add {bindingSpec.Protocol} binding | {site.Name} | **{bindingSpecString} {(useSNI ? "SNI" : "Non-SNI")}**",
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

                    // we have added a binding, add to our list of known bindings to avoid trying to add any duplicates
                    existingBindings.Add(bindingSpec);
                }
                else
                {
                    // preview mode, validate binding spec
                    if (string.IsNullOrEmpty(bindingSpec.SiteId))
                    {
                        action.HasError = true;
                        action.Description += $" Failed to add/update binding. [IIS Site Id could not be determined]";
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
                    Description = $"Update {bindingSpec.Protocol} binding | {site.Name} | **{bindingSpecString.Replace("*", "\\*")} {(useSNI ? "SNI" : "Non-SNI")}**"
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

        /// <summary>
        /// creates or updates the https binding for the dns host name specified, assigning the given
        /// certificate selected from the certificate store
        /// </summary>
        /// <param name="site">  </param>
        /// <param name="certificate">  </param>
        /// <param name="host">  </param>
        /// <param name="sslPort">  </param>
        /// <param name="ipAddress">  </param>
        public async Task<List<ActionStep>> UpdateFtpBinding(
                                                                IBindingDeploymentTarget deploymentTarget,
                                                                IBindingDeploymentTargetItem site,
                                                                List<BindingInfo> existingBindings,
                                                                string certStoreName,
                                                                string certificateHash,
                                                                int port,
                                                                string host,
                                                                string ipAddress,
                                                                bool isPreviewOnly = false
                                                                )
        {
            var steps = new List<ActionStep>();

            var internationalHost = host ?? "";

            var bindingSpecString = $"{ipAddress}:{port}:{internationalHost}";

            var bindingSpec = new BindingInfo
            {
                Host = internationalHost,
                Protocol = "ftp",
                IsHTTPS = true,
                Port = port,
                IP = ipAddress,
                SiteId = site.Id,
                CertificateStore = certStoreName,
                CertificateHash = certificateHash,
                IsFtpSite = true
            };

            if (!HasExistingBinding(existingBindings, bindingSpec))
            {
                // there are no existing applicable bindings to update for this domain
                // add new ftp binding at default port "<ip>:port:hostDnsName";

                var action = new ActionStep
                {
                    Title = "Install Certificate For FTP Binding",
                    Category = "Deployment.AddBinding",
                    Description = $"Add {bindingSpec.Protocol} binding | {site.Name} | **{bindingSpecString} **",
                    Key = $"[{site.Id}]:{bindingSpecString}"
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
                // update one or more existing bindings with new cert
                var action = new ActionStep
                {
                    Title = "Install Certificate For Binding",
                    Category = "Deployment.UpdateBinding",
                    Description = $"Update {bindingSpec.Protocol} binding | {site.Name} | **{bindingSpecString}**"
                };

                if (!isPreviewOnly)
                {
                    // Update existing Binding
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
    }
}
