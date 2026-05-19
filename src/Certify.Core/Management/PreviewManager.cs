using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Hub;
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
        public async Task<List<ActionStep>> GeneratePreview(
                    ManagedCertificate item,
                    ITargetWebServer serverProvider,
                    ICertifyManager certifyManager,
                    ICredentialsManager credentialsManager
            )
        {
            var newLine = "\r\n";
            var steps = new List<ActionStep>();

            var stepIndex = 1;

            var allTaskProviders = await certifyManager.GetDeploymentProviders();
            var certificateAuthorities = await certifyManager.GetCertificateAuthorities();

            // ensure defaults are applied for the deployment mode, overwriting any previous selections
            item.RequestConfig.ApplyDeploymentOptionDefaults();

            if (item.ItemType == ManagedCertificateType.SSL_ExternallyManaged)
            {
                return await GenerateExternalSubscriptionPreview(item, serverProvider, certifyManager, allTaskProviders);
            }

            var identifiers = item.GetCertificateIdentifiers();

            if (identifiers.Any())
            {
                var allCredentials = await credentialsManager.GetCredentials();

                var allIdentifiers = item.GetCertificateIdentifiers();

                // certificate summary
                var certDescription = new StringBuilder();
                var ca = certificateAuthorities.FirstOrDefault(c => c.Id == item.CertificateAuthorityId);

                certDescription.AppendLine($"A new certificate will be requested from the **{ca?.Title.AsNullWhenBlank() ?? "Default"}** certificate authority for the following identifiers:");

                if (item.RequestConfig?.PreferredExpiryDays > 0)
                {
                    certDescription.AppendLine($"The certificate will be requested with a custom lifetime of **{item.RequestConfig.PreferredExpiryDays}** days.");
                }

                if (identifiers.Any(d => d.IdentifierType == CertIdentifierType.Dns))
                {

                    certDescription.AppendLine($"\n**{item.RequestConfig.PrimaryDomain}** (Primary Domain)");

                    if (item.RequestConfig.SubjectAlternativeNames.Any(s => s != item.RequestConfig.PrimaryDomain))
                    {
                        certDescription.AppendLine($" and will include the following *Subject Alternative Names*:");

                        foreach (var d in item.RequestConfig.SubjectAlternativeNames)
                        {
                            certDescription.AppendLine($"* {d} ");
                        }
                    }
                }

                if (identifiers.Any(d => d.IdentifierType != CertIdentifierType.Dns))
                {
                    foreach (var ident in identifiers.Where(i => i.IdentifierType != CertIdentifierType.Dns))
                    {
                        certDescription.AppendLine($"* {ident.Value} [{ident.IdentifierType}]");
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

                    var matchingDomains = item.GetChallengeConfigDomainMatches(challengeConfig, allIdentifiers);
                    if (matchingDomains.Any())
                    {
                        challengeInfo.AppendLine(
                            $"{newLine}The following matching identifiers will use this challenge: " + newLine
                            );

                        foreach (var d in matchingDomains)
                        {
                            challengeInfo.AppendLine($"{newLine} * {d}");
                        }

                        if (allIdentifiers.Any(i => i.IdentifierType == CertIdentifierType.Dns))
                        {
                            challengeInfo.AppendLine(
                               $"{newLine}**Please review the Deployment section below to ensure this certificate will be applied to the expected website bindings (if any).**" + newLine
                               );
                        }
                    }
                    else
                    {
                        challengeInfo.AppendLine(
                        $"{newLine}*No domains will match this challenge type.* Either the challenge is not required or domain matches are not fully configured."
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
                            $"The issuing Certificate Authority will follow any redirection in place (such as rewriting the URL to *https*) but the initial request will be made via *http* on port 80. " +
                            newLine);
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
                               $"The following DNS API Credentials will be used:  **{creds.Title}** " + newLine);
                            }
                            else
                            {
                                challengeInfo.AppendLine(
                                    $"**Invalid credential settings.**  The currently selected credential does not exist."
                                    );
                            }
                        }
                        else
                        {
                            challengeInfo.AppendLine(
                                $"No DNS API Credentials have been set. API Credentials are normally required to make automatic updates to DNS records."
                                );
                        }

                        challengeInfo.AppendLine(
                            newLine + $"The issuing Certificate Authority will follow any redirection in place (such as a substitute CNAME pointing to another domain) but the initial request will be made against any of the domain's nameservers. "
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
                             $"{newLine}This challenge type will be selected for any identifier not matched by another challenge. ");
                        }
                        else
                        {
                            challengeInfo.AppendLine(
                          $"{newLine}**This challenge type will be selected for all identifiers.**");
                        }
                    }
                }

                steps.Add(new ActionStep
                {
                    Title = $"{stepIndex}. Validation",
                    Category = "Validation",
                    Description = challengeInfo.ToString()
                });
                stepIndex++;

                // pre request tasks
                if (item.PreRequestTasks?.Any() == true)
                {
                    var substeps = item.PreRequestTasks.Select(t => new ActionStep { Key = t.Id, Title = $"{t.TaskName} ({allTaskProviders.FirstOrDefault(p => p.Id == t.TaskTypeId)?.Title})", Description = t.Description });

                    steps.Add(new ActionStep
                    {
                        Title = $"{stepIndex}. Pre-Request Tasks",
                        Category = "PreRequestTasks",
                        Description = $"Execute {substeps.Count()} Pre-Request Tasks",
                        Substeps = substeps.ToList()
                    });
                    stepIndex++;
                }

                // cert request step

                var preferredKeyType = !string.IsNullOrEmpty(item.RequestConfig.CSRKeyAlg) ? item.RequestConfig.CSRKeyAlg : CoreAppSettings.Current.DefaultKeyType;
                if (string.IsNullOrEmpty(preferredKeyType))
                {
                    preferredKeyType = StandardKeyTypes.RSA256; // MS Exchange etc do not support ECDSA key types so we default to RSA
                }

                var certRequest =
                    $"A Certificate Signing Request (CSR) will be submitted to the Certificate Authority, using the **{preferredKeyType}** signing algorithm.";
                steps.Add(new ActionStep
                {
                    Title = $"{stepIndex}. Certificate Request",
                    Category = "CertificateRequest",
                    Description = certRequest
                });
                stepIndex++;

                stepIndex = await AddDeploymentPreview(steps, item, serverProvider, certifyManager, stepIndex);

                stepIndex = AddPostRequestTaskPreview(steps, item, allTaskProviders, stepIndex);

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

        private async Task<List<ActionStep>> GenerateExternalSubscriptionPreview(
            ManagedCertificate item,
            ITargetWebServer serverProvider,
            ICertifyManager certifyManager,
            IEnumerable<Certify.Models.Config.DeploymentProviderDefinition> allTaskProviders)
        {
            var steps = new List<ActionStep>();
            var stepIndex = 1;

            var source = item.ExternalSource;
            if (string.IsNullOrEmpty(source?.ExternalReference))
            {
                steps.Add(new ActionStep
                {
                    Title = "External subscription not configured",
                    Description = "This managed certificate is set to use an external subscription, but the subscription is not currently configured."
                });

                return steps;
            }

            var summary = new StringBuilder();
            summary.AppendLine("This managed certificate uses an external certificate subscription.");
            summary.AppendLine("A local renewal request preview will not be performed for this item.");
            summary.AppendLine();

            var summaryRows = new List<KeyValuePair<string, string>>();
            var remoteSummary = await ResolveRemoteManagedCertificateSummary(certifyManager, source);
            var remoteTitle = remoteSummary?.Title.AsNullWhenBlank()
                              ?? source.SourceItemName.AsNullWhenBlank()
                              ?? source.ExternalReference.AsNullWhenBlank()
                              ?? "(Not selected)";

            summaryRows.Add(new KeyValuePair<string, string>("Remote Item", remoteTitle));
            summaryRows.Add(new KeyValuePair<string, string>("Source Type", source.SourceType.AsNullWhenBlank() ?? "Unknown"));
            summaryRows.Add(new KeyValuePair<string, string>("Retrieval Mode", source.RetrievalMode.AsNullWhenBlank() ?? ExternalCertificateRetrievalModes.Pull));

            if (!string.IsNullOrWhiteSpace(source.ExternalReference))
            {
                summaryRows.Add(new KeyValuePair<string, string>("Reference", $"`{source.ExternalReference}`"));
            }

            if (remoteSummary != null)
            {
                if (!string.IsNullOrWhiteSpace(remoteSummary.InstanceTitle))
                {
                    summaryRows.Add(new KeyValuePair<string, string>("Remote Instance", remoteSummary.InstanceTitle));
                }

                if (!string.IsNullOrWhiteSpace(remoteSummary.Status))
                {
                    summaryRows.Add(new KeyValuePair<string, string>("Remote Status", remoteSummary.Status));
                }

                if (remoteSummary.DateExpiry.HasValue)
                {
                    summaryRows.Add(new KeyValuePair<string, string>("Remote Expiry", remoteSummary.DateExpiry.Value.ToString("u")));
                }

                AppendMarkdownTable(summary, summaryRows);

                if (remoteSummary.Identifiers?.Any() == true)
                {
                    summary.AppendLine();
                    summary.AppendLine("The remote certificate summary currently includes the following identifiers:");

                    foreach (var identifier in remoteSummary.Identifiers)
                    {
                        summary.AppendLine($"* {identifier.Value} [{identifier.IdentifierType}]");
                    }
                }

                if (!string.IsNullOrWhiteSpace(remoteSummary.Comments))
                {
                    summary.AppendLine();
                    summary.AppendLine($"**Remote Notes:** {remoteSummary.Comments}");
                }
            }
            else
            {
                AppendMarkdownTable(summary, summaryRows);
                summary.AppendLine();
                summary.AppendLine("The remote item summary is not currently available, so the preview will use the saved subscription details.");
            }

            steps.Add(new ActionStep
            {
                Title = "Summary",
                Description = summary.ToString()
            });

            summary.AppendLine();
            summary.AppendLine("On sync, Certify will check the configured external source for the selected remote certificate.");
            summary.AppendLine("If an updated certificate is available, it will be downloaded and staged for local deployment.");
            summary.AppendLine("No ACME order, challenge validation, or local certificate renewal request is performed for this workflow.");

            if (source.PollIntervalMinutes > 0
                && !string.Equals(source.RetrievalMode, ExternalCertificateRetrievalModes.Push, StringComparison.OrdinalIgnoreCase))
            {
                summary.AppendLine($"The source will be polled every **{source.PollIntervalMinutes}** minutes when polling is applicable.");
            }

            if (!string.IsNullOrWhiteSpace(source.PendingSourceVersion) || !string.IsNullOrWhiteSpace(source.PendingCertificatePath))
            {
                summary.AppendLine("A pending external certificate update is currently recorded and may wait for the next applicable maintenance window before deployment.");
            }

            // Enrich the item with source identifiers so that BindingDeploymentManager can match
            // server hostname bindings when generating the deployment preview. For real deployment
            // the same enrichment is applied from the parsed PFX in ApplyExternalCertificateMetadata.
            var sourceIdentifiers = remoteSummary?.Identifiers?.ToList();
            if (sourceIdentifiers?.Any() == true)
            {
                item.ApplySourceIdentifiers(sourceIdentifiers);
            }

            stepIndex = await AddDeploymentPreview(steps, item, serverProvider, certifyManager, stepIndex);

            AddPostRequestTaskPreview(steps, item, allTaskProviders, stepIndex);

            return steps;
        }

        private async Task<ManagedCertificateSummary?> ResolveRemoteManagedCertificateSummary(ICertifyManager certifyManager, ExternalCertificateSubscription source)
        {
            if (!string.Equals(source.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(source.ExternalReference))
            {
                return null;
            }

            var summaryItems = await certifyManager.GetHubSubscribableManagedCertificates();

            return summaryItems.FirstOrDefault(i => string.Equals($"{i.InstanceId}/{i.Id}", source.ExternalReference, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<int> AddDeploymentPreview(List<ActionStep> steps, ManagedCertificate item, ITargetWebServer? serverProvider, ICertifyManager certifyManager, int stepIndex)
        {
            var newLine = "\r\n";
            var deploymentDescription = new StringBuilder();
            var deploymentStep = new ActionStep
            {
                Title = $"{stepIndex}. Deployment",
                Category = "Deployment",
                Description = ""
            };

            if (
                item.RequestConfig.DeploymentSiteOption == DeploymentOption.Auto ||
                item.RequestConfig.DeploymentSiteOption == DeploymentOption.AllSites ||
                item.RequestConfig.DeploymentSiteOption == DeploymentOption.SingleSite
            )
            {
                if (serverProvider == null)
                {
                    deploymentDescription.AppendLine("* Target instance has no supported targets for auto-deployment (e.g. IIS). Deployment Tasks may still apply.");
                }
                else
                {
                    if (item.RequestConfig.DeploymentBindingMatchHostname)
                    {
                        deploymentDescription.AppendLine("* Deploy to hostname bindings matching certificate domains.");
                    }

                    if (item.RequestConfig.DeploymentBindingBlankHostname)
                    {
                        deploymentDescription.AppendLine("* Deploy to bindings with blank hostname.");
                    }

                    if (item.RequestConfig.DeploymentBindingReplacePrevious)
                    {
                        deploymentDescription.AppendLine("* Deploy to bindings with previous certificate.");
                    }

                    if (item.RequestConfig.DeploymentBindingOption == DeploymentBindingOption.AddOrUpdate)
                    {
                        deploymentDescription.AppendLine("* Add or Update https bindings as required");
                    }

                    if (item.RequestConfig.DeploymentBindingOption == DeploymentBindingOption.UpdateOnly)
                    {
                        deploymentDescription.AppendLine("* Update https bindings as required (no auto-created https bindings)");
                    }

                    if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
                    {
                        if (!string.IsNullOrEmpty(item.ServerSiteId))
                        {
                            try
                            {
                                var siteInfo = await serverProvider.GetSiteById(item.ServerSiteId);
                                deploymentDescription.AppendLine($"## Deploying to Site{newLine}{newLine}`{siteInfo.Name}`{newLine}");
                            }
                            catch (Exception exp)
                            {
                                deploymentDescription.AppendLine($"Error: **cannot identify selected site.** {exp.Message} ");
                            }
                        }
                    }
                    else
                    {
                        deploymentDescription.AppendLine("## Deploying to all matching sites:");
                    }
                }

                var bindingRequest = await certifyManager.DeployCertificate(item, null, true);
                if (bindingRequest.Actions?.Any(b => b.Category == "CertificateStorage") == true)
                {
                    var defaultValue = "Unknown Storage";
                    deploymentDescription.AppendLine(bindingRequest.Actions.First(b => b.Category == "CertificateStorage")?.Description.WithDefault(defaultValue) ?? defaultValue);
                }
                else
                {
                    deploymentDescription.AppendLine("**Certificate will not be added to the machine certificate store**");

                    deploymentStep.Substeps = new List<ActionStep>
                    {
                        new ActionStep {Description = newLine + "**Certificate will not be added to the machine certificate store**"}
                    };
                }

                if (!bindingRequest.Actions?.Any(b => b.Category.StartsWith("Deployment")) == true)
                {
                    deploymentStep.Substeps = new List<ActionStep>
                    {
                        new ActionStep {Description = newLine + "**There are no matching targets to deploy to. Certificate will be stored but currently no bindings will be updated.**"}
                    };
                }
                else
                {
                    deploymentStep.Substeps = bindingRequest.Actions.Where(b => b.Category.StartsWith("Deployment")).ToList();

                    deploymentDescription.AppendLine("\n Action | Site | Binding ");
                    deploymentDescription.Append(" ------ | ---- | ------- ");
                }
            }
            else if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.DeploymentStoreOnly)
            {
                deploymentDescription.AppendLine("* The certificate will be saved to the local machines Certificate Store only (Personal/My Store)");
            }
            else if (item.RequestConfig.DeploymentSiteOption == DeploymentOption.NoDeployment)
            {
                deploymentDescription.AppendLine("* The certificate will be saved to local storage.");
            }

            deploymentStep.Description = deploymentDescription.ToString();

            steps.Add(deploymentStep);

            return stepIndex + 1;
        }

        private int AddPostRequestTaskPreview(List<ActionStep> steps, ManagedCertificate item, IEnumerable<Certify.Models.Config.DeploymentProviderDefinition> allTaskProviders, int stepIndex)
        {
            if (item.PostRequestTasks?.Any() != true)
            {
                return stepIndex;
            }

            var substeps = item.PostRequestTasks.Select(t => new ActionStep { Key = t.Id, Title = $"{t.TaskName} ({allTaskProviders.FirstOrDefault(p => p.Id == t.TaskTypeId)?.Title})", Description = t.Description });

            steps.Add(new ActionStep
            {
                Title = $"{stepIndex}. Post-Request (Deployment) Tasks",
                Category = "PostRequestTasks",
                Description = $"Execute {substeps.Count()} Post-Request Tasks",
                Substeps = substeps.ToList()
            });

            return stepIndex + 1;
        }

        private static void AppendMarkdownTable(StringBuilder summary, IEnumerable<KeyValuePair<string, string>> rows)
        {
            summary.AppendLine("| Item | Value |");
            summary.AppendLine("| --- | --- |");

            foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
            {
                summary.AppendLine($"| {EscapeMarkdownTableCell(row.Key)} | {EscapeMarkdownTableCell(row.Value)} |");
            }
        }

        private static string EscapeMarkdownTableCell(string value)
        {
            return value
                .Replace("\r\n", "<br />")
                .Replace("\n", "<br />")
                .Replace("\r", "<br />")
                .Replace("|", "\\|");
        }

        /// <summary>
        /// WIP: For current configured environment, show preview of recommended site management (for
        ///      local IIS, scan sites and recommend actions)
        /// </summary>
        /// <returns></returns>
        public async Task<List<ManagedCertificate>> PreviewManagedCertificates(StandardServerTypes serverType,
            ITargetWebServer serverProvider, ICertifyManager certifyManager)
        {
            var sites = new List<ManagedCertificate>();

            try
            {
                var allSites = await serverProvider.GetSiteBindingList(CoreAppSettings.Current.IgnoreStoppedSites);
                var targetSites = allSites
                    .OrderBy(s => s.SiteId)
                    .ThenBy(s => s.Host);

                var siteIds = targetSites.GroupBy(x => x.SiteId);

                foreach (var s in siteIds)
                {
                    var managedCertificate = new ManagedCertificate { Id = s.Key };
                    managedCertificate.ItemType = ManagedCertificateType.SSL_ACME;

                    managedCertificate.Name = targetSites.First(i => i.SiteId == s.Key).SiteName;

                    sites.Add(managedCertificate);
                }
            }
            catch (Exception)
            {
                //can't read sites
                Debug.WriteLine("Can't get target server site list.");
            }

            return sites;
        }
    }
}
