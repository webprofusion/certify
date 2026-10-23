using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Models.Shared;
using Certify.Models.Shared.Validation;
using Certify.Shared.Core.Utils;

namespace Certify.Core.Management.Challenges
{
    public class ChallengeResponseService : ActionLogCollector
    {
        private NetworkUtils _netUtil;

        public ChallengeResponseService(bool enableProxyAPI)
        {
            _netUtil = new NetworkUtils(enableProxyAPI)
            {
                Log = (message) => LogAction(message)
            };
        }

        /// <summary>
        /// Simulates responding to a challenge, performs a sample configuration and attempts to
        /// verify it.
        /// </summary>
        /// <param name="serverManager">  </param>
        /// <param name="managedCertificate">  </param>
        /// <param name="performCleanupOnly"> If true, perform cleanup of challenges only, this mode otherwise the same as a non-test challenge response.</param>
        /// <param name="dnsPersistDirectory"> The account and CA directory values dns-persist-01 records are checked against, if any identifier uses dns-persist-01.</param>
        /// <param name="acmeAccountUri"> The URL of the ACME account the request uses, which dns-account-01 record names are derived from.</param>
        /// <returns> APIResult </returns>
        /// <remarks> 
        /// The purpose of this method is to test the options (permissions, configuration) before
        /// submitting a request to the ACME server, to avoid creating failed requests and hitting
        /// usage limits. Also used as a method for batch cleanup of challenge responses (DNS etc)
        /// </remarks>
        public async Task<List<StatusMessage>> TestChallengeResponse(
            ILog log,
            ITargetWebServer serverManager,
            ManagedCertificate managedCertificate,
            bool isPreviewMode,
            bool enableDnsChecks,
            bool performCleanupOnly,
            ICredentialsManager credentialsManager,
            IProgress<RequestProgressState> progress = null,
            DnsPersistDirectoryInfo dnsPersistDirectory = null,
            string acmeAccountUri = null
            )
        {
            var results = new List<StatusMessage>();

            var checkedPersistRecords = new HashSet<string>(StringComparer.Ordinal);

            var manualDnsNoticeAdded = false;

            var requestConfig = managedCertificate.RequestConfig;

            if (!performCleanupOnly)
            {
                var validationResult = CertificateEditorService.Validate(managedCertificate, null, null, false);

                if (!validationResult.IsValid)
                {
                    results.Add(new StatusMessage { IsOK = false, Message = validationResult.Message });
                    return results;
                }
            }

            var identifiers = managedCertificate.GetCertificateIdentifiers();

            var generatedAuthorizations = new List<PendingAuthorization>();

            try
            {
                // if DNS checks enabled, attempt them here
                if (isPreviewMode && enableDnsChecks)
                {
                    var includeIPResolution = false;
                    if (managedCertificate.RequestConfig.Challenges.Any(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP))
                    {
                        includeIPResolution = true;
                    }

                    log.Information("Performing preview DNS tests. {managedItem}", managedCertificate);

                    var tasks = new List<Task<List<ActionResult>>>();

                    foreach (var d in identifiers.Where(d => d.IdentifierType == CertIdentifierType.Dns))
                    {
                        tasks.Add(_netUtil.CheckDNS(log, d.Value.Replace("*.", ""), includeIPResolution));
                    }

                    var allResults = await Task.WhenAll(tasks);

                    // add DNS check results. DNS check fails are considered a warning instead of an error.
                    foreach (var checkResults in allResults)
                    {
                        foreach (var c in checkResults)
                        {
                            results.Add(new StatusMessage
                            {
                                IsOK = true,
                                HasWarning = !c.IsSuccess,
                                Message = c.Message
                            });
                        }
                    }
                }

                foreach (var domain in identifiers)
                {
                    var challengeConfig = managedCertificate.GetChallengeConfig(domain);

                    var challengeType = challengeConfig.ChallengeType;
                    if (challengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                    {
                        log.Warning("tls-sni-01 challenge type is no longer supported by the Certificate Authority. Falling back to http-01");
                        challengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP;
                    }

                    if (challengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                    {
                        // if dns validation not selected but one or more identifiers is a wildcard, reject
                        if (domain.Value.StartsWith("*."))
                        {
                            results.Add(new StatusMessage { IsOK = false, Message = $"http-01 authorization cannot be used for wildcard domains: {domain.Value}. Use DNS (dns-01) validation instead." });
                            return results;
                        }

                        var challengeFileUrl = $"http://{domain.Value}/.well-known/acme-challenge/configcheck";

                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem>{
                                    new AuthorizationChallengeItem
                                {
                                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                                        ResourcePath = Path.Combine(".well-known","acme-challenge","configcheck"),
                                        ResourceUri = challengeFileUrl,
                                        Value = "Extensionless File Config Test - OK"
                                    }
                                }
                        };

                        generatedAuthorizations.Add(simulatedAuthorization);

                        var httpChallengeResult = await PerformChallengeResponse_Http01(
                           log, serverManager, domain, managedCertificate, simulatedAuthorization
                        );

                        if (!httpChallengeResult.IsSuccess)
                        {
                            var result = new StatusMessage();
                            result.IsOK = false;
                            result.FailedItemSummary.Add($"Config checks failed to verify http://{domain.Value} is both publicly accessible and can serve extensionless files e.g. {challengeFileUrl}");
                            result.Message = httpChallengeResult.Message;
                            results.Add(result);

                            // don't check any more after first failure
                            break;
                        }
                        else
                        {
                            results.Add(new StatusMessage { IsOK = true, Message = httpChallengeResult.Message, Result = httpChallengeResult });
                        }
                    }
                    else if (challengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                    {
                        var result = new StatusMessage();
                        result.IsOK = false;
                        result.FailedItemSummary.Add($"The {SupportedChallengeTypes.CHALLENGE_TYPE_SNI} challenge type is no longer available.");
                        results.Add(result);

                        return results;

                    }
                    else if (SupportedChallengeTypes.UsesDnsProvider(challengeType))
                    {
                        // manual DNS can't be tested, a test record would only ask the user to create a record that is never used
                        if (!performCleanupOnly && challengeConfig.ChallengeProvider == DNS.DnsProviderManual.Definition.Id)
                        {
                            if (!manualDnsNoticeAdded)
                            {
                                results.Add(new StatusMessage { IsOK = true, Message = "No test available for Manual DNS. The DNS records to create will be shown when the certificate is requested." });
                                manualDnsNoticeAdded = true;
                            }

                            continue;
                        }

                        var recordName = $"{Certify.Models.Hub.ManagedChallengeAccess.DnsChallengeTestRecordPrefix}{domain.Value}".Replace("*.", "");

                        // the record a real challenge response uses, which for dns-account-01 depends on the ACME account
                        string realRecordName = null;
                        if (challengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                        {
                            realRecordName = $"_acme-challenge.{domain.Value}".Replace("*.", "");
                        }
                        else if (!string.IsNullOrEmpty(acmeAccountUri))
                        {
                            realRecordName = DnsAccountLabel.GetRecordName(acmeAccountUri, domain.Value);
                        }

                        if (performCleanupOnly)
                        {
                            if (realRecordName == null)
                            {
                                results.Add(new StatusMessage { IsOK = true, HasWarning = true, Message = $"The dns-account-01 record for {domain.Value} was not cleaned up, because the ACME account is not known." });
                                continue;
                            }

                            recordName = realRecordName;
                        }

                        // ISSUE: dependency on changing behavior for a specific plugin
                        if ((challengeConfig.ChallengeProvider == "DNS01.API.AcmeDns" || challengeConfig.ChallengeProvider == "DNS01.API.CertifyDns") && realRecordName != null)
                        {
                            // use real cname to avoid having to setup different records
                            recordName = realRecordName;
                        }

                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem> {
                                     new AuthorizationChallengeItem
                                     {
                                          ChallengeType = challengeType,
                                            Key= recordName,
                                            Value = GenerateSimulatedDnsAuthValue()
                                     }
                                 }
                        };
                        generatedAuthorizations.Add(simulatedAuthorization);

                        var dnsResult =
                             await PerformChallengeResponse_Dns01(
                                log,
                                challengeType,
                                domain,
                                managedCertificate,
                                simulatedAuthorization,
                                isTestMode: performCleanupOnly ? false : true, //for cleanup only mode we use the real challenge record name, test mode uses a test record name
                                isCleanupOnly: performCleanupOnly,
                                credentialsManager
                            );

                        var result = new StatusMessage();

                        result.Message = dnsResult.Result.Message;
                        result.IsOK = dnsResult.Result.IsSuccess;
                        if (!result.IsOK)
                        {
                            result.FailedItemSummary.Add(dnsResult.Result.Message);
                        }

                        results.Add(result);
                    }
                    else if (challengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST)
                    {
                        // dns-persist-01: check the existing _validation-persist record, once per record name
                        var persistRecordName = DnsPersistRecordBuilder.GetRecordName(domain.Value);

                        if (performCleanupOnly || !checkedPersistRecords.Add(persistRecordName))
                        {
                            continue;
                        }

                        if (dnsPersistDirectory == null)
                        {
                            results.Add(new StatusMessage
                            {
                                IsOK = true,
                                HasWarning = true,
                                Message = $"The dns-persist-01 record {persistRecordName} was not checked, because the ACME account or its CA directory is not available."
                            });
                            continue;
                        }

                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem> {
                                new AuthorizationChallengeItem
                                {
                                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST,
                                    Key = persistRecordName,
                                    DnsPersist = DnsPersistChallengeInfo.FromDirectory(dnsPersistDirectory)
                                }
                            }
                        };

                        var persistResult =
                            await PerformChallengeResponse_DnsPersist01(
                                log,
                                domain,
                                managedCertificate,
                                simulatedAuthorization,
                                isCleanupOnly: false
                            );

                        var result = new StatusMessage();
                        result.Message = persistResult.Result.Message;
                        result.IsOK = persistResult.Result.IsSuccess;
                        result.HasWarning = persistResult.Result.IsWarning;
                        if (!result.IsOK)
                        {
                            result.FailedItemSummary.Add(persistResult.Result.Message);
                        }

                        results.Add(result);
                    }
                    else
                    {
                        throw new NotSupportedException($"ChallengeType not supported: {challengeConfig.ChallengeType}");
                    }
                }
            }
            finally
            {
                log?.Verbose("Performing test challenge cleanup");

                foreach (var ga in generatedAuthorizations)
                {
                    if (ga.Cleanup != null)
                    {
                        await ga.Cleanup();
                    }
                }
            }

            return results;
        }

        private string GenerateSimulatedKeyAuth()
        {
            // create simulated challenge

            var random = new Random();

            var simulated_token_data = new byte[24]; // generate 192 bits of data

            random.NextBytes(simulated_token_data);

            var simulated_token = Certify.Management.Util.ToUrlSafeBase64String(simulated_token_data);

            var sha256 = System.Security.Cryptography.SHA256.Create();

            var thumbprint_data = sha256.ComputeHash(Encoding.UTF8.GetBytes(simulated_token));

            var thumbprint = Certify.Management.Util.ToUrlSafeBase64String(thumbprint_data);

            return $"{simulated_token}.{thumbprint}";
        }

        private string GenerateSimulatedDnsAuthValue()
        {

            // create simulated challenge response

            var random = new Random();

            var simulated_token_data = new byte[24]; // generate 192 bits of data

            random.NextBytes(simulated_token_data);

            var simulated_token = Certify.Management.Util.ToUrlSafeBase64String(simulated_token_data);

            var hash = "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var keyAuthzDig = sha.ComputeHash(Encoding.UTF8.GetBytes(simulated_token));
                hash = Certify.Management.Util.ToUrlSafeBase64String(keyAuthzDig);

            }

            return $"{hash}";
        }

        public async Task<PendingAuthorization> PrepareAutomatedChallengeResponse(ILog log, ITargetWebServer iisManager, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth, ICredentialsManager credentialsManager)
        {
            var requestConfig = managedCertificate.RequestConfig;

            var challengeConfig = managedCertificate.GetChallengeConfig(pendingAuth.Identifier);

            if (pendingAuth.Challenges != null)
            {
                // from list of possible challenges, select the one we prefer to attempt
                var requiredChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == challengeConfig.ChallengeType);

                if (requiredChallenge != null)
                {
                    pendingAuth.AttemptedChallenge = requiredChallenge;
                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                    {
                        // perform http-01 challenge response
                        var check = await PerformChallengeResponse_Http01(log, iisManager, pendingAuth.Identifier, managedCertificate, pendingAuth);
                        if (requestConfig.PerformExtensionlessConfigChecks)
                        {
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = check.IsSuccess;
                        }
                    }

                    if (SupportedChallengeTypes.UsesDnsProvider(requiredChallenge.ChallengeType))
                    {
                        // perform dns-01 (or dns-account-01) challenge response
                        var check = await PerformChallengeResponse_Dns01(log, requiredChallenge.ChallengeType, pendingAuth.Identifier, managedCertificate, pendingAuth, isTestMode: false, isCleanupOnly: false, credentialsManager);
                        pendingAuth.AttemptedChallenge.IsFailure = !check.Result.IsSuccess;
                        pendingAuth.AttemptedChallenge.ChallengeResultMsg = check.Result.Message;
                        pendingAuth.AttemptedChallenge.IsAwaitingUser = check.IsAwaitingUser;
                        pendingAuth.AttemptedChallenge.PropagationSeconds = check.PropagationSeconds;
                        pendingAuth.IsFailure = !check.Result.IsSuccess;
                        pendingAuth.AuthorizationError = pendingAuth.IsFailure ? check.Result.Message : "";
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST)
                    {
                        // perform dns-persist-01 challenge response (draft-ietf-acme-dns-persist): check the
                        // persistent _validation-persist TXT record the domain owner created, warning if the CA may not accept it
                        var check = await PerformChallengeResponse_DnsPersist01(log, pendingAuth.Identifier, managedCertificate, pendingAuth, isCleanupOnly: false);
                        pendingAuth.AttemptedChallenge.IsFailure = !check.Result.IsSuccess;
                        pendingAuth.AttemptedChallenge.ChallengeResultMsg = check.Result.Message;
                        pendingAuth.AttemptedChallenge.IsAwaitingUser = check.IsAwaitingUser;
                        pendingAuth.AttemptedChallenge.PropagationSeconds = check.PropagationSeconds;
                        pendingAuth.IsFailure = !check.Result.IsSuccess;
                        pendingAuth.AuthorizationError = pendingAuth.IsFailure ? check.Result.Message : "";
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_TKAUTH)
                    {
                        pendingAuth.AttemptedChallenge.ConfigCheckedOK = true;
                    }
                }
            }

            return pendingAuth;
        }

        /// <summary>
        /// Prepares IIS to respond to a http-01 challenge
        /// </summary>
        /// <returns> Test the challenge response locally. </returns>
        private async Task<ActionResult> PerformChallengeResponse_Http01(ILog log, ITargetWebServer iisManager, CertIdentifierItem domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedCertificate.RequestConfig;
            var httpChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP);

            if (httpChallenge == null)
            {
                var msg = $"No http challenge to complete for {managedCertificate.Name}. Request cannot continue.";
                log.Warning(msg);
                return new ActionResult { IsSuccess = false, Message = msg };
            }

            log.Information($"Preparing challenge response for the issuing Certificate Authority to check at: {httpChallenge.ResourceUri} with content {httpChallenge.Value}");
            log.Information("If the challenge response file is not accessible at this exact URL the validation will fail and a certificate will not be issued.");

            // get website root path (from challenge config or fallback to deprecated
            // WebsiteRootPath), expand environment variables if required
            var websiteRootPath = requestConfig.WebsiteRootPath;
            var challengeConfig = managedCertificate.GetChallengeConfig(domain);
            if (!string.IsNullOrEmpty(challengeConfig.ChallengeRootPath))
            {
                websiteRootPath = challengeConfig.ChallengeRootPath;
            }

            if (!string.IsNullOrEmpty(managedCertificate.ServerSiteId))
            {
                var siteInfo = await iisManager.GetSiteById(managedCertificate.ServerSiteId);

                if (siteInfo == null)
                {
                    return new ActionResult { IsSuccess = false, Message = "IIS Website unavailable. Site may be removed or IIS is unavailable" };
                }

                // if website root path not specified, determine it now
                if (string.IsNullOrEmpty(websiteRootPath))
                {
                    websiteRootPath = siteInfo.Path;
                }

                if (!string.IsNullOrEmpty(websiteRootPath) && websiteRootPath.Contains("%"))
                {
                    // if websiteRootPath contains %websiteroot% variable, replace that with the
                    // current physical path for the site
                    if (websiteRootPath.Contains("%websiteroot%"))
                    {
                        // sets env variable for this process only
                        Environment.SetEnvironmentVariable("websiteroot", siteInfo.Path);
                    }
                    // expand any environment variables present in site path
                    websiteRootPath = Environment.ExpandEnvironmentVariables(websiteRootPath);
                }
            }

            log.Information("Using website path {path}", websiteRootPath ?? "[Auto]");

            var performFilesystemBasedValidation = false;
            var destPath = "";

            if (string.IsNullOrEmpty(websiteRootPath) || !Directory.Exists(websiteRootPath))
            {
                // Our website root path is unknown. Log as warning. This is ok if http challenge server is running but will fail if we had to fallback to filesystem validation

                var msg = $"The website root path for {managedCertificate.Name} could not be determined. Filesystem based http validation will not be possible.";
                log.Warning(msg);
                performFilesystemBasedValidation = false;
            }
            else
            {

                // copy temp file to path challenge expects in web folder
                var destFile = Path.Combine(websiteRootPath, httpChallenge.ResourcePath);
                destPath = Path.GetDirectoryName(destFile);

                if (!Directory.Exists(destPath))
                {
                    try
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    catch (Exception exp)
                    {
                        // failed to create directory, probably permissions or may be invalid config

                        var msg = $"Pre-config check failed: Could not create directory: {destPath}";
                        log.Error(exp, msg);
                        return new ActionResult { IsSuccess = false, Message = msg };
                    }
                }

                // copy challenge response to web folder /.well-known/acme-challenge. Check if it already
                // exists (as in 'configcheck' file) as can cause conflicts.
                if (!File.Exists(destFile) || !destFile.EndsWith("configcheck"))
                {
                    try
                    {
                        File.WriteAllText(destFile, httpChallenge.Value);
                    }
                    catch (Exception exp)
                    {
                        // failed to create configcheck file, probably permissions or may be invalid config

                        var msg = $"Pre-config check failed: Could not create file: {destFile}";
                        log.Error(exp, msg);
                        return new ActionResult { IsSuccess = false, Message = msg };
                    }
                }

                // prepare cleanup - should this be configurable? Because in some case many sites
                // renewing may all point to the same web root, we keep the configcheck file
                pendingAuth.Cleanup = async () =>
                {
                    await Task.Run(() =>
                    {
                        if (!destFile.EndsWith("configcheck") && File.Exists(destFile))
                        {
                            log.Debug("Challenge Cleanup: Removing {file}", destFile);
                            try
                            {
                                File.Delete(destFile);
                            }
                            catch { }
                        }
                    });
                };

                performFilesystemBasedValidation = true;
            }

            // if config checks are enabled but our last renewal was successful, skip auto config
            // until we have failed twice
            if (requestConfig.PerformExtensionlessConfigChecks)
            {
                if (managedCertificate.DateRenewed != null && managedCertificate.RenewalFailureCount < 2)
                {
                    return new ActionResult { IsSuccess = true, Message = $"Skipping URL access checks and auto config (if applicable): {httpChallenge.ResourceUri}. Will resume checks if renewal failure count exceeds 2 attempts." };
                }

                // first check if it already works with no changes
                if (await _netUtil.CheckURL(log, httpChallenge.ResourceUri))
                {
                    return new ActionResult { IsSuccess = true, Message = $"Verified URL is accessible: {httpChallenge.ResourceUri}" };
                }

                // initial check didn't work, if auto config enabled attempt to find a working config
                if (requestConfig.PerformAutoConfig && performFilesystemBasedValidation)
                {
                    // FIXME: need to only overwrite config we have auto populated, not user
                    // specified config, compare to our preconfig and only overwrite if same as ours?
                    // Or include preset key in our config, or make behaviour configurable
                    LogAction($"Pre-config check failed: Auto-config will overwrite existing config: {destPath}\\web.config");

                    var configPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "Web.config");
                    var configOptions = Directory.EnumerateFiles(configPath, "*.config");

                    foreach (var configFile in configOptions)
                    {
                        // create a web.config for extensionless files, then test it (make a request
                        // for the extensionless configcheck file over http)

                        var webConfigContent = File.ReadAllText(configFile);

                        // no existing config, attempt auto config and perform test
                        LogAction($"Testing config alternative: " + configFile);

                        try
                        {
                            System.IO.File.WriteAllText(Path.Combine(destPath, "web.config"), webConfigContent);
                        }
                        catch (Exception exp)
                        {
                            LogAction($"Failed to write config: " + exp.Message);
                        }

                        if (await _netUtil.CheckURL(log, httpChallenge.ResourceUri))
                        {
                            return new ActionResult { IsSuccess = true, Message = $"Verified URL is accessible: {httpChallenge.ResourceUri}" };
                        }
                    }
                }

                // failed to auto configure or confirm resource is accessible
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Could not verify URL is accessible: {httpChallenge.ResourceUri}"
                };
            }
            else
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = $"Config checks disabled. Did not verify URL access: {httpChallenge.ResourceUri}"
                };
            }
        }

        private DnsChallengeHelper _dnsHelper = null;

        /// <summary>
        /// Create the TXT record for a dns-01 or dns-account-01 challenge, which differ only in the record name the
        /// challenge gives, through the configured DNS provider
        /// </summary>
        internal async Task<DnsChallengeHelperResult> PerformChallengeResponse_Dns01(ILog log, string challengeType, CertIdentifierItem domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth, bool isTestMode, bool isCleanupOnly, ICredentialsManager credentialsManager)
        {
            var dnsChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == challengeType);

            if (dnsChallenge == null)
            {
                var msg = $"No {challengeType} challenge to completed for {managedCertificate.Name}. Request cannot continue.";

                log.Warning(msg);

                return new DnsChallengeHelperResult(new ActionResult
                {
                    IsSuccess = false,
                    Message = msg
                });
            }

            // create DNS records (manually or via automation)
            if (_dnsHelper == null)
            {
                _dnsHelper = new DnsChallengeHelper(credentialsManager);
            }

            DnsChallengeHelperResult dnsResult;
            if (!isCleanupOnly)
            {
                dnsResult = await _dnsHelper.CompleteDNSChallenge(log, managedCertificate, domain, dnsChallenge.Key, dnsChallenge.Value, isTestMode);

                if (!dnsResult.Result.IsSuccess)
                {
                    if (dnsResult.IsAwaitingUser)
                    {
                        log?.Error($"Action Required: {dnsResult.Result.Message}");
                    }
                    else
                    {
                        log?.Error($"DNS update failed: {dnsResult.Result.Message}");
                    }
                }
                else
                {
                    log.Information($"DNS: {dnsResult.Result.Message}");
                }
            }
            else
            {
                dnsResult = new DnsChallengeHelperResult { Result = new ActionResult("Preparing to delete DNS challenge TXT record.", true) };
            }

            var cleanupQueue = new List<Action> { };

            // configure cleanup actions for use after challenge completes
            pendingAuth.Cleanup = async () =>
               {
                   _ = await _dnsHelper.DeleteDNSChallenge(log, managedCertificate, domain, dnsChallenge.Key, dnsChallenge.Value);
               };

            return dnsResult;
        }

        /// <summary>
        /// Checks DNS for the persistent _validation-persist TXT record required for dns-persist-01 validation
        /// (draft-ietf-acme-dns-persist). The record is created once by the domain owner and is never cleaned up.
        /// If no record the CA will accept is found, a warning describing the problem and the record to create is
        /// logged, and the request continues for the CA to check.
        /// </summary>
        internal async Task<DnsChallengeHelperResult> PerformChallengeResponse_DnsPersist01(ILog log, CertIdentifierItem domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth, bool isCleanupOnly)
        {
            var persistChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS_PERSIST);

            if (persistChallenge == null)
            {
                var msg = $"No dns-persist-01 challenge to complete for {managedCertificate.Name}. Request cannot continue.";
                log.Warning(msg);
                return new DnsChallengeHelperResult(new ActionResult { IsSuccess = false, Message = msg });
            }

            // the _validation-persist record is intentionally persistent - no cleanup action
            pendingAuth.Cleanup = () => Task.CompletedTask;

            if (isCleanupOnly)
            {
                return new DnsChallengeHelperResult(new ActionResult("dns-persist-01 records are persistent and are not removed.", true));
            }

            var challenge = persistChallenge.DnsPersist;
            if (challenge == null)
            {
                return new DnsChallengeHelperResult(new ActionResult($"To complete the dns-persist-01 challenge create a TXT record {persistChallenge.Key} in your domain DNS with value {persistChallenge.Value}", true));
            }

            var validationDomain = DnsPersistRecordBuilder.GetValidationDomain(domain.Value);
            var recordName = DnsPersistRecordBuilder.GetRecordName(domain.Value);

            // identifiers sharing this record (e.g. example.com and *.example.com) need one record, with policy=wildcard if any is a wildcard
            var requiresWildcard = DnsPersistRecordBuilder.IsWildcard(domain.Value)
                || DnsPersistRecordBuilder.GroupByRecord(managedCertificate.GetCertificateIdentifiers().Where(i => i.IdentifierType == CertIdentifierType.Dns).Select(i => i.Value))
                    .Any(g => g.Domain == validationDomain && g.RequiresWildcard);

            var txtValues = await _netUtil.GetDNSRecordTXTValues(log, recordName);

            if (txtValues == null)
            {
                // this instance could not query DNS, which does not mean the CA cannot, so let the CA check the record
                var notChecked = $"dns-persist-01: the TXT records at {recordName} could not be checked from this instance, so they are left for the CA to check.";
                log?.Warning(notChecked);
                return new DnsChallengeHelperResult(new ActionResult(notChecked, true) { IsWarning = true });
            }

            var check = DnsPersistRecordChecker.Check(txtValues, challenge, domain.Value, requiresWildcard, DateTimeOffset.UtcNow);

            foreach (var warning in check.Warnings)
            {
                log?.Warning($"dns-persist-01: {warning}");
            }

            if (check.IsMatch)
            {
                persistChallenge.Key = recordName;
                persistChallenge.Value = check.MatchedRecord.Raw;

                var found = $"dns-persist-01: found the TXT record {recordName} for {check.MatchedRecord.IssuerDomainName}.";
                log?.Information(found);

                return new DnsChallengeHelperResult(new ActionResult(string.Join(" ", new[] { found }.Concat(check.Warnings)), true) { IsWarning = check.Warnings.Any() });
            }

            var record = DnsPersistRecordBuilder.Build(new DnsPersistRecordOptions
            {
                Variant = challenge.Variant,
                Domain = validationDomain,
                IssuerDomainName = DnsPersistRecordBuilder.ChooseIssuerDomainName(challenge.IssuerDomainNames, challenge.DirectoryIssuerDomainNames, check.IssuersInDns),
                AccountUrl = challenge.AccountUrl,
                AccountThumbprint = challenge.AccountThumbprint,
                AccountHashPrefix = challenge.AccountHashPrefix,
                PolicyWildcard = requiresWildcard
            });

            persistChallenge.Key = record.Name;
            persistChallenge.Value = record.Value;

            var instructions = $"dns-persist-01: {string.Join(" ", check.Problems)} The CA may not accept the existing records. A record it will accept is the TXT record {record.Name} with the value {record.Value}";

            if (record.ValueStrings.Count > 1)
            {
                instructions += $" (as {record.ValueStrings.Count} strings: {record.ZoneFileEntry})";
            }

            // this instance's view of DNS can differ from the CA's (e.g. split-horizon DNS), so the CA still checks the record
            log?.Warning(instructions);

            return new DnsChallengeHelperResult(new ActionResult(instructions, true) { IsWarning = true });
        }
    }
}
