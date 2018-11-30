using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Management
{
    public class PreviewManager
    {
        /// <summary>
        /// Generate a list of actions which will be performed on the next renewal of this managed certificate, populating 
        /// the description of each action with a Markdown format description
        /// </summary>
        /// <param name="item"></param>
        /// <param name="serverProvider"></param>
        /// <param name="certifyManager"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> GeneratePreview(ManagedCertificate item, ICertifiedServer serverProvider,
            ICertifyManager certifyManager)
        {
            var newLine = "\r\n";
            var steps = new List<ActionStep>();

            var stepIndex = 1;

            bool hasDomains = true;

            // ensure defaults are applied for the deployment mode, overwriting any previous selections
            item.RequestConfig.ApplyDeploymentOptionDefaults();

            if (string.IsNullOrEmpty(item.RequestConfig.PrimaryDomain))
            {
                hasDomains = false;
            }

            if (hasDomains)
            {
                var allCredentials = await new CredentialsManager().GetStoredCredentials();

                var allDomains = new List<string> { item.RequestConfig.PrimaryDomain };

                if (item.RequestConfig.SubjectAlternativeNames != null)
                {
                    allDomains.AddRange(item.RequestConfig.SubjectAlternativeNames);
                }
                allDomains = allDomains.Distinct().OrderBy(d => d).ToList();


                // certificate summary
                var certDescription = new StringBuilder();
                certDescription.AppendLine(
                    "A new certificate will be requested from the *Let's Encrypt* certificate authority for the following domains:"
                    );

                certDescription.AppendLine($"\n**{item.RequestConfig.PrimaryDomain}** (Primary Domain)");

                if (item.RequestConfig.SubjectAlternativeNames.Any(s => s != item.RequestConfig.PrimaryDomain))
                {
                    certDescription.AppendLine($" and will include the following *Subject Alternative Names*:");

                    foreach (var d in item.RequestConfig.SubjectAlternativeNames)
                    {
                        certDescription.AppendLine($"* {d} ");
                    }
                }

                steps.Add(new ActionStep
                {
                    Title = "Summary",
                    Description = certDescription.ToString()
                });

                // validation steps :
                // TODO: preview description should come from the challenge type provider

                var challengeInfo = new StringBuilder();
                foreach (var challengeConfig in item.RequestConfig.Challenges)
                {
                    challengeInfo.AppendLine(
                        $"{newLine}Authorization will be attempted using the **{challengeConfig.ChallengeType}** challenge type." +
                        newLine
                        );

                    var matchingDomains = item.GetChallengeConfigDomainMatches(challengeConfig, allDomains);
                    if (matchingDomains.Any())
                    {
                        challengeInfo.AppendLine(
                            $"{newLine}The following matching domains will use this challenge: " + newLine
                            );

                        foreach (var d in matchingDomains)
                        {
                            challengeInfo.AppendLine($"{newLine} * {d}");
                        }
                    }
                    else
                    {
                        challengeInfo.AppendLine(
                        $"{newLine}**No domains will match this challenge type.** Either the challenge is not required or domain matches are not fully configured."
                        );
                    }

                    challengeInfo.AppendLine(newLine);

                    if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                    {
                        challengeInfo.AppendLine(
                           $"This will involve the creation of a randomly named (extensionless) text file for each domain (website) included in the certificate." +
                            newLine
                            );

                        if (CoreAppSettings.Current.EnableHttpChallengeServer)
                        {
                            challengeInfo.AppendLine(
                               $"The *Http Challenge Server* option is enabled. This will create a temporary web service on port 80 during validation. " +
                               $"This process co-exists with your main web server and listens for http challenge requests to /.well-known/acme-challenge/. " +
                               $"If you are using a web server on port 80 other than IIS (or other http.sys enabled server), that will be used instead." +
                               newLine
                               );
                        }

                        if (!string.IsNullOrEmpty(item.RequestConfig.WebsiteRootPath) && string.IsNullOrEmpty(challengeConfig.ChallengeRootPath))
                        {
                            challengeInfo.AppendLine(
                                $"The file will be created at the path `{item.RequestConfig.WebsiteRootPath}\\.well-known\\acme-challenge\\` " +
                                newLine
                                );
                        }

                        if (!string.IsNullOrEmpty(challengeConfig.ChallengeRootPath))
                        {
                            challengeInfo.AppendLine(
                                $"The file will be created at the path `{challengeConfig.ChallengeRootPath}\\.well-known\\acme-challenge\\` " +
                                newLine
                                );
                        }

                        challengeInfo.AppendLine(
                            $"The text file will need to be accessible from the URL `http://<yourdomain>/.well-known/acme-challenge/<randomfilename>` " +
                            newLine);
                            

                        challengeInfo.AppendLine(
                            $"Let's Encrypt will follow any redirection in place (such as rewriting the URL to *https*) but the initial request will be made via *http* on port 80. " +
                            newLine );

                    }

                    if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    {
                        challengeInfo.AppendLine(
                            $"This will involve the creation of a DNS TXT record named `_acme-challenge.yourdomain.com` for each domain or subdomain included in the certificate. " +
                            newLine);

                        if (!string.IsNullOrEmpty(challengeConfig.ChallengeCredentialKey))
                        {
                            var creds = allCredentials.FirstOrDefault(c => c.StorageKey == challengeConfig.ChallengeCredentialKey);
                            if (creds != null)
                            {
                                challengeInfo.AppendLine(
                               $"The following DNS API Credentials will be used:  **{creds.Title}** " + newLine );
                            }
                            else
                            {
                                challengeInfo.AppendLine(
                                    $"**Invalid credential settngs.**  The currently selected credential does not exist." 
                                    );
                            }
                        }
                        else
                        {
                            challengeInfo.AppendLine( 
                                $"No DNS API Credentials have been set.  API Credentials are normally required to make automatic updates to DNS records." 
                                );
                        }

                        challengeInfo.AppendLine( 
                            newLine + $"Let's Encrypt will follow any redirection in place (such as a substitute CNAME pointing to another domain) but the initial request will be made against any of the domain's nameservers. "
                            );
                    }

                    if (!string.IsNullOrEmpty(challengeConfig.DomainMatch))
                    {
                        challengeInfo.AppendLine(
                            $"{newLine}This challenge type will be selected based on matching domain **{challengeConfig.DomainMatch}** ");
                    }
                    else
                    {
                        if (item.RequestConfig.Challenges.Count > 1)
                        {
                            challengeInfo.AppendLine(
                             $"{newLine}This challenge type will be selected for any domain not matched by another challenge. ");

                            
                        }
                        else
                        {
                            challengeInfo.AppendLine(
                          $"{newLine}**This challenge type will be selected for all domains.**");
                        }
                    }

                    challengeInfo.AppendLine($"{newLine}---{newLine}");
                }

                steps.Add(new ActionStep
                {
                    Title = $"{stepIndex}. Domain Validation",
                    Category="Validation",
                    Description = challengeInfo.ToString()
                });
                stepIndex++;

                // pre request scripting steps
                if (!string.IsNullOrEmpty(item.RequestConfig.PreRequestPowerShellScript))
                {
                    steps.Add(new ActionStep
                    {
                        Title = $"{stepIndex}. Pre-Request Powershell",
                        Category="PreRequestScripting",
                        Description = $"Execute PowerShell Script: *{item.RequestConfig.PreRequestPowerShellScript}*"
                    });
                    stepIndex++;
                }

                // cert request step
                var certRequest =
                    $"A Certificate Signing Request (CSR) will be submitted to the *Let's Encrypt certificate authority*, using the **{item.RequestConfig.CSRKeyAlg}** signing algorithm.";
                steps.Add(new ActionStep
                {
                    Title = $"{stepIndex}. Certificate Request",
                    Category="CertificateRequest",
                    Description = certRequest
                });
                stepIndex++;

                // post request scripting steps
                if (!string.IsNullOrEmpty(item.RequestConfig.PostRequestPowerShellScript))
                {
                    steps.Add(new ActionStep
                    {
                        Title = $"{stepIndex}. Post-Request Powershell",
                        Category="PostRequestScripting",
                        Description = $"Execute PowerShell Script: *{item.RequestConfig.PostRequestPowerShellScript}*"
                    });
                    stepIndex++;
                }

                // webhook scripting steps
                if (!string.IsNullOrEmpty(item.RequestConfig.WebhookUrl))
                {
                    steps.Add(new ActionStep
                    {
                        Title = $"{stepIndex}. Post-Request WebHook",
                        Description = $"Execute WebHook *{item.RequestConfig.WebhookUrl}*"
                    });

                    stepIndex++;
                }

                // deployment & binding steps

                var deploymentDescription = new StringBuilder();
                var deploymentStep = new ActionStep
                {
                    Title = $"{stepIndex}. Deployment",
                    Category="Deployment",
                    Description = ""
                };

                if (
                    item.RequestConfig.DeploymentSiteOption == DeploymentOption.Auto ||
                    item.RequestConfig.DeploymentSiteOption == DeploymentOption.AllSites ||
                    item.RequestConfig.DeploymentSiteOption == DeploymentOption.SingleSite
                )
                {
                    // deploying to single or multiple Site
                    if (item.ItemType == ManagedCertificateType.SSL_LetsEncrypt_LocalIIS)
                    {
                        if (item.RequestConfig.DeploymentBindingMatchHostname)
                            deploymentDescription.AppendLine(
                                "* Deploy to hostname bindings matching certificate domains.");

                        if (item.RequestConfig.DeploymentBindingBlankHostname)
                            deploymentDescription.AppendLine("* Deploy to bindings with blank hostname.");

                        if (item.RequestConfig.DeploymentBindingReplacePrevious)
                            deploymentDescription.AppendLine("* Deploy to bindings with previous certificate.");

                        if (item.RequestConfig.DeploymentBindingOption == DeploymentBindingOption.AddOrUpdate)
                            deploymentDescription.AppendLine("* Add or Update https bindings as required");

                        if (item.RequestConfig.DeploymentBindingOption == DeploymentBindingOption.UpdateOnly)
                            deploymentDescription.AppendLine("* Update https bindings as required (no auto-created https bindings)");

                        if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
                        {
                            if (!string.IsNullOrEmpty(item.ServerSiteId))
                                try
                                {
                                    var siteInfo = await serverProvider.GetSiteById(item.ServerSiteId);
                                    deploymentDescription.AppendLine($"## Deploying to Site" + newLine + newLine +
                                                             $"`{siteInfo.Name}`" + newLine );
                                }
                                catch (Exception exp)
                                {
                                    deploymentDescription.AppendLine($"Error: **cannot identify selected site.** {exp.Message} ");
                                }
                        }
                        else
                        {
                            deploymentDescription.AppendLine($"## Deploying to all matching sites:");
                        }

                        // add deployment sub-steps (if any)
                        var bindingRequest = await certifyManager.DeployCertificate(item, null, true);

                        if (bindingRequest.Actions != null) deploymentStep.Substeps = bindingRequest.Actions;

                        if (bindingRequest.Actions == null || !bindingRequest.Actions.Any())
                        {
                            deploymentStep.Substeps = new List<ActionStep>
                        {
                            new ActionStep {Description = newLine + "**There are no matching targets to deploy to. Certificate will be stored but currently no bindings will be updated.**"}
                        };
                        }
                        else
                        {
                            deploymentDescription.AppendLine(" Action | Site | Binding ");
                            deploymentDescription.AppendLine(" ------ | ---- | ------- ");
                        }
                    }
                    else
                    {
                        // no website selected, maybe validating http with a manually specified path
                       deploymentDescription.AppendLine($"Manual deployment to site.");
                    }
                }
                else if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.DeploymentStoreOnly)
                {
                    deploymentDescription.AppendLine("* The certificate will be saved to the local machines Certificate Store only (Personal/My Store)");
                }
                else if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.NoDeployment)
                {
                    deploymentDescription.AppendLine("* The certificate will be saved to local disk only.");
                }

                deploymentStep.Description = deploymentDescription.ToString();

                steps.Add(deploymentStep);
                stepIndex++;

                stepIndex = steps.Count;
            }
            else
            {
                steps.Add(new ActionStep
                {
                    Title = "Certificate has no domains",
                    Description = "No domains have been added to this certificate, so a certificate cannot be requested. Each certificate requires a primary domain (a 'subject') and an optional list of additional domains (subject alternative names)."
                });
            }

            return steps;
        }

        /// <summary>
        /// WIP: For current configured environment, show preview of recommended site management (for
        ///      local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public async Task<List<ManagedCertificate>> PreviewManagedCertificates(StandardServerTypes serverType,
            ICertifiedServer serverProvider, ICertifyManager certifyManager)
        {
            var sites = new List<ManagedCertificate>();

            if (serverType == StandardServerTypes.IIS)
                try
                {
                    var allSites = await serverProvider.GetSiteBindingList(CoreAppSettings.Current.IgnoreStoppedSites);
                    var iisSites = allSites
                        .OrderBy(s => s.SiteId)
                        .ThenBy(s => s.Host);

                    var siteIds = iisSites.GroupBy(x => x.SiteId);

                    foreach (var s in siteIds)
                    {
                        var managedCertificate = new ManagedCertificate { Id = s.Key };
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

            return sites;
        }
    }
}
