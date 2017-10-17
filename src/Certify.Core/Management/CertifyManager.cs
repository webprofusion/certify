using Certify.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CertifyManager
    {
        private ItemManager _siteManager = null;
        private IACMEClientProvider _acmeClientProvider = null;
        private IVaultProvider _vaultProvider = null;
        private IISManager _iisManager = null;

        private const string SCHEDULED_TASK_NAME = "Certify Maintenance Task";
        private const string SCHEDULED_TASK_EXE = "certify.exe";
        private const string SCHEDULED_TASK_ARGS = "renew";

        public CertifyManager()
        {
            Certify.Management.Util.SetSupportedTLSVersions();

            var acmeSharp = new Certify.Management.APIProviders.ACMESharpProvider();
            // ACME Sharp is both a vault (config storage) provider and ACME client provider
            _acmeClientProvider = acmeSharp;
            _vaultProvider = acmeSharp;

            _siteManager = new ItemManager();
            _siteManager.LoadSettings();

            _iisManager = new IISManager();
        }

        // expose IIS metadata
        public bool IsIISAvailable => _iisManager?.IsIISAvailable ?? false;

        public Version IISVersion => _iisManager.GetIisVersion();

        /// <summary>
        /// Check if we have one or more managed sites setup 
        /// </summary>
        public bool HasManagedSites
        {
            get
            {
                if (_siteManager.GetManagedSites().Count > 0)
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
            return this._siteManager.GetManagedSites();
        }

        public List<RegistrationItem> GetContactRegistrations()
        {
            return _vaultProvider.GetContactRegistrations();
        }

        public List<IdentifierItem> GeDomainIdentifiers()
        {
            return _vaultProvider.GetDomainIdentifiers();
        }

        public List<CertificateItem> GetCertificates()
        {
            return _vaultProvider.GetCertificates();
        }

        public void SetManagedSites(List<ManagedSite> managedSites)
        {
            this._siteManager.UpdatedManagedSites(managedSites);
        }

        public void SaveManagedSites(List<ManagedSite> managedSites)
        {
            this._siteManager.UpdatedManagedSites(managedSites);
            this._siteManager.StoreSettings();
        }

        public bool HasRegisteredContacts()
        {
            return _vaultProvider.HasRegisteredContacts();
        }

        public async Task<APIResult> TestChallenge(ManagedSite managedSite)
        {
            return await _vaultProvider.TestChallengeResponse(_iisManager, managedSite);
        }

        public async Task<APIResult> RevokeCertificate(ManagedSite managedSite)
        {
            var result = await _vaultProvider.RevokeCertificate(managedSite);
            if (result.IsOK)
            {
                managedSite.CertificateRevoked = true;
            }
            return result;
        }

        /// <summary>
        /// Test dummy method for async UI testing etc 
        /// </summary>
        /// <param name="vaultManager"></param>
        /// <param name="managedSite"></param>
        /// <param name="progress"></param>
        /// <returns></returns>
        public async Task<CertificateRequestResult> PerformDummyCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            return await Task<CertificateRequestResult>.Run<CertificateRequestResult>(() =>
            {
                for (var i = 0; i < 6; i++)
                {
                    if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Running, Message = "Step " + i });

                    var time = new Random().Next(2000);
                    System.Threading.Thread.Sleep(time);
                }
                if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = "Finish" });
                System.Threading.Thread.Sleep(500);
                return new CertificateRequestResult { };
            });
        }

        public void DeleteManagedSite(string id)
        {
            var site = _siteManager.GetManagedSite(id);
            if (site != null)
            {
                this._siteManager.DeleteManagedSite(site);
            }
        }

        public bool AddRegisteredContact(ContactRegistration reg)
        {
            return _acmeClientProvider.AddNewRegistrationAndAcceptTOS(reg.EmailAddress);
        }

        /// <summary>
        /// Remove other contacts which don't match the email address given 
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public void RemoveExtraContacts(string email)
        {
            var regList = _vaultProvider.GetContactRegistrations();
            foreach (var reg in regList)
            {
                if (!reg.Contacts.Contains("mailto:" + email))
                {
                    _vaultProvider.DeleteContactRegistration(reg.Id);
                }
            }
        }

        public List<SiteBindingItem> GetPrimaryWebSites(bool ignoreStoppedSites)
        {
            return _iisManager.GetPrimarySites(ignoreStoppedSites);
        }

        public string GetAcmeSummary()
        {
            return _acmeClientProvider.GetAcmeBaseURI();
        }

        public string GetVaultSummary()
        {
            return _vaultProvider.GetVaultSummary();
        }

        private void ReportProgress(IProgress<RequestProgressState> progress, string msg, string logManagedSiteId = null)
        {
            if (progress != null) progress.Report(new RequestProgressState { Message = msg });

            if (logManagedSiteId != null)
            {
                LogMessage(logManagedSiteId, msg, LogItemType.GeneralWarning);
            }
        }

        private void ReportProgress(IProgress<RequestProgressState> progress, RequestProgressState state, string logManagedSiteId = null)
        {
            if (progress != null) progress.Report(state);

            if (logManagedSiteId != null)
            {
                LogMessage(logManagedSiteId, state.Message, LogItemType.GeneralWarning);
            }
        }

        private void LogMessage(string managedSiteId, List<string> msgs, LogItemType logType = LogItemType.GeneralInfo)
        {
            foreach (var msg in msgs)
            {
                if (msg != null)
                {
                    LogMessage(managedSiteId, msg, logType);
                }
            }
        }

        private void LogMessage(string managedSiteId, string msg, LogItemType logType = LogItemType.GeneralInfo)
        {
            ManagedSiteLog.AppendLog(managedSiteId, new ManagedSiteLogItem
            {
                EventDate = DateTime.UtcNow,
                LogItemType = LogItemType.GeneralInfo,
                Message = msg
            });
        }

        public async Task<CertificateRequestResult> PerformCertificateRequest(ManagedSite managedSite, IProgress<RequestProgressState> progress = null)
        {
            // FIXME: refactor into different concerns, there's way too much being done here
            if (managedSite.RequestConfig.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP && managedSite.RequestConfig.PerformExtensionlessConfigChecks)
            {
                ReportProgress(progress, new RequestProgressState { IsRunning = true, CurrentState = RequestState.Running, Message = "Performing Config Tests" });

                var testResult = await TestChallenge(managedSite);
                if (!testResult.IsOK)
                {
                    return new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = String.Join("; ", testResult.FailedItemSummary), Result = testResult.Result };
                }
            }

            return await Task.Run(async () =>
            {
                // start with a failure result, set to success when succeeding
                var result = new CertificateRequestResult { ManagedItem = managedSite, IsSuccess = false, Message = "" };

                var config = managedSite.RequestConfig;
                try
                {
                    // run pre-request script, if set
                    if (!string.IsNullOrEmpty(config.PreRequestPowerShellScript))
                    {
                        try
                        {
                            string scriptOutput = await PowerShellManager.RunScript(result, config.PreRequestPowerShellScript);
                            LogMessage(managedSite.Id, $"Pre-Request Script output: \n{scriptOutput}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(managedSite.Id, $"Pre-Request Script error:\n{ex.Message}");
                        }
                    }

                    // if the script has requested the certificate request to be aborted, skip the request
                    if (result.Abort)
                    {
                        LogMessage(managedSite.Id, $"Certificate Request Aborted: {managedSite.Name}");
                        result.Message = "Certificate Request was aborted by PS script";
                        goto CertRequestAborted;
                    }

                    LogMessage(managedSite.Id, $"Beginning Certificate Request Process: {managedSite.Name}");

                    //enable or disable EFS flag on private key certs based on preference
                    if (CoreAppSettings.Current.EnableEFS)
                    {
                        _vaultProvider.EnableSensitiveFileEncryption();
                    }

                    //primary domain and each subject alternative name must now be registered as an identifier with LE and validated
                    ReportProgress(progress, new RequestProgressState { IsRunning = true, CurrentState = RequestState.Running, Message = "Registering Domain Identifiers" });

                    await Task.Delay(200); //allow UI update, we should we using async calls instead

                    List<string> allDomains = new List<string> { config.PrimaryDomain };

                    if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);

                    // begin by assuming all identifiers are valid
                    bool allIdentifiersValidated = true;

                    if (config.ChallengeType == null) config.ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP;

                    List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();
                    var distinctDomains = allDomains.Distinct();

                    string failureSummaryMessage = null;

                    // perform validation process for each domain
                    foreach (var domain in distinctDomains)
                    {
                        //begin authorization process (register identifier, request authorization if not already given)
                        var domainIdentifierId = _vaultProvider.ComputeDomainIdentifierId(domain);

                        LogMessage(managedSite.Id, $"Attempting Domain Validation: {domain}", LogItemType.CertificateRequestStarted);
                        ReportProgress(progress, $"Registering and Validating {domain} ");

                        //TODO: make operations async and yield IO of vault
                        /*var authorization = await Task.Run(() =>
                        {
                            return vaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: config.ChallengeType, domain: domain);
                        });*/

                        // begin authorization by registering the domain identifier. This may return
                        // an already validated authorization or we may still have to complete the
                        // authorization challenge. When rate limits are encountered, this step may fail.
                        var authorization = _vaultProvider.BeginRegistrationAndValidation(config, domainIdentifierId, challengeType: config.ChallengeType, domain: domain);

                        if (authorization != null && authorization.Identifier != null)
                        {
                            // check if authorization is pending, it may already be valid if an
                            // existing authorization was reused
                            if (authorization.Identifier.IsAuthorizationPending)
                            {
                                if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS)
                                {
                                    ReportProgress(progress, $"Performing Challenge Response via IIS: {domain} ");

                                    // ask LE to check our answer to their authorization challenge
                                    // (http-01 or tls-sni-01), LE will then attempt to fetch our
                                    // answer, if all accessible and correct (authorized) LE will
                                    // then allow us to request a certificate
                                    authorization = _vaultProvider.PerformIISAutomatedChallengeResponse(_iisManager, managedSite, authorization);

                                    // pass authorization log items onto main log
                                    /*authorization.LogItems?.ForEach((msg) =>
                                    {
                                        if (msg != null) LogMessage(managedSite.Id, msg, LogItemType.GeneralInfo);
                                    });*/

                                    if ((config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP && config.PerformExtensionlessConfigChecks && !authorization.ExtensionlessConfigCheckedOK) ||
                                        (config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI && config.PerformTlsSniBindingConfigChecks && !authorization.TlsSniConfigCheckedOK))
                                    {
                                        //if we failed the config checks, report any errors
                                        LogMessage(managedSite.Id, $"Failed prerequisite configuration checks ({ managedSite.ItemType })", LogItemType.CertficateRequestFailed);

                                        _siteManager.StoreSettings();

                                        if (config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP)
                                        {
                                            result.Message = "Automated configuration checks failed. Authorizations will not be able to complete.\nCheck you have http bindings for your site and ensure you can browse to http://" + domain + "/.well-known/acme-challenge/configcheck before proceeding.";
                                        }

                                        if (config.ChallengeType == ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_SNI)
                                        {
                                            result.Message = "Automated configuration checks failed. Authorizations will not be able to complete.\nCheck you have https SNI bindings for your site\n(ex: '0123456789ABCDEF0123456789ABCDEF.0123456789ABCDEF0123456789ABCDEF.acme.invalid') before proceeding.";
                                        }

                                        ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Error, Message = result.Message, Result = result });

                                        break;
                                    }
                                    else
                                    {
                                        ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Running, Message = $"Requesting Validation from Let's Encrypt: {domain}" });
                                        try
                                        {
                                            //ask LE to validate our challenge response
                                            _vaultProvider.SubmitChallenge(domainIdentifierId, config.ChallengeType);

                                            bool identifierValidated = _vaultProvider.CompleteIdentifierValidationProcess(authorization.Identifier.Alias);

                                            if (!identifierValidated)
                                            {
                                                var identifierInfo = _vaultProvider.GetDomainIdentifier(domain);
                                                var errorMsg = identifierInfo?.ValidationError;
                                                var errorType = identifierInfo?.ValidationErrorType;

                                                failureSummaryMessage = $"Domain validation failed: {domain} \r\n{errorMsg}";
                                                ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Error, Message = failureSummaryMessage }, managedSite.Id);

                                                allIdentifiersValidated = false;
                                            }
                                            else
                                            {
                                                ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Running, Message = "Domain validation completed: " + domain }, managedSite.Id);

                                                identifierAuthorizations.Add(authorization);
                                            }
                                        }
                                        finally
                                        {
                                            // clean up challenge answers
                                            // (.well-known/acme-challenge/* files for http-01 or iis
                                            // bindings for tls-sni-01)
                                            authorization.Cleanup();
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // we already have a completed authorization, check it's valid
                                if (authorization.Identifier.Status == "valid")
                                {
                                    LogMessage(managedSite.Id, $"Domain already has current authorization, skipping verification: { domain }");

                                    identifierAuthorizations.Add(new PendingAuthorization { Identifier = authorization.Identifier });
                                }
                                else
                                {
                                    LogMessage(managedSite.Id, $"Domain authorization failed : { domain } ");

                                    allIdentifiersValidated = false;
                                }
                            }
                        }
                        else
                        {
                            // could not begin authorization
                            LogMessage(managedSite.Id, $"Could not begin authorization for domain with Let's Encrypt: { domain } ");

                            if (authorization != null && authorization.LogItems != null)
                            {
                                LogMessage(managedSite.Id, authorization.LogItems);
                            }
                            allIdentifiersValidated = false;
                        }

                        // abandon authorization attempts if one of our domains has failed verification
                        if (!allIdentifiersValidated) break;
                    }

                    //check if all identifiers have a valid authorization
                    if (identifierAuthorizations.Count != distinctDomains.Count())
                    {
                        allIdentifiersValidated = false;
                    }

                    if (allIdentifiersValidated)
                    {
                        string primaryDnsIdentifier = identifierAuthorizations.First().Identifier.Alias;
                        string[] alternativeDnsIdentifiers = identifierAuthorizations.Select(i => i.Identifier.Alias).ToArray();

                        ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Running, Message = "Requesting Certificate via Lets Encrypt" }, managedSite.Id);

                        // Perform CSR request
                        // FIXME: make call async
                        var certRequestResult = _vaultProvider.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);

                        if (certRequestResult.IsSuccess)
                        {
                            ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Success, Message = "Completed Certificate Request." }, managedSite.Id);

                            string pfxPath = certRequestResult.Result.ToString();

                            // update managed site summary
                            try
                            {
                                var certInfo = CertificateManager.LoadCertificate(pfxPath);
                                managedSite.DateStart = certInfo.NotBefore;
                                managedSite.DateExpiry = certInfo.NotAfter;
                                managedSite.DateRenewed = DateTime.Now;

                                managedSite.CertificatePath = pfxPath;
                                managedSite.CertificateRevoked = false;

                                //ensure certificate contains all the requested domains
                                var subjectNames = certInfo.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.UpnName, false);

                                LogMessage(managedSite.Id, "New certificate contains following domains: " + subjectNames, LogItemType.GeneralInfo);
                            }
                            catch (Exception)
                            {
                                LogMessage(managedSite.Id, "Failed to parse certificate dates", LogItemType.GeneralError);
                            }

                            if (managedSite.ItemType == ManagedItemType.SSL_LetsEncrypt_LocalIIS && config.PerformAutomatedCertBinding)
                            {
                                ReportProgress(progress, new RequestProgressState { CurrentState = RequestState.Running, Message = "Performing Automated Certificate Binding" });

                                // Install certificate into certificate store and bind to IIS site
                                if (_iisManager.InstallCertForRequest(managedSite, pfxPath, cleanupCertStore: true))
                                {
                                    //all done
                                    LogMessage(managedSite.Id, "Completed certificate request and automated bindings update (IIS)", LogItemType.CertificateRequestSuccessful);

                                    _siteManager.UpdatedManagedSite(managedSite);

                                    result.IsSuccess = true;
                                    result.Message = $"Certificate installed and SSL bindings updated for {config.PrimaryDomain }";
                                    ReportProgress(progress, new RequestProgressState { IsRunning = false, CurrentState = RequestState.Success, Message = result.Message });
                                }
                                else
                                {
                                    result.Message = $"An error occurred installing the certificate. Certificate file may not be valid: {pfxPath}";
                                    LogMessage(managedSite.Id, result.Message, LogItemType.GeneralError);
                                }
                            }
                            else
                            {
                                //user has opted for manual binding of certificate

                                _siteManager.UpdatedManagedSite(managedSite);

                                result.IsSuccess = true;
                                result.Message = $"Certificate created ready for manual binding: {pfxPath}";
                                LogMessage(managedSite.Id, result.Message, LogItemType.CertificateRequestSuccessful);
                            }
                        }
                        else
                        {
                            result.Message = $"The Let's Encrypt service did not issue a valid certificate in the time allowed. {(certRequestResult.ErrorMessage ?? "")}";
                            LogMessage(managedSite.Id, result.Message, LogItemType.CertficateRequestFailed);
                        }
                    }
                    else
                    {
                        result.Message = "Validation of the required challenges did not complete successfully. " + (failureSummaryMessage != null ? failureSummaryMessage : "");
                        LogMessage(managedSite.Id, result.Message, LogItemType.CertficateRequestFailed);
                    }

                    // Goto label for aborted certificate request
                    CertRequestAborted: { }
                }
                catch (Exception exp)
                {
                    result.IsSuccess = false;
                    result.Message = managedSite.Name + ": Request failed - " + exp.Message + " " + exp.ToString();
                    LogMessage(managedSite.Id, result.Message, LogItemType.CertficateRequestFailed);
                    LogMessage(managedSite.Id, String.Join("\r\n", _vaultProvider.GetActionSummary()));
                    System.Diagnostics.Debug.WriteLine(exp.ToString());
                }
                finally
                {
                    // if the request was not aborted, perform post-request actions
                    if (!result.Abort)
                    {
                        // run post-request script, if set
                        if (!string.IsNullOrEmpty(config.PostRequestPowerShellScript))
                        {
                            try
                            {
                                string scriptOutput = await PowerShellManager.RunScript(result, config.PostRequestPowerShellScript);
                                LogMessage(managedSite.Id, $"Post-Request Script output:\n{scriptOutput}");
                            }
                            catch (Exception ex)
                            {
                                LogMessage(managedSite.Id, $"Post-Request Script error: {ex.Message}");
                            }
                        }
                        // run webhook triggers, if set
                        if ((config.WebhookTrigger == Webhook.ON_SUCCESS && result.IsSuccess) ||
                            (config.WebhookTrigger == Webhook.ON_ERROR && !result.IsSuccess) ||
                            (config.WebhookTrigger == Webhook.ON_SUCCESS_OR_ERROR))
                        {
                            try
                            {
                                var (success, code) = await Webhook.SendRequest(config, result.IsSuccess);
                                LogMessage(managedSite.Id, $"Webhook invoked: Url: {config.WebhookUrl}, Success: {success}, StatusCode: {code}");
                            }
                            catch (Exception ex)
                            {
                                LogMessage(managedSite.Id, $"Webhook error: {ex.Message}");
                            }
                        }
                    }
                }
                return result;
            });
        }

        public List<DomainOption> GetDomainOptionsFromSite(string siteId)
        {
            var defaultNoDomainHost = "";
            var domainOptions = new List<DomainOption>();

            var matchingSites = _iisManager.GetSiteBindingList(CoreAppSettings.Current.IgnoreStoppedSites, siteId);
            var siteBindingList = matchingSites.Where(s => s.SiteId == siteId);

            bool includeEmptyHostnameBindings = false;

            foreach (var siteDetails in siteBindingList)
            {
                //if domain not currently in the list of options, add it
                if (!domainOptions.Any(item => item.Domain == siteDetails.Host))
                {
                    DomainOption opt = new DomainOption
                    {
                        Domain = siteDetails.Host,
                        IsPrimaryDomain = false,
                        IsSelected = true,
                        Title = ""
                    };

                    if (String.IsNullOrWhiteSpace(opt.Domain))
                    {
                        //binding has no hostname/domain set - user will need to specify
                        opt.Title = defaultNoDomainHost;
                        opt.Domain = defaultNoDomainHost;
                        opt.IsManualEntry = true;
                    }
                    else
                    {
                        opt.Title = siteDetails.Protocol + "://" + opt.Domain;
                    }

                    if (siteDetails.IP != null && siteDetails.IP != "0.0.0.0")
                    {
                        opt.Title += " : " + siteDetails.IP;
                    }

                    if (!opt.IsManualEntry || (opt.IsManualEntry && includeEmptyHostnameBindings))
                    {
                        domainOptions.Add(opt);
                    }
                }
            }

            //TODO: if one or more binding is to a specific IP, how to manage in UI?

            if (domainOptions.Any(d => !String.IsNullOrEmpty(d.Domain)))
            {
                // mark first domain as primary, if we have no other settings
                if (!domainOptions.Any(d => d.IsPrimaryDomain == true))
                {
                    var electableDomains = domainOptions.Where(d => !String.IsNullOrEmpty(d.Domain) && d.Domain != defaultNoDomainHost);
                    if (electableDomains.Any())
                    {
                        // promote first domain in list to primary by default
                        electableDomains.First().IsPrimaryDomain = true;
                    }
                }
            }

            return domainOptions;
        }

        public List<ManagedSite> ImportManagedSitesFromVault(bool mergeSitesAsSan = false)
        {
            var sites = new List<ManagedSite>();

            if (_iisManager == null || !_iisManager.IsIISAvailable)
            {
                // IIS not enabled, can't match sites to vault items
                return sites;
            }

            //get dns identifiers from vault
            var identifiers = _vaultProvider.GetDomainIdentifiers();

            // match existing IIS sites to vault items
            var iisSites = _iisManager.GetSiteBindingList(ignoreStoppedSites: CoreAppSettings.Current.IgnoreStoppedSites);

            foreach (var identifier in identifiers)
            {
                //identify IIS site related to this identifier (if any)
                var iisSite = iisSites.FirstOrDefault(d => d.Host == identifier.Dns);
                var site = new ManagedSite
                {
                    Id = Guid.NewGuid().ToString(),
                    GroupId = iisSite?.SiteId,
                    Name = identifier.Dns + (iisSite != null ? " : " + iisSite.SiteName : ""),
                    IncludeInAutoRenew = true,
                    Comments = "Imported from vault",
                    ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS,
                    TargetHost = "localhost",
                    RequestConfig = new CertRequestConfig
                    {
                        BindingIPAddress = iisSite?.IP,
                        BindingPort = iisSite?.Port.ToString(),
                        ChallengeType = ACMESharpCompat.ACMESharpUtils.CHALLENGE_TYPE_HTTP,
                        EnableFailureNotifications = true,
                        PerformAutoConfig = true,
                        PerformAutomatedCertBinding = true,
                        PerformChallengeFileCopy = true,
                        PerformExtensionlessConfigChecks = true,
                        PrimaryDomain = identifier.Dns,
                        SubjectAlternativeNames = new string[] { identifier.Dns }
                    }
                };
                site.AddDomainOption(new DomainOption { Domain = identifier.Dns, IsPrimaryDomain = true, IsSelected = true });
                sites.Add(site);
            }

            if (mergeSitesAsSan)
            {
                foreach (var s in sites)
                {
                    //merge sites with same group (iis site etc) and different primary domain
                    if (sites.Any(m => m.GroupId != null && m.GroupId == s.GroupId && m.RequestConfig.PrimaryDomain != s.RequestConfig.PrimaryDomain))
                    {
                        //existing site to merge into
                        //add san for dns
                        var mergedSite = sites.FirstOrDefault(m =>
                        m.GroupId != null && m.GroupId == s.GroupId
                        && m.RequestConfig.PrimaryDomain != s.RequestConfig.PrimaryDomain
                        && m.RequestConfig.PrimaryDomain != null
                        );
                        if (mergedSite != null)
                        {
                            mergedSite.AddDomainOption(new DomainOption { Domain = s.RequestConfig.PrimaryDomain, IsPrimaryDomain = false, IsSelected = true });

                            //use shortest version of domain name as site name
                            if (mergedSite.RequestConfig.PrimaryDomain.Contains(s.RequestConfig.PrimaryDomain))
                            {
                                mergedSite.Name = mergedSite.Name.Replace(mergedSite.RequestConfig.PrimaryDomain, s.RequestConfig.PrimaryDomain);
                            }

                            //flag spare site config to be discar
                            s.RequestConfig.PrimaryDomain = null;
                        }
                    }
                }

                //discard sites which have been merged into other sites
                sites.RemoveAll(s => s.RequestConfig.PrimaryDomain == null);
            }
            return sites;
        }

        public static bool IsRenewalRequired(ManagedSite s, int renewalIntervalDays)
        {
            // if we know the last renewal date, check whether we should renew again, otherwise
            // assume it's more than 30 days ago by default and attempt renewal

            var timeSinceLastRenewal = (s.DateRenewed.HasValue ? s.DateRenewed.Value : DateTime.Now.AddDays(-30)) - DateTime.Now;

            bool isRenewalRequired = Math.Abs(timeSinceLastRenewal.TotalDays) > renewalIntervalDays;

            return isRenewalRequired;
        }

        public async Task<List<CertificateRequestResult>> PerformRenewalAllManagedSites(bool autoRenewalOnly = true, Dictionary<string, Progress<RequestProgressState>> progressTrackers = null)
        {
            await Task.Delay(200); //allow UI to update

            //currently the vault won't let us run parallel requests due to file locks
            bool performRequestsInParallel = false;

            bool testModeOnly = false;

            _siteManager.LoadSettings();

            IEnumerable<ManagedSite> sites = _siteManager.GetManagedSites();

            if (autoRenewalOnly)
            {
                sites = sites.Where(s => s.IncludeInAutoRenew == true);
            }

            // check site list and examine current certificates. If certificate is less than n days
            // old, don't attempt to renew it
            var sitesToRenew = new List<ManagedSite>();
            var renewalIntervalDays = CoreAppSettings.Current.RenewalIntervalDays;

            int numRenewalTasks = 0;
            int maxRenewalTasks = CoreAppSettings.Current.MaxRenewalRequests;

            var renewalTasks = new List<Task<CertificateRequestResult>>();
            foreach (var s in sites.Where(s => s.IncludeInAutoRenew == true))
            {
                // determine if this site requires renewal
                bool isRenewalRequired = IsRenewalRequired(s, renewalIntervalDays);

                //if we care about stopped sites being stopped, check for that
                bool isSiteRunning = true;
                if (CoreAppSettings.Current.IgnoreStoppedSites)
                {
                    isSiteRunning = IsManagedSiteRunning(s.Id);
                }

                if (isRenewalRequired && isSiteRunning)
                {
                    //get matching progress tracker for this site
                    IProgress<RequestProgressState> tracker = null;
                    if (progressTrackers != null)
                    {
                        tracker = progressTrackers[s.Id];
                    }

                    // optionally limit the number of renewal tasks to attempt in this pass
                    if (maxRenewalTasks == 0 || maxRenewalTasks > 0 && numRenewalTasks < maxRenewalTasks)
                    {
                        if (testModeOnly)
                        {
                            //simulated request for UI testing
                            renewalTasks.Add(this.PerformDummyCertificateRequest(s, tracker));
                        }
                        else
                        {
                            renewalTasks.Add(this.PerformCertificateRequest(s, tracker));
                        }
                    }
                    numRenewalTasks++;
                }
                else
                {
                    var msg = "Skipping Renewal, existing certificate still OK. ";

                    if (isRenewalRequired && !isSiteRunning)
                    {
                        //TODO: show this as warning rather than success
                        msg = "Site stopped (or not present), renewal skipped as domain validation cannot be performed. ";
                    }

                    if (progressTrackers != null)
                    {
                        //send progress back to report skip
                        var progress = (IProgress<RequestProgressState>)progressTrackers[s.Id];
                        if (progress != null) progress.Report(new RequestProgressState { CurrentState = RequestState.Success, Message = msg });
                    }

                    ManagedSiteLog.AppendLog(s.Id, new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.GeneralInfo, Message = msg + s.Name });
                }
            }

            if (!renewalTasks.Any())
            {
                //nothing to do
                return new List<CertificateRequestResult>();
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

        private bool IsManagedSiteRunning(string id, IISManager iis = null)
        {
            var managedSite = _siteManager.GetManagedSite(id);
            if (managedSite != null)
            {
                if (iis == null) iis = _iisManager;
                return iis.IsSiteRunning(id);
            }
            else
            {
                //site not identified, assume it is running
                return true;
            }
        }

        public bool IsWindowsScheduledTaskPresent()
        {
            var taskList = Microsoft.Win32.TaskScheduler.TaskService.Instance.RootFolder.GetTasks();
            if (taskList.Any(t => t.Name == SCHEDULED_TASK_NAME))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Creates the windows scheduled task to perform renewals, running as the given userid (who
        /// should be admin level so they can perform cert mgmt and IIS management functions)
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="pwd"></param>
        /// <returns></returns>
        public bool CreateWindowsScheduledTask(string userId, string pwd)
        {
            // https://taskscheduler.codeplex.com/documentation
            var taskService = Microsoft.Win32.TaskScheduler.TaskService.Instance;
            try
            {
                var cliPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SCHEDULED_TASK_EXE);

                //setup auto renewal task, executing as admin using the given username and password
                var task = taskService.NewTask();

                task.Principal.RunLevel = Microsoft.Win32.TaskScheduler.TaskRunLevel.Highest;
                task.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(cliPath, SCHEDULED_TASK_ARGS));
                task.Triggers.Add(new Microsoft.Win32.TaskScheduler.DailyTrigger { DaysInterval = 1 });

                //register/update task
                taskService.RootFolder.RegisterTaskDefinition(SCHEDULED_TASK_NAME, task, Microsoft.Win32.TaskScheduler.TaskCreation.CreateOrUpdate, userId, pwd, Microsoft.Win32.TaskScheduler.TaskLogonType.Password);

                return true;
            }
            catch (Exception exp)
            {
                System.Diagnostics.Debug.WriteLine(exp.ToString());
                //failed to create task
                return false;
            }
        }

        public void DeleteWindowsScheduledTask()
        {
            Microsoft.Win32.TaskScheduler.TaskService.Instance.RootFolder.DeleteTask(SCHEDULED_TASK_NAME, exceptionOnNotExists: false);
        }
    }
}