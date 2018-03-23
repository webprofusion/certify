using Certify.Management;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Shared;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        /// <param name="iisManager"></param>
        /// <param name="managedSite"></param>
        /// <returns> APIResult </returns>
        /// <remarks>
        /// The purpose of this method is to test the options (permissions, configuration) before
        /// submitting a request to the ACME server, to avoid creating failed requests and hitting
        /// usage limits.
        /// </remarks>
        public async Task<StatusMessage> TestChallengeResponse(ILogger log, ICertifiedServer iisManager, ManagedSite managedSite, bool isPreviewMode, bool enableDnsChecks)
        {
            _actionLogs.Clear(); // reset action logs

            var requestConfig = managedSite.RequestConfig;
            var result = new StatusMessage { IsOK = true };
            var domains = new List<string> { requestConfig.PrimaryDomain };

            if (requestConfig.SubjectAlternativeNames != null)
            {
                domains.AddRange(requestConfig.SubjectAlternativeNames);
            }

            var generatedAuthorizations = new List<PendingAuthorization>();

            try
            {
                // if DNS checks enabled, attempt them here
                if (isPreviewMode && enableDnsChecks)
                {
                    log.Information("Performing preview DNS tests. {managedItem}", managedSite);

                    // check all domain configs
                    Parallel.ForEach(domains.Distinct(), new ParallelOptions
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
                    });
                    if (!result.IsOK)
                    {
                        return result;
                    }
                }

                if (requestConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                {
                    foreach (var domain in domains.Distinct())
                    {
                        string challengeFileUrl = $"http://{domain}/.well-known/acme-challenge/configcheck";

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

                        var resultOK = await PrepareChallengeResponse_Http01(
                           log, iisManager, domain, managedSite, simulatedAuthorization
                        )();

                        if (!resultOK)
                        {
                            result.IsOK = false;
                            result.FailedItemSummary.Add($"Config checks failed to verify http://{domain} is both publicly accessible and can serve extensionless files e.g. {challengeFileUrl}");

                            // don't check any more after first failure
                            break;
                        }
                    }
                }
                else if (requestConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                {
                    if (iisManager.GetServerVersion().Major < 8)
                    {
                        result.IsOK = false;
                        result.FailedItemSummary.Add($"The {SupportedChallengeTypes.CHALLENGE_TYPE_SNI} challenge is only available for IIS versions 8+.");
                        return result;
                    }

                    result.IsOK = domains.Distinct().All(domain =>
                    {
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
                        return PrepareChallengeResponse_TlsSni01(
                            log, iisManager, domain, managedSite, simulatedAuthorization
                        )();
                    });
                }
                else if (requestConfig.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                {
                    result.IsOK = domains.Distinct().All(domain =>
                    {
                        var simulatedAuthorization = new PendingAuthorization
                        {
                            Challenges = new List<AuthorizationChallengeItem> {
                                     new AuthorizationChallengeItem
                                     {
                                          ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                                            Key= "_acme-challenge-test."+domain,
                                            Value = GenerateSimulatedKeyAuth()
                                     }
                             }
                        };

                        generatedAuthorizations.Add(simulatedAuthorization);

                        return PrepareChallengeResponse_Dns01(
                            log,
                            domain,
                            managedSite,
                            simulatedAuthorization
                        )();
                    });
                }
                else
                {
                    throw new NotSupportedException($"ChallengeType not supported: {requestConfig.ChallengeType}");
                }
            }
            finally
            {
                //FIXME: needs to be filtered by managed site: result.Message = String.Join("\r\n", GetActionLogSummary());
                generatedAuthorizations.ForEach(ga => ga.Cleanup());
            }
            return result;
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
            string simulated_token = Convert.ToBase64String(simulated_token_data);
            var sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] thumbprint_data = sha256.ComputeHash(Encoding.UTF8.GetBytes(simulated_token));
            var thumbprint = BitConverter.ToString(thumbprint_data).Replace("-", "").ToLower();
            return $"{simulated_token}.{thumbprint}";
        }

        public async Task<PendingAuthorization> PerformAutomatedChallengeResponse(ILogger log, ICertifiedServer iisManager, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;
            var domain = pendingAuth.Identifier.Dns;
            if (pendingAuth.Challenges != null)
            {
                // from list of possible challenges, select the one we prefer to attempt
                var requiredChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == managedSite.RequestConfig.ChallengeType);

                if (requiredChallenge != null)
                {
                    pendingAuth.AttemptedChallenge = requiredChallenge;
                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP)
                    {
                        // perform http-01 challenge response
                        var check = PrepareChallengeResponse_Http01(log, iisManager, domain, managedSite, pendingAuth);
                        if (requestConfig.PerformExtensionlessConfigChecks)
                        {
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = await check();
                        }
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI)
                    {
                        // perform tls-sni-01 challenge response
                        var check = PrepareChallengeResponse_TlsSni01(log, iisManager, domain, managedSite, pendingAuth);
                        if (requestConfig.PerformTlsSniBindingConfigChecks)
                        {
                            // set config check OK if all checks return true
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = check();
                        }
                    }

                    if (requiredChallenge.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS)
                    {
                        // perform dns-01 challenge response FIXME:
                        var check = PrepareChallengeResponse_Dns01(log, domain, managedSite, pendingAuth);
                        /*if (requestConfig.PerformTlsSniBindingConfigChecks)
                        {
                            // set config check OK if all checks return true
                            pendingAuth.AttemptedChallenge.ConfigCheckedOK = check();
                        }*/
                    }
                }
            }
            return pendingAuth;
        }

        /// <summary>
        /// Prepares IIS to respond to a http-01 challenge 
        /// </summary>
        /// <returns>
        /// A Boolean returning Func. Invoke the Func to test the challenge response locally.
        /// </returns>
        private Func<Task<bool>> PrepareChallengeResponse_Http01(ILogger log, ICertifiedServer iisManager, string domain, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;
            var httpChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP);

            if (httpChallenge == null)
            {
                log.Warning($"No http challenge to complete for {managedSite.Name}. Request cannot continue.");
                return () => Task.FromResult(false);
            }

            log.Information("Preparing challenge response for Let's Encrypt server to check at: {uri}", httpChallenge.ResourceUri);
            log.Information("If the challenge response file is not accessible at this exact URL the validation will fail and a certificate will not be issued.");

            // get website root path, expand environment variables if required
            string websiteRootPath = requestConfig.WebsiteRootPath;

            // if website root path not specified, determine it now
            if (String.IsNullOrEmpty(websiteRootPath))
            {
                websiteRootPath = iisManager.GetSitePhysicalPath(managedSite);
            }

            if (!String.IsNullOrEmpty(websiteRootPath) && websiteRootPath.Contains("%"))
            {
                // if websiteRootPath contains %websiteroot% variable, replace that with the current
                // physical path for the site
                if (websiteRootPath.Contains("%websiteroot%"))
                {
                    // sets env variable for this process only
                    Environment.SetEnvironmentVariable("websiteroot", iisManager.GetSitePhysicalPath(managedSite));
                }
                // expand any environment variables present in site path
                websiteRootPath = Environment.ExpandEnvironmentVariables(websiteRootPath);
            }

            log.Information("Using website path {path}", websiteRootPath);

            if (String.IsNullOrEmpty(websiteRootPath) || !Directory.Exists(websiteRootPath))
            {
                // our website no longer appears to exist on disk, continuing would potentially
                // create unwanted folders, so it's time for us to give up
                log.Error($"The website root path for {managedSite.Name} could not be determined. Request cannot continue.");
                return () => Task.FromResult(false);
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
                    log.Error(exp, $"Pre-config check failed: Could not create directory: {destPath}");
                    return () => Task.FromResult(false);
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
                    log.Error(exp, $"Pre-config check failed: Could not create file: {destFile}");
                    return () => Task.FromResult(false);
                }
            }

            // configure cleanup - should this be configurable? Because in some case many sites
            // renewing may all point to the same web root, we keep the configcheck file
            pendingAuth.Cleanup = () =>
            {
                if (!destFile.EndsWith("configcheck") && File.Exists(destFile))
                {
                    log.Verbose("Challenge Cleanup: Removing {file}", destFile);
                    File.Delete(destFile);
                }
            };

            return async () =>
            {
                // first check if it already works with no changes
                if (await _netUtil.CheckURL(log, httpChallenge.ResourceUri))
                {
                    return true;
                }

                // initial check didn't work, if auto config enabled attempt to find a working config
                if (requestConfig.PerformAutoConfig)
                {
                    this.LogAction($"Pre-config check failed: Auto-config will overwrite existing config: {destPath}\\web.config");

                    var configOptions = Directory.EnumerateFiles(Environment.CurrentDirectory + "\\Scripts\\Web.config\\", "*.config");

                    foreach (var configFile in configOptions)
                    {
                        // create a web.config for extensionless files, then test it (make a request
                        // for the extensionless configcheck file over http)

                        string webConfigContent = File.ReadAllText(configFile);

                        // no existing config, attempt auto config and perform test
                        this.LogAction($"Testing config alternative: " + configFile);

                        try
                        {
                            System.IO.File.WriteAllText(destPath + "\\web.config", webConfigContent);
                        }
                        catch (Exception exp)
                        {
                            this.LogAction($"Failed to write config: " + exp.Message);
                        }

                        if (await _netUtil.CheckURL(log, $"http://{domain}/{httpChallenge.ResourcePath}"))
                        {
                            return true;
                        }
                    }

                    //couldn't auto configure
                    return false;
                }
                else
                {
                    // auto config not enabled, just have to fail
                    return false;
                }
            };
        }

        private Func<bool> PrepareChallengeResponse_TlsSni01(ILogger log, ICertifiedServer iisManager, string domain, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            var requestConfig = managedSite.RequestConfig;
            var tlsSniChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_SNI);

            if (tlsSniChallenge == null)
            {
                log.Warning($"No tls-sni-01 challenge to complete for {managedSite.Name}. Request cannot continue.");
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
                string sni = $"{hex.Substring(0, 32)}.{hex.Substring(32)}.acme.invalid";
                log.Information($"Preparing binding at: https://{domain}, sni: {sni}");

                var x509 = CertificateManager.GenerateTlsSni01Certificate(sni);
                CertificateManager.StoreCertificate(x509);
                var certStoreName = CertificateManager.GetDefaultStore().Name;

                iisManager.InstallCertificateforBinding(certStoreName, x509.GetCertHash(), managedSite, sni);

                // add check to the queue
                checkQueue.Add(() => _netUtil.CheckSNI(domain, sni));

                // add cleanup actions to queue
                cleanupQueue.Add(() => iisManager.RemoveHttpsBinding(managedSite, sni));
                cleanupQueue.Add(() => CertificateManager.RemoveCertificate(x509));
            }

            // configure cleanup to execute the cleanup queue
            pendingAuth.Cleanup = () => cleanupQueue.ForEach(a => a());

            // perform our own config checks
            return () => checkQueue.All(check => check());
        }

        private Func<bool> PrepareChallengeResponse_Dns01(ILogger log, string domain, ManagedSite managedSite, PendingAuthorization pendingAuth)
        {
            // TODO: make this async
            var requestConfig = managedSite.RequestConfig;
            var dnsChallenge = pendingAuth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);

            if (dnsChallenge == null)
            {
                log.Warning($"No dns-01 challenge to complete for {managedSite.Name}. Request cannot continue.");
                return () => false;
            }

            // create DNS records (manually or via automation)
            var dnsHelper = new DNSChallengeHelper();
            var helperResult = dnsHelper.CompleteDNSChallenge(log, managedSite, domain, dnsChallenge.Key, dnsChallenge.Value).Result;

            var cleanupQueue = new List<Action> { };
            var checkQueue = new List<Func<bool>> { };

            // add check to the queue checkQueue.Add(() => _netUtil.CheckDNS(domain, ));
            checkQueue.Add(() => { return helperResult.IsSuccess; });

            // TODO: add cleanup actions to queue cleanupQueue.Add(() => remove temp txt record);

            // configure cleanup actions for use after challenge completes
            pendingAuth.Cleanup = () => cleanupQueue.ForEach(a => a());

            // perform our config checks
            return () => checkQueue.All(check => check());
        }
    }
}