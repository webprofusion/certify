using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;

namespace Certify.Core.Management
{
    /// <summary>
    /// Manages the deployment of TLS certificates to web server and ftp server bindings (a certificate associated with a partcular IP+Port+hostname combination).
    /// Storage (in a certificate store) and deployment to a target server.
    /// Typical usage is to create or update bindings for a new certificate. On most operating systems certificate bindings can be against an IP and port combination
    /// however that quickly leads to IP address exhaustion. SNI (server name indication, available in Windows Server 2012 onwards etc) is typically used instead to host multiple certificates on a single IP+Port combination. 
    /// Generally for SNI bindings the IP is set to * or 0.0.0.0 for non-specific IP bindings.
    /// Operations can include a preview mode where the actual storage/deployment is not performed, and instead the changes are returned as a list of proposed actions that could be performed.
    /// </summary>
    public class BindingDeploymentManager : IBindingDeploymentManager
    {
        private static readonly ActivitySource _activitySource = new("Certify.BindingDeploymentManager");
        private static readonly Meter _meter = new("Certify.BindingDeploymentManager", "1.0.0");
        
        // Counters
        private static readonly Counter<int> _bindingsCreatedCounter = _meter.CreateCounter<int>("certify.bindings.created", "bindings", "Number of bindings created");
        private static readonly Counter<int> _bindingsUpdatedCounter = _meter.CreateCounter<int>("certify.bindings.updated", "bindings", "Number of bindings updated");
        private static readonly Counter<int> _bindingsFailedCounter = _meter.CreateCounter<int>("certify.bindings.failed", "bindings", "Number of binding operations that failed");
        private static readonly Counter<int> _certificatesStoredCounter = _meter.CreateCounter<int>("certify.certificates.stored", "certificates", "Number of certificates stored");
        private static readonly Counter<int> _certificateStorageFailedCounter = _meter.CreateCounter<int>("certify.certificates.storage_failed", "certificates", "Number of certificate storage failures");
        
        // Histograms
        private static readonly Histogram<double> _deploymentDurationHistogram = _meter.CreateHistogram<double>("certify.deployment.duration", "ms", "Duration of certificate deployment operations");
        private static readonly Histogram<double> _bindingOperationDurationHistogram = _meter.CreateHistogram<double>("certify.binding.operation_duration", "ms", "Duration of individual binding operations");
        private static readonly Histogram<int> _sitesProcessedHistogram = _meter.CreateHistogram<int>("certify.deployment.sites_processed", "sites", "Number of sites processed in a deployment");
        
        // Gauges (via ObservableGauge)
        private static int _activeDeployments = 0;
        private static readonly ObservableGauge<int> _activeDeploymentsGauge = _meter.CreateObservableGauge<int>("certify.deployment.active", () => _activeDeployments, "deployments", "Number of deployments currently in progress");

        private bool _enableCertDoubleImportBehaviour { get; set; } = true;

        private ILog _log;

        public BindingDeploymentManager(ILog log = null)
        {
            _log = log;
        }

        /// <summary>
        /// List of IP addresses that are considered unassigned or wildcard
        /// </summary>
        private static string[] unassignedIPs = new string[] {
                null,
                "",
                "0.0.0.0",
                "::",
                "*"
            };

        /// <summary>
        /// List of hostnames that are considered non-specific
        /// </summary>
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
            using var activity = _activitySource.StartActivity("StoreAndDeploy", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            System.Threading.Interlocked.Increment(ref _activeDeployments);
            
            try
            {
                // Add tags to activity for tracing
                activity?.SetTag("managed_cert.id", managedCertificate.Id);
                activity?.SetTag("managed_cert.name", managedCertificate.Name);
                activity?.SetTag("deployment.preview_only", isPreviewOnly);
                activity?.SetTag("deployment.site_option", managedCertificate.RequestConfig.DeploymentSiteOption.ToString());
                activity?.SetTag("deployment.binding_option", managedCertificate.RequestConfig.DeploymentBindingOption.ToString());
                activity?.SetTag("cert_store.name", certStoreName ?? CertificateManager.DEFAULT_STORE_NAME);
                
                _log?.Information("Starting certificate deployment for {ManagedCertName} (ID: {ManagedCertId}), Preview: {IsPreview}", 
                    managedCertificate.Name, managedCertificate.Id, isPreviewOnly);

                var actions = new List<ActionStep>();

                var requestConfig = managedCertificate.RequestConfig;

                if (!isPreviewOnly)
                {
                    if (new System.IO.FileInfo(pfxPath).Length == 0)
                    {
                        var ex = new ArgumentException("InstallCertForRequest: Invalid PFX File");
                        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        _certificateStorageFailedCounter.Add(1, new KeyValuePair<string, object>("error", "invalid_pfx"));
                        throw ex;
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
                        using var certStoreActivity = _activitySource.StartActivity("StoreCertificate", ActivityKind.Internal);
                        var certStoreStopwatch = Stopwatch.StartNew();
                        
                        try
                        {
                            certStoreActivity?.SetTag("cert.primary_domain", requestConfig.PrimaryDomain);
                            certStoreActivity?.SetTag("cert_store.name", certStoreName);
                            
                            storedCert = await CertificateManager.StoreCertificate(requestConfig.PrimaryDomain, pfxPath, isRetry: false, enableRetryBehaviour: _enableCertDoubleImportBehaviour, pwd: pfxPwd, storeName: certStoreName);
                            
                            if (storedCert != null)
                            {
                                certHash = storedCert.GetCertHash();

                                // TODO: move setting friendly name to cert request manager
                                managedCertificate.CertificateFriendlyName = storedCert.FriendlyName;

                                actions.Add(new ActionStep { HasError = false, Title = "Certificate Stored", Category = "CertificateStorage", Description = "Certificate stored OK" });
                                
                                _certificatesStoredCounter.Add(1, 
                                    new KeyValuePair<string, object>("cert_store.name", certStoreName),
                                    new KeyValuePair<string, object>("managed_cert.id", managedCertificate.Id));
                                
                                certStoreActivity?.SetStatus(ActivityStatusCode.Ok);
                                
                                _log?.Information("Certificate stored successfully in {CertStore} for {PrimaryDomain}", 
                                    certStoreName, requestConfig.PrimaryDomain);
                            }
                            else
                            {
                                _log?.Warning("Certificate storage returned null for {PrimaryDomain}", requestConfig.PrimaryDomain);
                            }
                        }
                        catch (Exception exp)
                        {
                            certStoreActivity?.SetStatus(ActivityStatusCode.Error, exp.Message);
                            certStoreActivity?.AddException(exp);
                            
                            _certificateStorageFailedCounter.Add(1, 
                                new KeyValuePair<string, object>("error", exp.GetType().Name),
                                new KeyValuePair<string, object>("cert_store.name", certStoreName));
                            
                            _log?.Error(exp, "Failed to store certificate for {PrimaryDomain} in {CertStore}", 
                                requestConfig.PrimaryDomain, certStoreName);
                            
                            actions.Add(new ActionStep { HasError = true, Title = "Certificate Storage Failed", Category = "CertificateStorage", Description = "Error storing certificate. " + exp.Message });
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
                    // if a deployment target is available (IIS, nginx etc) we will attempt to deploy the cert to the target
                    if (deploymentTarget != null)
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

                        activity?.SetTag("deployment.dns_hosts_count", dnsHosts.Count);
                        activity?.AddEvent(new ActivityEvent("DeployingToBindings", 
                            tags: new ActivityTagsCollection { { "dns_hosts", string.Join(", ", dnsHosts) } }));

                        // depending on our deployment mode we decide which sites/bindings to update:
                        var deployments = await DeployToAllTargetBindings(deploymentTarget, managedCertificate, requestConfig, certStoreName, certHash, dnsHosts, isPreviewOnly);

                        actions.AddRange(deployments);
                    }
                    else
                    {
                        _log?.Warning("No deployment target available for {ManagedCertName}", managedCertificate.Name);
                        activity?.AddEvent(new ActivityEvent("NoDeploymentTarget"));
                    }
                }
                else
                {
                    if (requestConfig.DeploymentSiteOption != DeploymentOption.NoDeployment)
                    {
                        actions.Add(new ActionStep { HasError = true, Title = "Certificate Storage", Description = "Certificate could not be stored." });
                        _log?.Error("Certificate storage failed - stored certificate is null for {ManagedCertName}", managedCertificate.Name);
                    }
                }

                stopwatch.Stop();
                
                // Record metrics
                var hasErrors = actions.Any(a => a.HasError);
                activity?.SetTag("deployment.has_errors", hasErrors);
                activity?.SetTag("deployment.actions_count", actions.Count);
                
                _deploymentDurationHistogram.Record(stopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object>("deployment.site_option", requestConfig.DeploymentSiteOption.ToString()),
                    new KeyValuePair<string, object>("deployment.preview_only", isPreviewOnly),
                    new KeyValuePair<string, object>("deployment.has_errors", hasErrors));
                
                if (hasErrors)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Deployment completed with errors");
                    _log?.Warning("Certificate deployment completed with errors for {ManagedCertName}", managedCertificate.Name);
                }
                else
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _log?.Information("Certificate deployment completed successfully for {ManagedCertName} in {ElapsedMs}ms", 
                        managedCertificate.Name, stopwatch.ElapsedMilliseconds);
                }

                // deployment tasks completed
                return actions;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                
                _bindingsFailedCounter.Add(1, 
                    new KeyValuePair<string, object>("error", ex.GetType().Name),
                    new KeyValuePair<string, object>("operation", "StoreAndDeploy"));
                
                _log?.Error(ex, "Fatal error during certificate deployment for {ManagedCertName}", managedCertificate.Name);
                
                throw;
            }
            finally
            {
                System.Threading.Interlocked.Decrement(ref _activeDeployments);
            }
        }

        /// <summary>
        /// Deploy the certificate to all target bindings
        /// </summary>
        private async Task<List<ActionStep>> DeployToAllTargetBindings(IBindingDeploymentTarget deploymentTarget,
                                                                    ManagedCertificate managedCertificate,
                                                                    CertRequestConfig requestConfig,
                                                                    string certStoreName,
                                                                    byte[] certHash,
                                                                    List<string> dnsHosts,
                                                                    bool isPreviewOnly = false
                                                                )
        {
            using var activity = _activitySource.StartActivity("DeployToAllTargetBindings", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            activity?.SetTag("deployment.dns_hosts_count", dnsHosts.Count);
            activity?.SetTag("deployment.site_option", requestConfig.DeploymentSiteOption.ToString());
            activity?.SetTag("deployment.binding_option", requestConfig.DeploymentBindingOption.ToString());
            
            _log?.Debug("Deploying to target bindings, DNS hosts: {DnsHostCount}, Site option: {SiteOption}", 
                dnsHosts.Count, requestConfig.DeploymentSiteOption);

            var actions = new List<ActionStep>();
            var targetSites = new List<IBindingDeploymentTargetItem>();

            // ensure defaults applied for deployment mode
            requestConfig?.ApplyDeploymentOptionDefaults();

            // if single site, add that
            if (requestConfig.DeploymentSiteOption == DeploymentOption.SingleSite)
            {
                if (!string.IsNullOrEmpty(managedCertificate.ServerSiteId))
                {
                    var site = await deploymentTarget.GetTargetItem(managedCertificate.ServerSiteId);
                    if (site != null)
                    {
                        targetSites.Add(site);
                        _log?.Debug("Single site deployment target: {SiteName} ({SiteId})", site.Name, site.Id);
                    }
                    else
                    {
                        _log?.Warning("Could not find target site {SiteId}", managedCertificate.ServerSiteId);
                        activity?.AddEvent(new ActivityEvent("SiteNotFound", 
                            tags: new ActivityTagsCollection { { "site.id", managedCertificate.ServerSiteId } }));
                    }
                }
            }

            // or add all sites (if required)
            if (requestConfig.DeploymentSiteOption == DeploymentOption.AllSites || requestConfig.DeploymentSiteOption == DeploymentOption.Auto)
            {
                var allSites = await deploymentTarget.GetAllTargetItems();
                targetSites.AddRange(allSites);
                _log?.Debug("Multiple site deployment, target count: {SiteCount}", allSites.Count());
            }

            activity?.SetTag("deployment.target_sites_count", targetSites.Count);
            _sitesProcessedHistogram.Record(targetSites.Count,
                new KeyValuePair<string, object>("deployment.site_option", requestConfig.DeploymentSiteOption.ToString()));

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

            activity?.SetTag("deployment.total_bindings_count", allBindings?.Count ?? 0);
            _log?.Debug("Retrieved {BindingCount} total bindings across {SiteCount} sites", 
                allBindings?.Count ?? 0, targetSites.Count);

            // for each sites we want to target, identify the bindings to add/update as required
            var siteIndex = 0;
            foreach (var site in targetSites)
            {
                siteIndex++;
                using var siteActivity = _activitySource.StartActivity("ProcessSite", ActivityKind.Internal);
                
                try
                {
                    siteActivity?.SetTag("site.id", site.Id);
                    siteActivity?.SetTag("site.name", site.Name);
                    siteActivity?.SetTag("site.index", siteIndex);
                    
                    _log?.Debug("Processing site {SiteIndex}/{TotalSites}: {SiteName} ({SiteId})", 
                        siteIndex, targetSites.Count, site.Name, site.Id);

                    //var existingBindings = await deploymentTarget.GetBindings(site.Id);
                    var existingBindings = allBindings?.Where(b => b.SiteId == site.Id)?.ToList() ?? new List<BindingInfo>();

                    var existingHttps = existingBindings.Where(e => e.Protocol == "https").ToList();

                    //remove https bindings which already have an https equivalent (specific hostname or blank)
                    existingBindings.RemoveAll(b => existingHttps.Any(e => e.Host == b.Host) && b.Protocol == "http");

                    existingBindings = existingBindings.OrderBy(b => b.Protocol).ThenBy(b => b.Host).ToList();

                    siteActivity?.SetTag("site.bindings_count", existingBindings.Count);
                    siteActivity?.SetTag("site.https_bindings_count", existingHttps.Count);

                    // copy existing bindings so we can add/remove
                    var updatedBindings = existingBindings.ToList();
                    var category = "Binding Details";

                    var bindingsProcessed = 0;
                    var bindingsUpdated = 0;
                    var bindingsCreated = 0;

                    // for each binding create or update an https binding
                    foreach (var b in existingBindings)
                    {
                        bindingsProcessed++;
                        var updateBinding = false;
                        var bindingExplanationSteps = new List<ActionStep>();

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
                                    bindingExplanationSteps.Add(new ActionStep(category, "Thumbprint Match", "Certificate thumbprint on existing binding matches previous certificate."));
                                }
                                else if (string.Equals(b.CertificateHash, managedCertificate.CertificateThumbprintHash))
                                {
                                    updateBinding = true;
                                    bindingExplanationSteps.Add(new ActionStep(category, "Thumbprint Match", "Certificate thumbprint on existing binding matches current certificate."));
                                }
                            }
                        }

                        if (updateBinding == false)
                        {
                            if (nonSpecificHostnames.Contains(hostname) && requestConfig.DeploymentBindingBlankHostname)
                            {
                                updateBinding = true;
                                bindingExplanationSteps.Add(new ActionStep(category, "Blank Hostname", "Binding hostname is blank and deployment to blank hostname is enabled."));
                            }
                            else if (requestConfig.DeploymentBindingMatchHostname && ManagedCertificate.IsDomainOrWildcardMatch(dnsHosts, hostname))
                            {
                                updateBinding = true;
                                bindingExplanationSteps.Add(new ActionStep(category, "Hostname Match", "Binding hostname matches domain or wildcard."));
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

                            var isHostnameSpecified = !nonSpecificHostnames.Contains(hostname) && !string.IsNullOrWhiteSpace(hostname);

                            // use sni by default if hostname is specified
                            var useSNI = isHostnameSpecified;

                            var specificIPWarning = "Specific IPs in bindings can cause binding conflicts and should only be used with extreme caution. Generally bindings should have IP set to All Unassigned, Hostname set and SNI enabled.";

                            if (b.Protocol == "https" || b.Protocol == "ftp")
                            {
                                // Updating an existing binding
                                bindingExplanationSteps.Add(new ActionStep(category, "Update Binding", "The existing binding will be updated with new settings."));
                                bindingsUpdated++;

                                // re-use existing port and IP choices, if appropriate.
                                if (b.Port > 0 && b.Port != sslPort)
                                {
                                    sslPort = b.Port;
                                    bindingExplanationSteps.Add(new ActionStep(category, "Binding Port", "Existing binding uses non-default port so that will be re-used in the updated binding."));
                                }

                                // carry over the custom/manually set target IP address for the updated binding if the IP is not in the "unassigned" set
                                // Here we are assuming that the user has intentionally specified the IP.
                                if (!unassignedIPs.Contains(b.IP))
                                {
                                    targetIPAddress = b.IP;
                                    var step = new ActionStep(category, "Specific IP", $"Existing binding uses a specific IP, so that will be re-used in the updated binding. {specificIPWarning}");
                                    step.HasWarning = true;
                                    bindingExplanationSteps.Add(step);
                                }

                                if (!useSNI && b.IsSNIEnabled)
                                {
                                    // if sni not enabled/requested but source binding being updated actually uses it, use it again.   
                                    useSNI = true;

                                    var step = new ActionStep(category, "SNI Enabled", "SNI was enabled only because the existing binding uses SNI.");
                                    step.HasWarning = true;
                                    bindingExplanationSteps.Add(step);

                                }
                                else if (useSNI && !b.IsSNIEnabled && b.Protocol == "https")
                                {
                                    // if sni enabled/requested but source binding being updated doesn't use it, disable it
                                    useSNI = false;

                                    var step = new ActionStep(category, "SNI Disabled", "SNI would normally be used but was disabled because the existing binding has SNI disabled. Use non-SNI bindings with caution.");
                                    step.HasWarning = true;
                                    bindingExplanationSteps.Add(step);
                                }
                            }
                            else
                            {
                                // Add a new https binding, based on a source http binding
                                bindingExplanationSteps.Add(new ActionStep(category, "Add Binding", "A new binding will be created based on the existing binding hostname."));
                                bindingsCreated++;

                                // use sni if requested or if not specified then use sni if we have a hostname
                                useSNI = requestConfig.BindingUseSNI ?? isHostnameSpecified;

                                if (useSNI)
                                {
                                    var step = new ActionStep(category, "SNI Enabled", "SNI will be used by default.");
                                    bindingExplanationSteps.Add(step);
                                }

                                // if a specific binding port is requested, use that
                                if (!string.IsNullOrWhiteSpace(requestConfig.BindingPort))
                                {
                                    sslPort = int.Parse(requestConfig.BindingPort);

                                    if (sslPort != 443)
                                    {
                                        var step = new ActionStep(category, "Binding Port", $"A non-standard http port has been requested ({sslPort}) .");
                                        bindingExplanationSteps.Add(step);
                                    }
                                }

                                // if a specific IP binding is requested only allow it if no hostname specified or SNI not enabled, otherwise targetIPAddress is * (All Unassigned)
                                // in general IP specific bindings are not required when SNI is available and hostname is known and using specific IPs can eventually lead to binding conflicts if a non-SNI binding gets created on the same IP.

                                if (!string.IsNullOrWhiteSpace(requestConfig.BindingIPAddress) && !unassignedIPs.Contains(requestConfig.BindingIPAddress))
                                {
                                    // if a custom target IP has been request, only permit it if hostname not specified or SNI specifically disabled.
                                    if (!useSNI || (string.IsNullOrWhiteSpace(hostname) || nonSpecificHostnames.Contains(hostname)))
                                    {
                                        // no hostname specified, allow a target IP address
                                        targetIPAddress = requestConfig.BindingIPAddress;

                                        var step = new ActionStep(category, "Specific IP", $"A specific binding IP {requestConfig.BindingIPAddress} has been requested. {specificIPWarning}");
                                        step.HasWarning = true;
                                        bindingExplanationSteps.Add(step);
                                    }
                                    else
                                    {
                                        targetIPAddress = "*";
                                        var step = new ActionStep(category, "Non-specific IP", $"Binding IP will be set to All Unassigned (recommended default).");
                                        bindingExplanationSteps.Add(step);
                                    }
                                }
                                else if (nonSpecificHostnames.Contains(hostname) && !unassignedIPs.Contains(b.IP) && requestConfig.DeploymentBindingBlankHostname)
                                {
                                    // SNI cannot be used because there is no hostname but the original http binding was IP specific
                                    targetIPAddress = b.IP;

                                    var step = new ActionStep(category, "Specific IP", $"A specific binding IP {requestConfig.BindingIPAddress} will be used because the original http binding has a specific IP and no hostname has been set on the binding. {specificIPWarning}");
                                    step.HasWarning = true;
                                    bindingExplanationSteps.Add(step);
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
                                   useSNI: useSNI,
                                   ipAddress: targetIPAddress,
                                   alwaysRecreateBindings: requestConfig.AlwaysRecreateBindings,
                                   isPreviewOnly: isPreviewOnly
                               );

                                stepActions.First().Substeps = bindingExplanationSteps;

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

                                stepActions.First().Substeps = bindingExplanationSteps;

                                actions.AddRange(stepActions);
                            }
                        }
                    }

                    siteActivity?.SetTag("site.bindings_processed", bindingsProcessed);
                    siteActivity?.SetTag("site.bindings_updated", bindingsUpdated);
                    siteActivity?.SetTag("site.bindings_created", bindingsCreated);
                    siteActivity?.SetStatus(ActivityStatusCode.Ok);
                    
                    _log?.Debug("Completed processing site {SiteName}: {ProcessedCount} bindings processed, {UpdatedCount} updated, {CreatedCount} created", 
                        site.Name, bindingsProcessed, bindingsUpdated, bindingsCreated);
                }
                catch (Exception exp)
                {
                    siteActivity?.SetStatus(ActivityStatusCode.Error, exp.Message);
                    siteActivity?.AddException(exp);
                    
                    var errorStep = new ActionStep 
                    { 
                        Title = $"Failed to deploy to site: {site.Name}", 
                        Category = "Deploy.AddOrUpdateBindings", 
                        HasError = true, 
                        Description = $"Site: {site.Name} ({site.Id}) - {exp.Message}",
                        Key = $"error_{site.Id}"
                    };
                    
                    actions.Add(errorStep);
                    
                    _bindingsFailedCounter.Add(1, 
                        new KeyValuePair<string, object>("error", exp.GetType().Name),
                        new KeyValuePair<string, object>("site.id", site.Id),
                        new KeyValuePair<string, object>("operation", "DeployToSite"));
                    
                    _log?.Error(exp, "Error deploying bindings to site {SiteName} ({SiteId})", site.Name, site.Id);
                }
            }

            stopwatch.Stop();
            activity?.SetTag("deployment.duration_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("deployment.actions_count", actions.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return actions;
        }

        /// <summary>
        /// Check if a binding already exists for the given specification
        /// </summary>
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
        /// Test if two bindings are equivalent
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool AreBindingsEquivalent(BindingInfo a, BindingInfo b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            if (
                (a.IP == b.IP || (unassignedIPs.Contains(a.IP) && unassignedIPs.Contains(b.IP))) &&
                (a.Host == b.Host || (nonSpecificHostnames.Contains(a.Host) && nonSpecificHostnames.Contains(b.Host))) &&
                a.Port == b.Port &&
                a.IsSNIEnabled == b.IsSNIEnabled &&
                a.Protocol == b.Protocol
                )
            {
                return true;
            }
            else
            {
                return false;
            }
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
            using var activity = _activitySource.StartActivity("UpdateWebBinding", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            var steps = new List<ActionStep>();

            var internationalHost = host ?? "";

            if (unassignedIPs.Contains(ipAddress))
            {
                ipAddress = "*";
            }

            // can't use SNI is hostname is blank or wildcard
            if (useSNI && nonSpecificHostnames.Contains(internationalHost))
            {
                useSNI = false;
            }

            var bindingSpecString = $"{ipAddress}:{sslPort}:{internationalHost}";
            
            activity?.SetTag("binding.host", internationalHost);
            activity?.SetTag("binding.port", sslPort);
            activity?.SetTag("binding.ip", ipAddress);
            activity?.SetTag("binding.sni_enabled", useSNI);
            activity?.SetTag("binding.preview_only", isPreviewOnly);
            activity?.SetTag("site.id", site.Id);
            activity?.SetTag("site.name", site.Name);

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

            var isNewBinding = !HasExistingBinding(existingBindings, bindingSpec);
            activity?.SetTag("binding.is_new", isNewBinding);

            if (isNewBinding)
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
                        
                        activity?.SetStatus(ActivityStatusCode.Error, result.Description);
                        _bindingsFailedCounter.Add(1, 
                            new KeyValuePair<string, object>("operation", "add"),
                            new KeyValuePair<string, object>("protocol", "https"),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        _log?.Error("Failed to add web binding {BindingSpec} to site {SiteName}: {Error}", 
                            bindingSpecString, site.Name, result.Description);
                    }
                    else
                    {
                        if (result.HasWarning)
                        {
                            action.HasWarning = true;
                            action.Description += $" [{result.Description}]";
                            _log?.Warning("Web binding added with warning {BindingSpec} to site {SiteName}: {Warning}", 
                                bindingSpecString, site.Name, result.Description);
                        }
                        else
                        {
                            _log?.Information("Successfully added web binding {BindingSpec} to site {SiteName}", 
                                bindingSpecString, site.Name);
                        }
                        
                        _bindingsCreatedCounter.Add(1, 
                            new KeyValuePair<string, object>("protocol", "https"),
                            new KeyValuePair<string, object>("sni_enabled", useSNI),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        activity?.SetStatus(ActivityStatusCode.Ok);
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
                        
                        activity?.SetStatus(ActivityStatusCode.Error, "Site ID not determined");
                        _log?.Error("Cannot add binding - site ID could not be determined for {BindingSpec}", bindingSpecString);
                    }
                }

                action.ObjectResult = bindingSpec;

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
                        
                        activity?.SetStatus(ActivityStatusCode.Error, result.Description);
                        _bindingsFailedCounter.Add(1, 
                            new KeyValuePair<string, object>("operation", "update"),
                            new KeyValuePair<string, object>("protocol", "https"),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        _log?.Error("Failed to update web binding {BindingSpec} on site {SiteName}: {Error}", 
                            bindingSpecString, site.Name, result.Description);
                    }
                    else
                    {
                        if (result.HasWarning)
                        {
                            // has update warning
                            action.HasWarning = true;
                            action.Description += $" [{result.Description}]";
                            _log?.Warning("Web binding updated with warning {BindingSpec} on site {SiteName}: {Warning}", 
                                bindingSpecString, site.Name, result.Description);
                        }
                        else
                        {
                            _log?.Information("Successfully updated web binding {BindingSpec} on site {SiteName}", 
                                bindingSpecString, site.Name);
                        }
                        
                        _bindingsUpdatedCounter.Add(1, 
                            new KeyValuePair<string, object>("protocol", "https"),
                            new KeyValuePair<string, object>("sni_enabled", useSNI),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        activity?.SetStatus(ActivityStatusCode.Ok);
                    }
                }

                action.ObjectResult = bindingSpec;

                steps.Add(action);
            }

            stopwatch.Stop();
            _bindingOperationDurationHistogram.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object>("operation", isNewBinding ? "add" : "update"),
                new KeyValuePair<string, object>("protocol", "https"),
                new KeyValuePair<string, object>("preview_only", isPreviewOnly));

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
            using var activity = _activitySource.StartActivity("UpdateFtpBinding", ActivityKind.Internal);
            var stopwatch = Stopwatch.StartNew();
            
            var steps = new List<ActionStep>();

            var internationalHost = host ?? "";

            var bindingSpecString = $"{ipAddress}:{port}:{internationalHost}";
            
            activity?.SetTag("binding.host", internationalHost);
            activity?.SetTag("binding.port", port);
            activity?.SetTag("binding.ip", ipAddress);
            activity?.SetTag("binding.preview_only", isPreviewOnly);
            activity?.SetTag("site.id", site.Id);
            activity?.SetTag("site.name", site.Name);

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

            var isNewBinding = !HasExistingBinding(existingBindings, bindingSpec);
            activity?.SetTag("binding.is_new", isNewBinding);

            if (isNewBinding)
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
                        
                        activity?.SetStatus(ActivityStatusCode.Error, result.Description);
                        _bindingsFailedCounter.Add(1, 
                            new KeyValuePair<string, object>("operation", "add"),
                            new KeyValuePair<string, object>("protocol", "ftp"),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        _log?.Error("Failed to add FTP binding {BindingSpec} to site {SiteName}: {Error}", 
                            bindingSpecString, site.Name, result.Description);
                    }
                    else
                    {
                        if (result.HasWarning)
                        {
                            action.HasWarning = true;
                            action.Description += $" [{result.Description}]";
                            _log?.Warning("FTP binding added with warning {BindingSpec} to site {SiteName}: {Warning}", 
                                bindingSpecString, site.Name, result.Description);
                        }
                        else
                        {
                            _log?.Information("Successfully added FTP binding {BindingSpec} to site {SiteName}", 
                                bindingSpecString, site.Name);
                        }
                        
                        _bindingsCreatedCounter.Add(1, 
                            new KeyValuePair<string, object>("protocol", "ftp"),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        activity?.SetStatus(ActivityStatusCode.Ok);
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
                        
                        activity?.SetStatus(ActivityStatusCode.Error, result.Description);
                        _bindingsFailedCounter.Add(1, 
                            new KeyValuePair<string, object>("operation", "update"),
                            new KeyValuePair<string, object>("protocol", "ftp"),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        _log?.Error("Failed to update FTP binding {BindingSpec} on site {SiteName}: {Error}", 
                            bindingSpecString, site.Name, result.Description);
                    }
                    else
                    {
                        if (result.HasWarning)
                        {
                            // has update warning
                            action.HasWarning = true;
                            action.Description += $" [{result.Description}]";
                            _log?.Warning("FTP binding updated with warning {BindingSpec} on site {SiteName}: {Warning}", 
                                bindingSpecString, site.Name, result.Description);
                        }
                        else
                        {
                            _log?.Information("Successfully updated FTP binding {BindingSpec} on site {SiteName}", 
                                bindingSpecString, site.Name);
                        }
                        
                        _bindingsUpdatedCounter.Add(1, 
                            new KeyValuePair<string, object>("protocol", "ftp"),
                            new KeyValuePair<string, object>("site.id", site.Id));
                        
                        activity?.SetStatus(ActivityStatusCode.Ok);
                    }
                }

                steps.Add(action);
            }

            stopwatch.Stop();
            _bindingOperationDurationHistogram.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object>("operation", isNewBinding ? "add" : "update"),
                new KeyValuePair<string, object>("protocol", "ftp"),
                new KeyValuePair<string, object>("preview_only", isPreviewOnly));

            return steps;
        }
    }
}
