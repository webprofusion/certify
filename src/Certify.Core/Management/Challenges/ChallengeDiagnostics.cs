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

namespace Certify.Core.Management.Challenges
{
    public class ChallengeDiagnostics : ActionLogCollector
    {
        private NetworkUtils _netUtil;

        public ChallengeDiagnostics(bool enableProxyAPI)
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
        /// <param name="serverManager"></param>
        /// <param name="managedCertificate"></param>
        /// <returns> APIResult </returns>
        /// <remarks>
        /// The purpose of this method is to test the options (permissions, configuration) before
        /// submitting a request to the ACME server, to avoid creating failed requests and hitting
        /// usage limits.
        /// </remarks>
        public async Task<List<StatusMessage>> TestChallengeResponse(
            ILog log,
            ICertifiedServer serverManager,
            ManagedCertificate managedCertificate,
            bool isPreviewMode,
            bool enableDnsChecks,
            IProgress<RequestProgressState> progress = null
            )
        {
            var results = new List<StatusMessage>();

            var requestConfig = managedCertificate.RequestConfig;
            var result = new StatusMessage { IsOK = true };
            var domains = new List<string> { requestConfig.PrimaryDomain };

            if (requestConfig.SubjectAlternativeNames != null)
            {
                domains.AddRange(requestConfig.SubjectAlternativeNames);
            }

            domains = domains.Distinct().ToList();

            var generatedAuthorizations = new List<PendingAuthorization>();

            try
            {
                // if DNS checks enabled, attempt them here
                if (isPreviewMode && enableDnsChecks)
                {
                    log.Information("Performing preview DNS tests. {managedItem}", managedCertificate);

                    // check all domain configs
                    Parallel.ForEach(domains, new ParallelOptions
                    {
                        // check 8 domains at a time
                        MaxDegreeOfParallelism = 8
                    },
                    domain =>
                    {
                        var (ok, message) = _netUtil.CheckDNS(log, domain);
                        if (!ok)
                        {
                            result.IsOK = false;
                            result.FailedItemSummary.Add(message);
                        }
                        else
                        {
                            result.IsOK = true;
                            result.Message = message;
                        }
                        results.Add(result);
                    });

                    if (!result.IsOK)
                    {
                        results.Add(result);
                        return results;
                    }
                }

                foreach (var domain in domains)
                {
                    var challengeConfig = managedCertificate.GetChallengeConfig(domain);

                    if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                    {
                        var challengeFileUrl = $"http://{domain}/.well-known/acme-challenge/configcheck";

                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem>{
                                    new AuthorizationChallengeItem
                                {
                                        ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP,
                                        ResourcePath =  ".well-known\\acme-challenge\\configcheck",
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
                    else if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                    {
                        var serverVersion = await serverManager.GetServerVersion();

                        if (serverVersion.Major < 8)
                        {
                            result.IsOK = false;
                            result.FailedItemSummary.Add($"The {SupportedChallengeTypes.CHALLENGE_TYPE_SNI} challenge is only available for IIS versions 8+.");
                            results.Add(result);

                            return results;
                        }

                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem> {
                                     new AuthorizationChallengeItem
                                     {
                                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_SNI,
                                          HashIterationCount= 1,
                                          Value = GenerateSimulatedKeyAuth()
                                     }
                                 }
                        };

                        generatedAuthorizations.Add(simulatedAuthorization);

                        result.IsOK =
                             PrepareChallengeResponse_TlsSni01(
                                log, serverManager, domain, managedCertificate, simulatedAuthorization
                            )();

                        results.Add(result);
                    }
                    else if (challengeConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    {
                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem> {
                                     new AuthorizationChallengeItem
                                     {
                                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                            Key=$"_acme-challenge-test.{domain}".Replace("*.", ""),
                                            Value = GenerateSimulatedKeyAuth()
                                     }
                                 }
                        };
                        generatedAuthorizations.Add(simulatedAuthorization);

                        var dnsResult =
                             await PerformChallengeResponse_Dns01(
                                log,
                                domain,
                                managedCertificate,
                                simulatedAuthorization
                            );

                        result.Message = dnsResult.Message;
                        result.IsOK = dnsResult.IsSuccess;

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
                //FIXME: needs to be filtered by managed site: result.Message = String.Join("\r\n", GetActionLogSummary());
                generatedAuthorizations.ForEach(ga => ga.Cleanup());
            }

            return results;
        }

        /// <summary>
        /// Creates a realistic-looking simulated Key Authorization 
        /// </summary>
        /// <remarks>
        /// example KeyAuthorization (from
        /// https://tools.ietf.org/html/draft-ietf-acme-acme-01#section-7.2):
        /// "evaGxfADs6pSRb2LAv9IZf17Dt3juxGJ-PCt92wr-oA.nP1qzpXGymHBrUEepNY9HCsQk7K8KhOypzEt62jcerQ"
        /// i.e. [token-string].[sha256(token-string bytes)] where token-string is &gt;= 128 bits of data
        /// </remarks>
        private string GenerateSimulatedKeyAuth()
        {
            // create simulated challenge

            var random = new Random();

            var simulated_token_data = new byte[24]; // generate 192 bits of data

            random.NextBytes(simulated_token_data);

            var simulated_token = Convert.ToBase64String(simulated_token_data);

            var sha256 = System.Security.Cryptography.SHA256.Create();

            byte[] thumbprint_data = sha256.ComputeHash(Encoding.UTF8.GetBytes(simulated_token));

            var thumbprint = BitConverter.ToString(thumbprint_data).Replace("-", "").ToLower();

            return $"{simulated_token}.{thumbprint}";
        }

        public async Task<PendingAuthorization> PerformAutomatedChallengeResponse(ILog log, ICertifiedServer iisManager, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedCertificate.RequestConfig;
            var domain = pendingAuth.Identifier.Dns;
            var challengeConfig = managedCertificate.GetChallengeConfig(domain);

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
                        var check = await PerformChallengeResponse_Http01(log, iisManager, domain, managedCertificate, pendingAuth);
                        if (requestConfig.PerformExtensionlessConfigChecks)
                        {
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = check.IsSuccess;
                        }
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                    {
                        // perform tls-sni-01 challenge response
                        var check = PrepareChallengeResponse_TlsSni01(log, iisManager, domain, managedCertificate, pendingAuth);
                        if (requestConfig.PerformTlsSniBindingConfigChecks)
                        {
                            // set config check OK if all checks return true
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = check();
                        }
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    {
                        // perform dns-01 challenge response
                        var check = await PerformChallengeResponse_Dns01(log, domain, managedCertificate, pendingAuth);
                        pendingAuth.AttemptedChallenge.ConfigCheckedOK = check.IsSuccess;
                        pendingAuth.AttemptedChallenge.ChallengeResultMsg = check.Message;
                    }
                }
            }
            return pendingAuth;
        }

        /// <summary>
        /// Prepares IIS to respond to a http-01 challenge 
        /// </summary>
        /// <returns> Test the challenge response locally. </returns>
        private async Task<ActionResult> PerformChallengeResponse_Http01(ILog log, ICertifiedServer iisManager, string domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedCertificate.RequestConfig;
            var httpChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP);

            if (httpChallenge == null)
            {
                var msg = $"No http challenge to complete for {managedCertificate.Name}. Request cannot continue.";
                log.Warning(msg);
                return new ActionResult { IsSuccess = false, Message = msg };
            }

            log.Information("Preparing challenge response for Let's Encrypt server to check at: {uri}", httpChallenge.ResourceUri);
            log.Information("If the challenge response file is not accessible at this exact URL the validation will fail and a certificate will not be issued.");

            // get website root path, expand environment variables if required
            var websiteRootPath = requestConfig.WebsiteRootPath;

            if (!string.IsNullOrEmpty(managedCertificate.ServerSiteId))
            {
                var siteInfo = await iisManager.GetSiteById(managedCertificate.ServerSiteId);

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

            log.Information("Using website path {path}", websiteRootPath);

            if (String.IsNullOrEmpty(websiteRootPath) || !Directory.Exists(websiteRootPath))
            {
                // our website no longer appears to exist on disk, continuing would potentially
                // create unwanted folders, so it's time for us to give up

                var msg = $"The website root path for {managedCertificate.Name} could not be determined. Request cannot continue.";
                log.Error(msg);
                return new ActionResult { IsSuccess = false, Message = msg };
            }

            // copy temp file to path challenge expects in web folder
            var destFile = Path.Combine(websiteRootPath, httpChallenge.ResourcePath);
            var destPath = Path.GetDirectoryName(destFile);

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
            if (!File.Exists(destFile))
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
            pendingAuth.Cleanup = () =>
            {
                if (!destFile.EndsWith("configcheck") && File.Exists(destFile))
                {
                    log.Verbose("Challenge Cleanup: Removing {file}", destFile);
                    try
                    {
                        File.Delete(destFile);
                    }
                    catch { }
                }
            };

            if (requestConfig.PerformExtensionlessConfigChecks)
            {
                // first check if it already works with no changes
                if (await _netUtil.CheckURL(log, httpChallenge.ResourceUri))
                {
                    return new ActionResult { IsSuccess = true, Message = $"Verified URL is accessible: {httpChallenge.ResourceUri}" };
                }

                // initial check didn't work, if auto config enabled attempt to find a working config
                if (requestConfig.PerformAutoConfig)
                {
                    // FIXME: need to only overwrite config we have auto populated, not user
                    //        specified config, compare to our preconfig and only overwrite if same
                    //        as ours? Or include preset key in our config, or make behaviour configurable
                    LogAction($"Pre-config check failed: Auto-config will overwrite existing config: {destPath}\\web.config");

                    var configOptions = Directory.EnumerateFiles(Environment.CurrentDirectory + "\\Scripts\\Web.config\\", "*.config");

                    foreach (var configFile in configOptions)
                    {
                        // create a web.config for extensionless files, then test it (make a request
                        // for the extensionless configcheck file over http)

                        var webConfigContent = File.ReadAllText(configFile);

                        // no existing config, attempt auto config and perform test
                        LogAction($"Testing config alternative: " + configFile);

                        try
                        {
                            System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);
                        }
                        catch (Exception exp)
                        {
                            this.LogAction($"Failed to write config: " + exp.Message);
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
                    Message = $"Config checks disabled. Could not verify URL: {httpChallenge.ResourceUri}"
                };
            }
        }

        private Func<bool> PrepareChallengeResponse_TlsSni01(ILog log, ICertifiedServer iisManager, string domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
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

            for (int i = 1; i < z.Length; i++)
            {
                z[i] = sha256.ComputeHash(z[i - 1]);
            }

            // generate certs and install iis bindings
            var cleanupQueue = new List<Action>();

            var checkQueue = new List<Func<bool>>();

            foreach (string hex in z.Select(b =>
                BitConverter.ToString(b).Replace("-", "").ToLower()))
            {
                var sni = $"{hex.Substring(0, 32)}.{hex.Substring(32)}.acme.invalid";

                log.Information($"Preparing binding at: https://{domain}, sni: {sni}");

                var x509 = CertificateManager.GenerateTlsSni01Certificate(sni);

                CertificateManager.StoreCertificate(x509);

                var certStoreName = CertificateManager.GetDefaultStore().Name;

                iisManager.InstallCertificateforBinding(certStoreName, x509.GetCertHash(), managedCertificate, sni);

                // add check to the queue
                checkQueue.Add(() => _netUtil.CheckSNI(domain, sni));

                // add cleanup actions to queue
                cleanupQueue.Add(() => iisManager.RemoveHttpsBinding(managedCertificate, sni));

                cleanupQueue.Add(() => CertificateManager.RemoveCertificate(x509));
            }

            // configure cleanup to execute the cleanup queue
            pendingAuth.Cleanup = () => cleanupQueue.ForEach(a => a());

            // perform our own config checks
            return () => checkQueue.All(check => check());
        }

        private async Task<ActionResult> PerformChallengeResponse_Dns01(ILog log, string domain, ManagedCertificate managedCertificate, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedCertificate.RequestConfig;

            var dnsChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);

            if (dnsChallenge == null)
            {
                var msg = $"No dns-01 challenge to complete for {managedCertificate.Name}. Request cannot continue.";

                log.Warning(msg);

                return new ActionResult
                {
                    IsSuccess = false,
                    Message = msg
                };
            }

            // create DNS records (manually or via automation)
            var dnsHelper = new DNSChallengeHelper();

            var dnsResult = await dnsHelper.CompleteDNSChallenge(log, managedCertificate, domain, dnsChallenge.Key, dnsChallenge.Value);

            if (!dnsResult.IsSuccess)
            {
                log.Error($"DNS update failed: Failed {dnsResult.Message}");
            }
            else
            {
                log.Information($"DNS updated OK : {dnsResult.Message}");
            }

            var cleanupQueue = new List<Action> { };

            // TODO: add cleanup actions to queue cleanupQueue.Add(() => remove temp txt record);

            // configure cleanup actions for use after challenge completes
            pendingAuth.Cleanup = () => cleanupQueue.ForEach(a => a());

            return dnsResult;
        }
    }
}
