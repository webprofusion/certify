using Certify.Models;
using Certify.Models.Providers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class PreviewManager
    {
        /// <summary>
        /// Generate a list of actions which will be performed on the next renewal of this managed certificate 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="serverProvider"></param>
        /// <param name="certifyManager"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> GeneratePreview(ManagedCertificate item, ICertifiedServer serverProvider, ICertifyManager certifyManager)
        {
            List<ActionStep> steps = new List<ActionStep>();

            int stepIndex = 1;

            // certificate summary
            string certDescription = "A new certificate will be requested from the Let's Encrypt certificate authority for the following domains:\n";
            certDescription += $"\n{ item.RequestConfig.PrimaryDomain } (Primary Domain) ";
            if (item.RequestConfig.SubjectAlternativeNames.Any(s => s != item.RequestConfig.PrimaryDomain))
            {
                certDescription += $"\nIncluding the following Subject Alternative Names:\n\n";

                foreach (var d in item.RequestConfig.SubjectAlternativeNames)
                {
                    certDescription += $"\t{ d} \n";
                }
            }

            steps.Add(new ActionStep { Title = "Summary", Description = certDescription });

            // validation steps
            string validationDescription = "";
            foreach (var challengeConfig in item.RequestConfig.Challenges)
            {
                validationDescription += $"Attempt authorization using the {challengeConfig.ChallengeType} challenge type.\r\n";
            }

            // if using http-01, describe steps

            // if using dns-01, describe steps

            steps.Add(new ActionStep { Title = $"{stepIndex}. Domain Validation", Description = validationDescription });
            stepIndex++;

            // pre request scripting steps
            if (!String.IsNullOrEmpty(item.RequestConfig.PreRequestPowerShellScript))
            {
                steps.Add(new ActionStep { Title = $"{stepIndex}. Pre-Request Powershell", Description = $"Execute PowerShell Script" });
                stepIndex++;
            }

            // cert request step
            string certRequest = $"Certificate Signing Request {item.RequestConfig.CSRKeyAlg}.";
            steps.Add(new ActionStep { Title = $"{stepIndex}. Certificate Request", Description = certRequest });
            stepIndex++;

            // post request scripting steps
            if (!String.IsNullOrEmpty(item.RequestConfig.PostRequestPowerShellScript))
            {
                steps.Add(new ActionStep { Title = $"{stepIndex}. Post-Request Powershell", Description = $"Execute PowerShell Script" });
                stepIndex++;
            }

            // webhook scripting steps
            if (!String.IsNullOrEmpty(item.RequestConfig.WebhookUrl))
            {
                steps.Add(new ActionStep { Title = $"{stepIndex}. Post-Request WebHook", Description = $"Execute WebHook {item.RequestConfig.WebhookUrl}" });

                stepIndex++;
            }

            // deployment & binding steps

            if (!String.IsNullOrEmpty(item.ServerSiteId))
            {
                if (item.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
                {
                    string deploymentDescription = $"Save certificate only.";
                    var deploymentStep = new ActionStep { Title = $"{stepIndex}. Deployment", Description = deploymentDescription };

                    if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
                    {
                        if (!String.IsNullOrEmpty(item.ServerSiteId))
                        {
                            try
                            {
                                var siteInfo = serverProvider.GetSiteById(item.ServerSiteId);
                                deploymentDescription = $"Deploy to IIS Site: {siteInfo.Name}  [{siteInfo.Id}] {item.RequestConfig.WebsiteRootPath} ";
                            }
                            catch (Exception exp)
                            {
                                deploymentDescription = $"Error: cannot identify selected site. {exp.Message} ";
                            }

                            // add deployment sub-steps (if any)
                            var bindingRequest = await certifyManager.ReapplyCertificateBindings(item, null, isPreviewOnly: true);
                            if (bindingRequest.Actions != null) deploymentStep.Substeps = bindingRequest.Actions;
                        }
                        else
                        {
                            // no website selected, maybe validating http with a manually specified path
                            if (!String.IsNullOrEmpty(item.RequestConfig.WebsiteRootPath))
                            {
                                deploymentDescription = $"Deploying to manual site: {item.RequestConfig.WebsiteRootPath}";
                            }
                        }
                    }

                    deploymentStep.Description = deploymentDescription;

                    steps.Add(deploymentStep);
                    stepIndex++;
                }
                else
                {
                    string deploymentDescription = $"Save certificate only.";
                    var deploymentStep = new ActionStep { Title = $"{stepIndex}. Deployment", Description = deploymentDescription };

                    if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.AllSites)
                    {
                        if (item.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
                        {
                            deploymentDescription = "Deploying to All IIS Sites on server";

                            // add deployment sub-steps (if any)
                            var bindingRequest = await certifyManager.ReapplyCertificateBindings(item, null, isPreviewOnly: true);
                            if (bindingRequest.Actions != null) deploymentStep.Substeps = bindingRequest.Actions;
                        }
                    }
                    else if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.DeploymentStoreOnly)
                    {
                        deploymentDescription = "Deploying to Certificate Store only (old certificates not removed)";
                    }
                    else if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.NoDeployment)
                    {
                        deploymentDescription = "Saving certificate to local disk only, no deployment.";
                    }

                    deploymentStep.Description = deploymentDescription;

                    steps.Add(deploymentStep);
                    stepIndex++;
                }
            }

            stepIndex = steps.Count;

            return steps;
        }

        /// <summary>
        /// WIP: For current configured environment, show preview of recommended site management (for
        /// local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public Task<List<ManagedCertificate>> PreviewManagedCertificates(StandardServerTypes serverType, ICertifiedServer serverProvider, ICertifyManager certifyManager)
        {
            List<ManagedCertificate> sites = new List<ManagedCertificate>();

            // FIXME: IIS query is not async
            if (serverType == StandardServerTypes.IIS)
            {
                try
                {
                    var iisSites = serverProvider.GetSiteBindingList(ignoreStoppedSites: CoreAppSettings.Current.IgnoreStoppedSites).OrderBy(s => s.SiteId).ThenBy(s => s.Host);

                    var siteIds = iisSites.GroupBy(x => x.SiteId);

                    foreach (var s in siteIds)
                    {
                        ManagedCertificate managedCertificate = new ManagedCertificate { Id = s.Key };
                        managedCertificate.ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS;
                        managedCertificate.TargetHost = "localhost";
                        managedCertificate.Name = iisSites.First(i => i.SiteId == s.Key).SiteName;

                        //TODO: replace site binding with domain options
                        //managedCertificate.SiteBindings = new List<ManagedCertificateBinding>();

                        /* foreach (var binding in s)
                         {
                             var managedBinding = new ManagedCertificateBinding { Hostname = binding.Host, IP = binding.IP, Port = binding.Port, UseSNI = true, CertName = "Certify_" + binding.Host };
                             // managedCertificate.SiteBindings.Add(managedBinding);
                         }*/
                        sites.Add(managedCertificate);
                    }
                }
                catch (Exception)
                {
                    //can't read sites
                    Debug.WriteLine("Can't get IIS site list.");
                }
            }
            return Task.FromResult(sites);
        }
    }
}