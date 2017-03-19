using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CertifyManager
    {
        private ItemManager siteManager = null;

        public CertifyManager()
        {
            siteManager = new ItemManager();
            siteManager.LoadSettings();
        }

        /// <summary>
        /// Check if we have one or more managed sites setup
        /// </summary>
        public bool HasManagedSites
        {
            get
            {
                if (siteManager.GetManagedSites().Count > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public List<ManagedSite> GetManagedSites()
        {
            return this.siteManager.GetManagedSites();
        }

        public void SetManagedSites(List<ManagedSite> managedSites)
        {
            this.siteManager.UpdatedManagedSites(managedSites);
        }

        public void SaveManagedSites(List<ManagedSite> managedSites)
        {
            this.siteManager.UpdatedManagedSites(managedSites);
            this.siteManager.StoreSettings();
        }

        /// <summary>
        /// Test dummy method for async UI testing etc
        /// </summary>
        /// <param name="vaultManager"></param>
        /// <param name="managedSite"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(VaultManager vaultManager, ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            return await Task<CertificateRequestResult>.Run<CertificateRequestResult>(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    if (progress != null) progress.Report(new RequestProgressState { IsRunning = true, CurrentState = RequestState.InProgress, Message = "Step " + i });
                    System.Threading.Thread.Sleep(60);
                }

                return new CertificateRequestResult { };
            });
        }

        public async Task<CertificateRequestResult> PerformCertificateRequest(VaultManager vaultManager, ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            //return await Task.Run(async () =>
            //{
            if (vaultManager == null)
            {
                vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);
            }
            //primary domain and each subject alternative name must now be registered as an identifier with LE and validated

            if (progress != null) progress.Report(new RequestProgressState { IsRunning = true, CurrentState = RequestState.InProgress, Message = "Registering Domain Identifiers" });

            await Task.Delay(200); //allow UI update

            var config = managedSite.RequestConfig;

            List<string> allDomains = new List<string>
            {
                config.PrimaryDomain
            };

            if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);

            bool allIdentifiersValidated = true;

            if (config.ChallengeType == null) config.ChallengeType = "http-01";

            List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();

            foreach (var domain in allDomains)
            {
                var identifierAlias = vaultManager.ComputeIdentifierAlias(domain);

                //check if this domain already has an associated identifier registerd with LetsEncrypt which hasn't expired yet
                await Task.Delay(200); //allow UI update

                var existingIdentifier = vaultManager.GetIdentifier(domain.Trim().ToLower());
                bool identifierAlreadyValid = false;
                if (existingIdentifier != null
                    && existingIdentifier.Authorization != null
                    && (existingIdentifier.Authorization.Status == "valid" || existingIdentifier.Authorization.Status == "pending")
                    && existingIdentifier.Authorization.Expires > DateTime.Now.AddDays(1))
                {
                    //we have an existing validated identifier, reuse that for this certificate request
                    identifierAlias = existingIdentifier.Alias;

                    if (existingIdentifier.Authorization.Status == "valid")
                    {
                        identifierAlreadyValid = true;
                    }

                    // managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted, Message = "Attempting Certificate Request: " + managedSite.SiteType });
                    System.Diagnostics.Debug.WriteLine("Reusing existing valid non-expired identifier for the domain " + domain);
                }

                ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted, Message = "Attempting Certificate Request: " + managedSite.ItemType });

                //begin authorization process (register identifier, request authorization if not already given)
                if (progress != null) progress.Report(new RequestProgressState { Message = "Registering and Validating " + domain });

                //TODO: make operations async and yeild IO of vault
                /*var authorization = await Task.Run(() =>
                {
                    return vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);
                });*/

                var authorization = vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);

                if (authorization != null && !identifierAlreadyValid)
                {
                    if (authorization.Identifier.Authorization.IsPending())
                    {
                        if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS)
                        {
                            if (progress != null) progress.Report(new RequestProgressState { Message = "Performing Challenge Response via IIS: " + domain });

                            //ask LE to check our answer to their authorization challenge (http), LE will then attempt to fetch our answer, if all accessible and correct (authorized) LE will then allow us to request a certificate
                            //prepare IIS with answer for the LE challenege
                            authorization = vaultManager.PerformIISAutomatedChallengeResponse(config, authorization);

                            //if we attempted extensionless config checks, report any errors
                            if (config.PerformAutoConfig && !authorization.ExtensionlessConfigCheckedOK)
                            {
                                ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (" + managedSite.ItemType + ")" });
                                siteManager.StoreSettings();

                                var result = new CertificateRequestResult { IsSuccess = false, Message = "Automated configuration checks failed. Authorizations will not be able to complete.\nCheck you have http bindings for your site and ensure you can browse to http://" + domain + "/.well-known/acme-challenge/configcheck before proceeding." };
                                if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Error, Message = result.Message, Result = result });

                                return result;
                            }
                            else
                            {
                                if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.InProgress, Message = "Requesting Validation from Lets Encrypt: " + domain });

                                //ask LE to validate our challenge response
                                vaultManager.SubmitChallenge(identifierAlias, config.ChallengeType);

                                bool identifierValidated = vaultManager.CompleteIdentifierValidationProcess(authorization.Identifier.Alias);

                                if (!identifierValidated)
                                {
                                    if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Error, Message = "Domain validation failed: " + domain });

                                    allIdentifiersValidated = false;
                                }
                                else
                                {
                                    if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.InProgress, Message = "Domain validation completed: " + domain });

                                    identifierAuthorizations.Add(authorization);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (identifierAlreadyValid)
                    {
                        //we have previously validated this identifier and it has not yet expired, so we can just reuse it in our cert request
                        identifierAuthorizations.Add(new PendingAuthorization { Identifier = existingIdentifier });
                    }
                }
            }

            if (allIdentifiersValidated)
            {
                string primaryDnsIdentifier = identifierAuthorizations.First().Identifier.Alias;
                string[] alternativeDnsIdentifiers = identifierAuthorizations.Where(i => i.Identifier.Alias != primaryDnsIdentifier).Select(i => i.Identifier.Alias).ToArray();

                if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.InProgress, Message = "Requesting Certificate via Lets Encrypt" });
                await Task.Delay(200); //allow UI update

                var certRequestResult = vaultManager.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);
                if (certRequestResult.IsSuccess)
                {
                    if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = "Completed Certificate Request." });

                    string pfxPath = certRequestResult.Result.ToString();

                    if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS && config.PerformAutomatedCertBinding)
                    {
                        if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.InProgress, Message = "Performing Automated Certificate Binding" });
                        await Task.Delay(200); //allow UI update

                        var iisManager = new IISManager();

                        //Install certificate into certificate store and bind to IIS site
                        if (iisManager.InstallCertForDomain(config.PrimaryDomain, pfxPath, cleanupCertStore: true, skipBindings: !config.PerformAutomatedCertBinding))
                        {
                            //all done
                            ManagedSiteLog.AppendLog(managedSite.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestSuccessful, Message = "Completed certificate request and automated bindings update (IIS)" });
                            siteManager.StoreSettings();

                            var result = new CertificateRequestResult { IsSuccess = true, Message = "Certificate installed and SSL bindings updated for " + config.PrimaryDomain };
                            if (progress != null) progress.Report(new RequestProgressState { IsRunning = false, CurrentState = RequestState.Success, Message = result.Message });

                            return result;
                        }
                        else
                        {
                            return new CertificateRequestResult { IsSuccess = false, Message = "An error occurred installing the certificate. Certificate file may not be valid: " + pfxPath };
                        }
                    }
                    else
                    {
                        return new CertificateRequestResult { IsSuccess = true, Message = "Certificate created ready for manual binding: " + pfxPath };
                    }
                }
                else
                {
                    return new CertificateRequestResult { IsSuccess = false, Message = "The Let's Encrypt service did not issue a valid certificate in the time allowed. " + (certRequestResult.ErrorMessage ?? "") };
                }
            }
            else
            {
                return new CertificateRequestResult { IsSuccess = false, Message = "Validation of the required challenges did not complete successfully. Please ensure all domains to be referenced in the Certificate can be used to access this site without redirection. " };
            }
            //});
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            await Task.Delay(200); //allow UI to update
            //currently the vault won't let us run parallel requests due to file locks
            bool performRequestsInParallel = false;

            siteManager.LoadSettings();

            //var vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);

            IEnumerable<ManagedSite> sites = siteManager.GetManagedSites();

            if (autoRenewalOnly)
            {
                sites = sites.Where(s => s.IncludeInAutoRenew == true);
            }

            var renewalTasks = new List<Task<CertificateRequestResult>>();
            foreach (var s in sites.Where(s => s.IncludeInAutoRenew == true))
            {
                //get matching progress tracker for this site
                IProgress<RequestProgressState> tracker = null;
                if (progressTrackers != null)
                {
                    tracker = progressTrackers[s.Id];
                }
                renewalTasks.Add(this.PerformCertificateRequest(null, s, tracker));
            }

            if (performRequestsInParallel)
            {
                var results = await Task.WhenAll(renewalTasks);

                //siteManager.StoreSettings();
                return results.ToList();
            }
            else
            {
                var results = new List<CertificateRequestResult>();
                foreach (var t in renewalTasks)
                {
                    results.Add(await t);
                }

                return results;
            }
        }
    }
}