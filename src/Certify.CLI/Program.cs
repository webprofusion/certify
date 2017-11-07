using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.ApplicationInsights;
using Certify.Client;
using Certify.Models;

namespace Certify.CLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            // upgrade assembly version of saved settings (if required)
#if DIRECTCLIENT
            Certify.Properties.Settings.Default.UpgradeSettingsVersion(); // deprecated
            Certify.Management.SettingsManager.LoadAppSettings();
#endif

            var p = new CertifyCLI();
            p.ShowVersion();

            if (args.Length == 0)
            {
                p.ShowHelp();
                p.ShowACMEInfo();
            }
            else
            {
                p.ShowACMEInfo();

                if (args.Contains("cleanup", StringComparer.InvariantCultureIgnoreCase))
                {
                    // cleanup vault
                    p.PerformVaultCleanup();
                }

                if (args.Contains("renew", StringComparer.InvariantCultureIgnoreCase))
                {
                    // perform auto renew all
                    var renewalTask = p.PerformAutoRenew();
                    renewalTask.ConfigureAwait(true);
                    renewalTask.Wait();

                    // now perform vault cleanup
                    p.PerformVaultCleanup();
                }

                if (args.Contains("list", StringComparer.InvariantCultureIgnoreCase))
                {
                    //list managed sites and status
                    p.ListManagedSites();
                }
            }

#if DEBUG
            p.ListManagedSites();
            Console.ReadKey();
#endif
            return 0;
        }
    }

    internal class CertifyCLI
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();
        private TelemetryClient _tc = null;
        private ICertifyClient _certifyClient = null;

        public CertifyCLI()
        {
#if DIRECTCLIENT
            _certifyClient = new CertifyDirectClient();
#else
            _certifyClient = new CertifyServiceClient();
#endif
        }

        private bool IsTelematicsEnabled()
        {
            return false;
            //return  CoreAppSettings.Current.EnableAppTelematics
        }

        private string GetInstrumentationKey()
        {
            return "";
            //return  Certify.Properties.Resources.AIInstrumentationKey;
        }

        private string GetAppVersion()
        {
            string version = _certifyClient.GetAppVersion().Result;
            return version;
        }

        private string GetAppWebsiteURL()
        {
            return "https://certifytheweb.com";
            // return Certify.Locales.CoreSR.Ap
            //return Certify.Properties.Resources.AppWebsiteURL;
        }

        private void InitTelematics()
        {
            if (IsTelematicsEnabled())
            {
                _tc = new TelemetryClient();
                _tc.Context.InstrumentationKey = GetInstrumentationKey();
                _tc.InstrumentationKey = GetInstrumentationKey();

                // Set session data:

                _tc.Context.Session.Id = Guid.NewGuid().ToString();
                _tc.Context.Component.Version = GetAppVersion();
                _tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
                _tc.TrackEvent("StartCLI");
            }
        }

        internal void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Certify SSL Manager - CLI v1.1.0. Certify.Core v" + GetAppVersion());
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see " + GetAppWebsiteURL());
            System.Console.WriteLine("");
        }

        internal void PerformVaultCleanup()
        {
            System.Console.WriteLine("Beginning Vault Cleanup..");
#if DIRECTCLIENT
            //var certifyManager = new CertifyManager();
            certifyManager.PerformVaultCleanup();
#endif

            System.Console.WriteLine("Completed Vault Cleanup..");
        }

        internal void ShowACMEInfo()
        {
#if DIRECTCLIENT
            var certifyManager = new CertifyManager();
            string vaultInfo = certifyManager.GetVaultSummary();
            string acmeInfo = certifyManager.GetAcmeSummary();

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.WriteLine("Let's Encrypt ACME API: " + acmeInfo);
            System.Console.WriteLine("ACMESharp Vault: " + vaultInfo);
#endif
            System.Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.White;
        }

        internal void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("Usage: certify <command> \n");
            System.Console.WriteLine("certify renew : renew certificates for all auto renewed managed sites");
            System.Console.WriteLine("certify list : list managed sites and current running/not running status in IIS");
            System.Console.WriteLine("certify cleanup : cleanup vault entries");

            System.Console.WriteLine("\n");
        }

        internal async Task<System.Collections.Generic.List<CertificateRequestResult>> PerformAutoRenew()
        {
            if (_tc == null) InitTelematics();
            if (_tc != null)
            {
                _tc.TrackEvent("CLI_BeginAutoRenew");
            }

            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("\nPerforming Auto Renewals..\n");

            //go through list of items configured for auto renew, perform renewal and report the result
            var results = await _certifyClient.PerformRenewalAllManagedSites();

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
            return results;
        }

        internal void ListManagedSites()
        {
            var managedSites = _certifyClient.GetManagedSites(null, 1000).Result;

            foreach (var site in managedSites)
            {
                string status = "Status Not Polled";
                /* var siteIISInfo = iisManager.GetSiteById(site.GroupId);
                 string status = "Running";
                 if (!iisManager.IsSiteRunning(site.GroupId))
                 {
                     status = "Not Running";
                 }*/

                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine($"{site.Name},{status},{site.DateExpiry}");
            }
#if DIRECTCLIENT
            var siteManager = new ItemManager();
            siteManager.LoadSettings();

            var managedSites = siteManager.GetManagedSites();
            IISManager iisManager = new IISManager();
            foreach (var site in managedSites)
            {
                var siteIISInfo = iisManager.GetSiteById(site.GroupId);
                string status = "Running";
                if (!iisManager.IsSiteRunning(site.GroupId))
                {
                    status = "Not Running";
                }
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine($"{site.Name},{status},{site.DateExpiry}");
            }
#endif
        }

        /*
       private bool PerformCertRequestAndIISBinding(string certDomain, string[] alternativeNames)
       {
           // ACME service requires international domain names in ascii mode
           certDomain = _idnMapping.GetAscii(certDomain);

            //create cert and binding it

            //Typical command sequence for a new certificate

            //Initialize-ACMEVault -BaseURI https://acme-staging.api.letsencrypt.org/

            // Get-Module -ListAvailable ACMESharp New-ACMEIdentifier -Dns test7.examplesite.co.uk
            // -Alias test7_examplesite_co_uk636213616564101276 -Label
            // Identifier:test7.examplesite.co.uk Complete-ACMEChallenge -Ref
            // test7_examplesite_co_uk636213616564101276 -ChallengeType http-01 -Handler manual
            // -Regenerate Submit-ACMEChallenge -Ref test7_examplesite_co_uk636213616564101276
            // -Challenge http-01 Update-ACMEIdentifier -Ref
            // test7_examplesite_co_uk636213616564101276 Update-ACMEIdentifier -Ref
            // test7_examplesite_co_uk636213616564101276 New-ACMECertificate -Identifier
            // test7_examplesite_co_uk636213616564101276 -Alias
            // cert_test7_examplesite_co_uk636213616564101276 -Generate Update-ACMEIdentifier -Ref
            // test7_examplesite_co_uk636213616564101276 Update-ACMEIdentifier -Ref
            // test7_examplesite_co_uk636213616564101276 Get-ACMECertificate -Ref = ac22dbfe - b75f
            // - 4cac-9247-b40c1d9bf9eb -ExportPkcs12
            // C:\ProgramData\ACMESharp\sysVault\99-ASSET\ac22dbfe-b75f-4cac-9247-b40c1d9bf9eb-all.pfx -Overwrite

            //get info on existing IIS site we want to create/update SSL binding for
            IISManager iisManager = new IISManager();
            var iisSite = iisManager.GetSiteBindingByDomain(certDomain);
            var certConfig = new CertRequestConfig()
            {
                PrimaryDomain = certDomain,
                PerformChallengeFileCopy = true,
                WebsiteRootPath = Environment.ExpandEnvironmentVariables(iisSite.PhysicalPath)
            };

            var certifyManager = new VaultManager(Properties.Settings.Default.VaultPath, LocalDiskVault.VAULT);

            //init vault if not already created
            certifyManager.InitVault(staging: true);

            //domain alias is used as an ID in both the vault and the LE server, it's specific to one authorization attempt and cannot be reused for renewal
            var domainIdentifierAlias = certifyManager.ComputeIdentifierAlias(certDomain);

            //NOTE: to support a SAN certificate (multiple alternative domains on one site) the domain validation steps need to be repeat for each name:

            //register identifier with LE, get http challenge spec back
            //create challenge response answer file under site .well-known, auto configure web.config for extenstionless content, mark challenge prep completed
            var authState = certifyManager.BeginRegistrationAndValidation(certConfig, domainIdentifierAlias);

            //ask LE to check our answer to their authorization challenge (http), LE will then attempt to fetch our answer, if all accessible and correct (authorized) LE will then allow us to request a certificate
            if (authState.Identifier.Authorization.IsPending())
            {
                //prepare IIS with answer for the LE challenege
                certifyManager.PerformIISAutomatedChallengeResponse(certConfig, authState);

                //ask LE to validate our challenge response
                certifyManager.SubmitChallenge(domainIdentifierAlias, "http-01");
            }

            //now check if LE has validated our challenge answer
            bool validated = certifyManager.CompleteIdentifierValidationProcess(domainIdentifierAlias);

            if (validated)
            {
                var certRequestResult = certifyManager.PerformCertificateRequestProcess(domainIdentifierAlias, alternativeIdentifierRefs: null);
                if (certRequestResult.IsSuccess)
                {
                    string pfxPath = certRequestResult.Result.ToString();
                    //Install certificate into certificate store and bind to IIS site
                    //TODO, match by site id?
                    if (iisManager.InstallCertForDomain(certDomain, pfxPath, cleanupCertStore: true, skipBindings: false))
                    {
                        //all done
                        System.Diagnostics.Debug.WriteLine("Certificate installed and SSL bindings updated for " + certDomain);
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to install PFX file for Certificate.");
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("LE did not issue a valid certificate in the time allowed.");
                    return false;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Validation of the required challenges did not complete successfully.");
                return false;
            }

           return false;
       }
       */
    }
}