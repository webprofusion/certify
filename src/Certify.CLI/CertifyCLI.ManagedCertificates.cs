using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {

        internal async Task PerformAutoRenew(string[] args)
        {
            var forceRenewal = false;

            var renewalMode = Models.RenewalMode.Auto;


            if (args.Contains("--force-renew-all"))
            {
                renewalMode = RenewalMode.All;
                forceRenewal = true;
            }

            if (args.Contains("--renew-witherrors"))
            {
                // renew errored items
                renewalMode = RenewalMode.RenewalsWithErrors;
            }

            if (args.Contains("--renew-newitems"))
            {
                // renew only new items
                renewalMode = RenewalMode.NewItems;
            }

            if (args.Contains("--renew-all-due"))
            {
                // renew only new items
                renewalMode = RenewalMode.RenewalsDue;
            }

            List<string> targetItemIds = new List<string> { };

            if (args.Any(a => a.StartsWith("id=")))
            {
                var idArg = args.FirstOrDefault(a => a.StartsWith("id="));
                if (idArg != null)
                {
                    var ids = idArg.Replace("id=", "").Split(',');
                    foreach (var id in ids)
                    {
                        targetItemIds.Add(id.Trim());
                    }
                }
            }

            var isPreviewMode = false;
            if (args.Contains("--preview"))
            {
                // don't perform real requests
                isPreviewMode = true;
            }

            if (_tc == null)
            {
                InitTelematics();
            }

            if (_tc != null)
            {
                _tc.TrackEvent("CLI_BeginAutoRenew");
            }

            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("\nPerforming Auto Renewals..\n");
            if (forceRenewal)
            {
                System.Console.WriteLine("\nForcing auto renew (--force-renewal-all specified). \n");
            }

            //go through list of items configured for auto renew, perform renewal and report the result
            var results = await _certifyClient.BeginAutoRenewal(new RenewalSettings { Mode = renewalMode, IsPreviewMode = isPreviewMode, TargetManagedCertificates = targetItemIds.Any() ? targetItemIds : null });

            Console.ForegroundColor = ConsoleColor.White;

            foreach (var r in results)
            {
                if (r.ManagedItem != null)
                {
                    System.Console.WriteLine("--------------------------------------");
                    if (r.IsSuccess)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        System.Console.WriteLine(r.ManagedItem.Name);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        System.Console.WriteLine(r.ManagedItem.Name);

                        if (r.Message != null)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            System.Console.WriteLine(r.Message);
                        }
                    }
                }
            }
            Console.ForegroundColor = ConsoleColor.White;

            System.Console.WriteLine("Completed:" + results.Where(r => r.IsSuccess == true).Count());
            if (results.Any(r => r.IsSuccess == false))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Failed:" + results.Where(r => r.IsSuccess == false).Count());
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        internal void ListManagedCertificates(string[] args)
        {
            var managedCertificates = _certifyClient.GetManagedCertificates(new ManagedCertificateFilter()).Result;

            // check for path argument and if present output json file
            var jsonArgIndex = Array.IndexOf(args, "--json");

            if (jsonArgIndex != -1)
            {
                // if we have a file argument, go ahead an export the list
                if (args.Length > (jsonArgIndex + 1))
                {
                    var pathArg = args[jsonArgIndex + 1];

                    try
                    {
                        var jsonOutput = JsonConvert.SerializeObject(managedCertificates, Formatting.Indented);

                        System.IO.File.WriteAllText(pathArg, jsonOutput);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine($"Failed to write output to file. Check folder exists and permissions allow write. " + pathArg);
                    }
                }
                else
                {
                    Console.WriteLine($"Output file path argument is required for json output.");
                }

            }
            else
            {
                // output list to console
                foreach (var site in managedCertificates)
                {
                    Console.ForegroundColor = ConsoleColor.White;

                    Console.WriteLine($"{site.Name},{site.DateExpiry},{site.Id},{site.Health.ToString()}");
                }
            }
        }
    }
}
