using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Microsoft.Extensions.Logging;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        /// <summary>
        /// Run general diagnostics, optionally fixing binding deployment
        /// </summary>
        /// <param name="autoFix">Attempt to re-apply current certificate</param>
        /// <param name="forceAutoDeploy">Change all deployment modes to Auto</param>
        public async Task RunCertDiagnostics(bool autoFix = false, bool forceAutoDeploy = false, bool includeOcspCheck = true)
        {
            static string stripNonNumericFromString(string input)
            {
                return new string(input.Where(c => char.IsDigit(c)).ToArray());
            }

            static bool isNumeric(string input)
            {
                return int.TryParse(input, out _);
            }

            var managedCertificates = await _certifyClient.GetManagedCertificates(new ManagedCertificateFilter());
            Console.ForegroundColor = ConsoleColor.White;
#if BINDING_CHECKS
            Console.WriteLine("Checking existing bindings..");

            var bindingConfig = Certify.Utils.Networking.GetCertificateBindings().Where(b => b.Port == 443);

            foreach (var b in bindingConfig)
            {
                Console.WriteLine($"{b.IP}:{b.Port}");
            }

            var dupeBindings = bindingConfig.GroupBy(x => x.IP + ":" + x.Port)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

            if (dupeBindings.Any())
            {
                foreach (var d in dupeBindings)
                {
                    Console.WriteLine($"Duplicate binding will fail:  {d}");
                }
            }
            else
            {
                Console.WriteLine("No duplicate IP:Port bindings identified.");
            }
#endif
            Console.WriteLine("Running cert diagnostics..");

            var countSiteIdsFixed = 0;
            var countBindingRedeployments = 0;
            var totalTime = Stopwatch.StartNew();
            var itemTiming = Stopwatch.StartNew();

            foreach (var site in managedCertificates)
            {

                itemTiming.Restart();

                var redeployRequired = false;

                if (autoFix)
                {
                    redeployRequired = true;
                }

                if ((site.GroupId != site.ServerSiteId) || !isNumeric(site.ServerSiteId))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\t WARNING: managed cert has invalid ServerSiteID: " + site.Name);
                    Console.ForegroundColor = ConsoleColor.White;

                    redeployRequired = true;

                    if (autoFix)
                    {

                        site.ServerSiteId = stripNonNumericFromString(site.ServerSiteId);
                        site.GroupId = site.ServerSiteId;
                        //update managed site
                        Console.WriteLine("\t Auto fixing managed cert ServerSiteID: " + site.Name);

                        var update = await _certifyClient.UpdateManagedCertificate(site);

                        countSiteIdsFixed++;
                    }
                }

                if (autoFix && forceAutoDeploy)
                {
                    redeployRequired = true;

                    if (site.RequestConfig.DeploymentSiteOption != DeploymentOption.Auto && site.RequestConfig.DeploymentSiteOption != DeploymentOption.AllSites)
                    {
                        Console.WriteLine("\t Auto fixing managed cert deployment mode: " + site.Name);
                        site.RequestConfig.DeploymentSiteOption = DeploymentOption.Auto;

                        var update = await _certifyClient.UpdateManagedCertificate(site);
                    }
                }

                if (!string.IsNullOrEmpty(site.CertificatePath) && System.IO.File.Exists(site.CertificatePath))
                {
                    Console.WriteLine($"{site.Name}");
                    var fileCert = CertificateManager.LoadCertificate(site.CertificatePath);

                    if (fileCert != null)
                    {
                        try
                        {
                            var storedCert = CertificateManager.GetCertificateByThumbprint(site.CertificateThumbprintHash);
                            if (storedCert != null)
                            {
                                // cert in store, check permissions
                                Console.WriteLine($"Stored cert :: " + storedCert.FriendlyName);
                                Console.WriteLine($"Signature Algorithm :: " + storedCert.SignatureAlgorithm.FriendlyName);

                                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                                {
                                    var access = CertificateManager.GetUserAccessInfoForCertificatePrivateKey(storedCert);
                                    foreach (System.Security.AccessControl.AuthorizationRule a in access.GetAccessRules(true, false, typeof(System.Security.Principal.NTAccount)))
                                    {
                                        Console.WriteLine("\t Access: " + a.IdentityReference.Value.ToString());
                                    }
                                }
                            }

                            if (includeOcspCheck)
                            {
                                var chainResults = CertificateManager.CheckCertChain(fileCert);

                                foreach (var result in chainResults)
                                {
                                    Console.WriteLine($"\t Cert Ocsp Status Check: {fileCert.Subject} " + result);
                                }

                                var ocspCheck = await CertificateManager.CheckOcspRevokedStatus(site.CertificatePath, "");
                                Console.ForegroundColor = ConsoleColor.White;

                                if (ocspCheck == Models.Certify.Models.CertificateStatusType.Revoked || ocspCheck == Models.Certify.Models.CertificateStatusType.Expired)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"\t Ocsp Status Check: {fileCert.Subject} " + ocspCheck);
                                    Console.ForegroundColor = ConsoleColor.White;
                                }
                                else
                                {
                                    Console.WriteLine($"\t Ocsp Status Check: {fileCert.Subject} " + ocspCheck);
                                }
                            }

                            // re-deploy certificate if possible
                            if (redeployRequired && autoFix)
                            {

                                //re-apply current certificate file to store and bindings
                                if (!string.IsNullOrEmpty(site.CertificateThumbprintHash))
                                {
                                    var result = await _certifyClient.ReapplyCertificateBindings(site.Id, false, false);

                                    countBindingRedeployments++;

                                    if (!result.IsSuccess)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("\t Error: Failed to re-applying certificate bindings:" + site.Name);
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("\t Info: re-applied certificate bindings:" + site.Name);
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }

                                    System.Threading.Thread.Sleep(500);
                                }
                                else
                                {

                                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                                    Console.WriteLine($"Warning: {site.Name} :: No certificate information, bindings cannot be redeployed");
                                    Console.ForegroundColor = ConsoleColor.White;

                                }
                            }
                        }
                        catch (Exception exp)
                        {
                            Console.WriteLine(exp.ToString());
                        }
                    }
                    else
                    {
                        //Console.WriteLine($"{site.Name} certificate file does not exist: {site.CertificatePath}");
                        if (redeployRequired)
                        {
                            Console.WriteLine($"{site.Name} has no current certificate and requires manual verification/redeploy of cert.");
                        }
                    }
                }

                Debug.WriteLine($"Item update took {itemTiming.Elapsed.TotalSeconds}s");
            }

            // TODO: get refresh of managed certs and for each current cert thumbprint, verify binding thumbprint match
            Debug.WriteLine($"Batch update took {totalTime.Elapsed.TotalSeconds}s. 500 items would take {((totalTime.Elapsed.TotalSeconds / managedCertificates.Count) * 500) / 60}mins");

            Console.WriteLine("-----------");
        }

        public async Task FindPendingAuthorizations(bool autoFix)
        {
            // scan log files for authz URLs, check status of each
            var logFolder = EnvironmentUtil.CreateAppDataPath("logs");
            var files = System.IO.Directory.GetFiles(logFolder, "log_*.txt");
            var orderUrls = new List<string>();

            foreach (var logFile in files)
            {
                try
                {
                    // Identify last N order URLs in each log file

                    System.Console.WriteLine("Parsing log: " + logFile);
                    var file = System.IO.File.ReadAllLines(logFile).Reverse();
                    var orderCount = 5;

                    foreach (var line in file)
                    {

                        if (line.Contains("https://acme-v02.api.letsencrypt.org/acme/order"))
                        {
                            var components = line.Split(' ');
                            var orderUrl = components.FirstOrDefault(c => c.Contains("https://"));
                            if (orderUrl != null)
                            {
                                orderUrls.Add(orderUrl.Trim());
                                orderCount--;
                            }
                        }

                        if (orderCount == 0)
                        {
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    System.Console.WriteLine("Could not parse order URLs from log: " + logFile);
                }
            }

            System.Console.WriteLine($"Orders to check for pending authz [{orderUrls.Count}]:");
            foreach (var url in orderUrls)
            {
                System.Console.WriteLine(url);
            }

            if (autoFix)
            {
                System.Console.WriteLine("Auto fixing:");

                // TODO: move this into certify manager and use client to call into service, removing dependency on certify core lib
                var c = new CertifyManager();
                await c.Init();

                var logger = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<CertifyCLI>());

                foreach (var url in orderUrls)
                {

                    System.Console.WriteLine("Checking Pending Challenges for " + url);

                    var dummyManagedCert = (new ManagedCertificate { CurrentOrderUri = url, UseStagingMode = false });
                    // get 
                    var caAccount = await c.GetAccountDetails(dummyManagedCert);
                    var acmeClient = await c.GetACMEProvider(dummyManagedCert, caAccount);

                    var pendingOrder = await acmeClient.BeginCertificateOrder(logger, dummyManagedCert, resumeExistingOrder: true);

                    foreach (var auth in pendingOrder.Authorizations)
                    {
                        try
                        {
                            if (!auth.IsFailure && !auth.IsValidated)
                            {
                                System.Console.WriteLine("Submitting challenge for validation " + auth.Identifier);
                                auth.AttemptedChallenge = auth.Challenges.FirstOrDefault(c => c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_HTTP || c.ChallengeType == SupportedChallengeTypes.CHALLENGE_TYPE_DNS);
                                await acmeClient.SubmitChallenge(logger, auth.AttemptedChallenge.ChallengeType, auth);
                            }
                        }
                        catch (Exception)
                        {
                            System.Console.WriteLine("Failed to complete pending authz for  " + url);

                        }

                        await Task.Delay(250);
                    }
                }
            }
        }
        public async Task RunCertMaintenanceTasks(string[] args)
        {
            System.Console.WriteLine("Performing managed certificate maintenance tasks..");

            string managedItemId = null;

            if (args.Length >= 2)
            {
                managedItemId = args[1];
            }

            var results = await _certifyClient.PerformManagedCertMaintenance(managedItemId);

            foreach (var result in results)
            {
                System.Console.WriteLine(result.Message);
            }
        }
    }
}
