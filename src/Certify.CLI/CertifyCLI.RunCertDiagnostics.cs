using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;

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
            // TODO: this should all move to the core service and be called via the client API
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
            var totalTime = Stopwatch.StartNew();
            var itemTiming = Stopwatch.StartNew();

            foreach (var site in managedCertificates)
            {

                itemTiming.Restart();

                if ((site.GroupId != site.ServerSiteId) || !isNumeric(site.ServerSiteId))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\t WARNING: managed cert has invalid ServerSiteID: " + site.Name);
                    Console.ForegroundColor = ConsoleColor.White;

                    if (autoFix)
                    {

                        site.ServerSiteId = stripNonNumericFromString(site.ServerSiteId);
                        site.GroupId = site.ServerSiteId;
                        //update managed site
                        Console.WriteLine("\t Auto fixing managed cert ServerSiteID: " + site.Name);

                        await _certifyClient.UpdateManagedCertificate(site);

                        countSiteIdsFixed++;
                    }
                }

                if (autoFix && forceAutoDeploy)
                {
                    if (site.RequestConfig.DeploymentSiteOption != DeploymentOption.Auto && site.RequestConfig.DeploymentSiteOption != DeploymentOption.AllSites)
                    {
                        Console.WriteLine("\t Auto fixing managed cert deployment mode: " + site.Name);
                        site.RequestConfig.DeploymentSiteOption = DeploymentOption.Auto;

                        await _certifyClient.UpdateManagedCertificate(site);
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

            await Task.CompletedTask;
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
