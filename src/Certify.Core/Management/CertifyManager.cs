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
        private SiteManager siteManager = null;

        public CertifyManager()
        {
            siteManager = new SiteManager();
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

        public async Task<CertificateRequestResult> PerformCertificateRequest(VaultManager vaultManager, ManagedSite managedSite)
        {
            if (vaultManager == null)
            {
                vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);
            }
            //primary domain and each subject alternative name must now be registered as an identifier with LE and validated

            var config = managedSite.RequestConfig;

            List<string> allDomains = new List<string>();
            allDomains.Add(config.PrimaryDomain);
            if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);
            bool allIdentifiersValidated = true;

            if (config.ChallengeType == null) config.ChallengeType = "http-01";

            List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();

            foreach (var domain in allDomains)
            {
                var identifierAlias = vaultManager.ComputeIdentifierAlias(domain);

                //check if this domain already has an associated identifier registerd with LetsEncrypt which hasn't expired yet

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

                managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted, Message = "Attempting Certificate Request: " + managedSite.SiteType });

                //begin authorization process (register identifier, request authorization if not already given)
                var authorization = vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);

                if (authorization != null && !identifierAlreadyValid)
                {
                    if (authorization.Identifier.Authorization.IsPending())
                    {
                        if (managedSite.SiteType == ManagedSiteType.LocalIIS)
                        {
                            //ask LE to check our answer to their authorization challenge (http), LE will then attempt to fetch our answer, if all accessible and correct (authorized) LE will then allow us to request a certificate
                            //prepare IIS with answer for the LE challenege
                            authorization = vaultManager.PerformIISAutomatedChallengeResponse(config, authorization);

                            //if we attempted extensionless config checks, report any errors
                            if (config.PerformExtensionlessAutoConfig && !authorization.ExtensionlessConfigCheckedOK)
                            {
                                managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (" + managedSite.SiteType + ")" });
                                siteManager.StoreSettings();

                                return new CertificateRequestResult { IsSuccess = false, ErrorMessage = "Automated checks for extensionless content failed. Authorizations will not be able to complete.Change the web.config in <your site>\\.well-known\\acme-challenge and ensure you can browse to http://<your site>/.well-known/acme-challenge/configcheck before proceeding." };
                            }
                            else
                            {
                                //ask LE to validate our challenge response
                                vaultManager.SubmitChallenge(identifierAlias, config.ChallengeType);

                                bool identifierValidated = vaultManager.CompleteIdentifierValidationProcess(authorization.Identifier.Alias);

                                if (!identifierValidated)
                                {
                                    allIdentifiersValidated = false;
                                }
                                else
                                {
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

                var certRequestResult = vaultManager.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);
                if (certRequestResult.IsSuccess)
                {
                    string pfxPath = certRequestResult.Result.ToString();

                    if (managedSite.SiteType == ManagedSiteType.LocalIIS && config.PerformAutomatedCertBinding)
                    {
                        var iisManager = new IISManager();

                        //Install certificate into certificate store and bind to IIS site
                        if (iisManager.InstallCertForDomain(config.PrimaryDomain, pfxPath, cleanupCertStore: true, skipBindings: !config.PerformAutomatedCertBinding))
                        {
                            //all done
                            managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestSuccessful, Message = "Completed certificate request and automated bindings update (IIS)" });
                            siteManager.StoreSettings();

                            return new CertificateRequestResult { IsSuccess = true, ErrorMessage = "Certificate installed and SSL bindings updated for " + config.PrimaryDomain };
                        }
                        else
                        {
                            return new CertificateRequestResult { IsSuccess = false, ErrorMessage = "An error occurred installing the certificate. Certificate file may not be valid: " + pfxPath };
                        }
                    }
                    else
                    {
                        return new CertificateRequestResult { IsSuccess = true, ErrorMessage = "Certificate created ready for manual binding: " + pfxPath };
                    }
                }
                else
                {
                    return new CertificateRequestResult { IsSuccess = false, ErrorMessage = "The Let's Encrypt service did not issue a valid certificate in the time allowed. " + (certRequestResult.ErrorMessage != null ? certRequestResult.ErrorMessage : "") };
                }
            }
            else
            {
                return new CertificateRequestResult { IsSuccess = false, ErrorMessage = "Validation of the required challenges did not complete successfully. Please ensure all domains to be referenced in the Certificate can be used to access this site without redirection. " };
            }
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true)
        {
            siteManager.LoadSettings();

            var vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, ACMESharp.Vault.Providers.LocalDiskVault.VAULT);

            IEnumerable<ManagedSite> sites = siteManager.GetManagedSites();

            var results = new List<CertificateRequestResult>();

            if (autoRenewalOnly)
            {
                sites = sites.Where(s => s.IncludeInAutoRenew == true);
            }

            foreach (var s in sites.Where(s => s.IncludeInAutoRenew == true))
            {
                results.Add(await this.PerformCertificateRequest(vaultManager, s));
            }

            siteManager.StoreSettings();

            return results;
        }
    }
}