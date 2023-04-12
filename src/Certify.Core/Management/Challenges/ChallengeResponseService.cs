﻿using System;
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
        /// <returns> APIResult </returns>
        /// <remarks> 
        /// The purpose of this method is to test the options (permissions, configuration) before
        /// submitting a request to the ACME server, to avoid creating failed requests and hitting
        /// usage limits.
        /// </remarks>
        public async Task<List<StatusMessage>> TestChallengeResponse(
            ILog log,
            ITargetWebServer serverManager,
            ManagedCertificate managedCertificate,
            bool isPreviewMode,
            bool enableDnsChecks,
            ICredentialsManager credentialsManager,
            IProgress<RequestProgressState> progress = null
            )
        {
            var results = new List<StatusMessage>();

            var requestConfig = managedCertificate.RequestConfig;

            if (string.IsNullOrEmpty(requestConfig.PrimaryDomain))
            {
                return new List<StatusMessage> { new StatusMessage { IsOK = false, Message = "There is no primary domain set for this certificate." } };
            }

            var identifiers = managedCertificate.GetCertificateIdentifiers();

            // TODO: some of these checks can be moved/shared with general item validation 

            // if wildcard domain included, check first level labels not also specified, i.e.
            // *.example.com & www.example.com cannot be mixed, but example.com, *.example.com &
            // test.wwww.example.com can
            var invalidLabels = new List<CertIdentifierItem>();
            if (identifiers.Any(d => d.IdentifierType == CertIdentifierType.Dns && d.Value.StartsWith("*.")))
            {
                foreach (var wildcard in identifiers.Where(d => d.IdentifierType == CertIdentifierType.Dns && d.Value.StartsWith("*.")))
                {
                    var rootDomain = wildcard.Value.Replace("*.", "");
                    // add list of identifiers where label count exceeds root domain label count
                    invalidLabels.AddRange(identifiers.Where(domain => domain.Value != wildcard.Value && domain.Value.EndsWith(rootDomain) && domain.Value.Count(s => s == '.') == wildcard.Value.Count(s => s == '.')));

                    if (invalidLabels.Any())
                    {
                        results.Add(new StatusMessage { IsOK = false, Message = $"Wildcard domain certificate requests (e.g. {wildcard}) cannot be mixed with requests including immediate subdomains (e.g. {invalidLabels[0]})." });
                        return results;
                    }
                }
            }

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
                            results.Add(new StatusMessage { IsOK = false, Message = $"http-01 authorization cannot be used for wildcard domains: {domain}. Use DNS (dns-01) validation instead." });
                            return results;
                        }

                        var challengeFileUrl = $"http://{domain}/.well-known/acme-challenge/configcheck";

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
                            result.FailedItemSummary.Add($"Config checks failed to verify http://{domain} is both publicly accessible and can serve extensionless files e.g. {challengeFileUrl}");
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
                    else if (challengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    {
                        var recordName = $"_acme-challenge-test.{domain}".Replace("*.", "");

                        // ISSUE: dependency on changing behavior for a specific plugin
                        if (challengeConfig.ChallengeProvider == "DNS01.API.AcmeDns" || challengeConfig.ChallengeProvider == "DNS01.API.CertifyDns")
                        {
                            // use real cname to avoid having to setup different records
                            recordName = $"_acme-challenge.{domain}".Replace("*.", "");
                        }

                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem> {
                                     new AuthorizationChallengeItem
                                     {
                                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                            Key= recordName,
                                            Value = GenerateSimulatedDnsAuthValue()
                                     }
                                 }
                        };
                        generatedAuthorizations.Add(simulatedAuthorization);

                        var dnsResult =
                             await PerformChallengeResponse_Dns01(
                                log,
                                domain,
                                managedCertificate,
                                simulatedAuthorization,
                                isTestMode: true,
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

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                    {
                        // perform tls-sni-01 challenge response
                        var check = PrepareChallengeResponse_TlsSni01(log, iisManager, pendingAuth.Identifier, managedCertificate, pendingAuth);
                        if (requestConfig.PerformTlsSniBindingConfigChecks)
                        {
                            // set config check OK if all checks return true
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = check();
                        }
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    {
                        // perform dns-01 challenge response
                        var check = await PerformChallengeResponse_Dns01(log, pendingAuth.Identifier, managedCertificate, pendingAuth, isTestMode: false, credentialsManager);
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
                        // perform tkauth-01 challenge response
                        //var check = await PerformChallengeResponse_Dns01(log, domain, managedCertificate, pendingAuth, isTestMode: false, credentialsManager);
                        /*
                         pendingAuth.AttemptedChallenge.IsFailure = !check.Result.IsSuccess;
                         pendingAuth.AttemptedChallenge.ChallengeResultMsg = check.Result.Message;
                         pendingAuth.AttemptedChallenge.IsAwaitingUser = check.IsAwaitingUser;
                         pendingAuth.AttemptedChallenge.PropagationSeconds = check.PropagationSeconds;
                         pendingAuth.IsFailure = !check.Result.IsSuccess;
                         pendingAuth.AuthorizationError = pendingAuth.IsFailure ? check.Result.Message : "";*/
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

                var msg = $"The website root path for {managedCertificate.Name} could not be determined. Fileysystem based http validation will not be possible.";
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
                    if (!destFile.EndsWith("configcheck") && File.Exists(destFile))
                    {
                        log.Debug("Challenge Cleanup: Removing {file}", destFile);
                        try
                        {
                            File.Delete(destFile);
                        }
                        catch { }
                    }
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

                    var configOptions = Directory.EnumerateFiles(Path.Combine(Environment.CurrentDirectory, "Scripts", "Web.config"), "*.config");

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

        private Func<bool> PrepareChallengeResponse_TlsSni01(ILog log, ITargetWebServer iisManager, CertIdentifierItem domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedCertificate.RequestConfig;

            var tlsSniChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI);

            if (tlsSniChallenge == null)
            {
                log.Warning($"No tls-sni-01 challenge to complete for {managedCertificate.Name}. Request cannot continue.");
                return () => false;
            }

            var sha256 = System.Security.Cryptography.SHA256.Create();

            var z = new byte[tlsSniChallenge.HashIterationCount][];

            // compute n sha256 hashes, where n=challengedata.iterationcount
            z[0] = sha256.ComputeHash(Encoding.UTF8.GetBytes(tlsSniChallenge.Value));

            for (var i = 1; i < z.Length; i++)
            {
                z[i] = sha256.ComputeHash(z[i - 1]);
            }

            // generate certs and install iis bindings
            var cleanupQueue = new List<Func<Task>>();

            var checkQueue = new List<Func<bool>>();

            foreach (var hex in z.Select(b =>
                BitConverter.ToString(b).Replace("-", "").ToLower()))
            {
                var sni = $"{hex.Substring(0, 32)}.{hex.Substring(32)}.acme.invalid";

                log.Information($"Preparing binding at: https://{domain}, sni: {sni}");

                var x509 = CertificateManager.GenerateSelfSignedCertificate(sni);

                CertificateManager.StoreCertificate(x509);

                var certStoreName = CertificateManager.GetMachineStore().Name;

                // iisManager.InstallCertificateforBinding(certStoreName, x509.GetCertHash(),
                // managedCertificate.ServerSiteId, sni);

                // add check to the queue
                checkQueue.Add(() => _netUtil.CheckSNI(domain.Value, sni).Result);

                // add cleanup actions to queue
                cleanupQueue.Add(() => iisManager.RemoveHttpsBinding(managedCertificate.ServerSiteId, sni));

                cleanupQueue.Add(() => Task.Run(() => CertificateManager.RemoveCertificate(x509)));
            }

            // configure cleanup to execute the cleanup queue
            pendingAuth.Cleanup = async () =>
            {
                cleanupQueue.ForEach(a => a());
            };

            // perform our own config checks
            return () => checkQueue.All(check => check());
        }

        private async Task<DnsChallengeHelperResult> PerformChallengeResponse_Dns01(ILog log, CertIdentifierItem domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth, bool isTestMode, ICredentialsManager credentialsManager)
        {
            var dnsChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);

            if (dnsChallenge == null)
            {
                var msg = $"No dns-01 challenge to completed for {managedCertificate.Name}. Request cannot continue.";

                log.Warning(msg);

                return new DnsChallengeHelperResult(new ActionResult
                {
                    IsSuccess = false,
                    Message = msg
                });
            }

            // create DNS records (manually or via automation)
            var dnsHelper = new DnsChallengeHelper(credentialsManager);

            var dnsResult = await dnsHelper.CompleteDNSChallenge(log, managedCertificate, domain, dnsChallenge.Key, dnsChallenge.Value, isTestMode);

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

            var cleanupQueue = new List<Action> { };

            // configure cleanup actions for use after challenge completes
            pendingAuth.Cleanup = async () =>
               {
                   _ = await dnsHelper.DeleteDNSChallenge(log, managedCertificate, domain, dnsChallenge.Key, dnsChallenge.Value);
               };

            return dnsResult;
        }
    }
}
