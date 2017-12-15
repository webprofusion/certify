using Certify.Client;
using Certify.Management;
using Certify.Models;
using Microsoft.ApplicationInsights;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.CLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var p = new CertifyCLI();

            p.ShowVersion();

            if (!p.IsServiceAvailable().Result)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Certify SSL Manager service not started.");
                Console.ForegroundColor = ConsoleColor.White;
                return -1;
            }

            Task.Run(async () =>
            {
                await p.LoadPreferences();
            });

            if (args.Length == 0)
            {
                p.ShowHelp();
                p.ShowACMEInfo();
            }
            else
            {
                p.ShowACMEInfo();

                if (args.Contains("renew", StringComparer.InvariantCultureIgnoreCase))
                {
                    // perform auto renew all
                    var renewalTask = p.PerformAutoRenew();
                    renewalTask.ConfigureAwait(true);
                    renewalTask.Wait();
                }

                if (args.Contains("list", StringComparer.InvariantCultureIgnoreCase))
                {
                    //list managed sites and status
                    p.ListManagedSites();
                }

                if (args.Contains("certdiag", StringComparer.InvariantCultureIgnoreCase))
                {
                    p.RunCertDiagnostics();
                }

                if (args.Contains("importcsv", StringComparer.InvariantCultureIgnoreCase))
                {
                    var importTask = p.ImportCSV(args);
                    importTask.ConfigureAwait(true);
                    importTask.Wait();
                }
            }

#if DEBUG
            Console.ReadKey();
#endif
            return 0;
        }
    }

    internal class CertifyCLI
    {
        private TelemetryClient _tc = null;
        private ICertifyClient _certifyClient = null;
        private Preferences _prefs = new Preferences();
        private PluginManager _pluginManager { get; set; }

        public CertifyCLI()
        {
            _certifyClient = new CertifyServiceClient();
        }

        public async Task<bool> IsServiceAvailable()
        {
            bool isAvailable = false;

            try
            {
                await _certifyClient.GetAppVersion();
                isAvailable = true;
            }
            catch (Exception)
            {
                isAvailable = false;
            }
            return isAvailable;
        }

        private void InitPlugins()
        {
            _pluginManager = new Management.PluginManager();

            _pluginManager.LoadPlugins();
        }

        private bool IsRegistered()
        {
            var licensingManager = _pluginManager.LicensingManager;
            if (licensingManager != null)
            {
                if (licensingManager.IsInstallRegistered(1, Certify.Management.Util.GetAppDataFolder()))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task LoadPreferences()
        {
            _prefs = await _certifyClient.GetPreferences();
        }

        private bool IsTelematicsEnabled()
        {
            return _prefs.EnableAppTelematics;
        }

        private string GetInstrumentationKey()
        {
            return Certify.Locales.ConfigResources.AIInstrumentationKey;
        }

        private async Task<string> GetAppVersion()
        {
            try
            {
                return await _certifyClient.GetAppVersion();
            }
            catch (Exception)
            {
                return await Task.FromResult("--- (Service Not Started)");
            }
        }

        private string GetAppWebsiteURL()
        {
            return Certify.Locales.ConfigResources.AppWebsiteURL;
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
                _tc.Context.Component.Version = GetAppVersion().Result;
                _tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
                _tc.TrackEvent("StartCLI");
            }
        }

        internal void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Certify SSL Manager - CLI v3.0.0. Certify.Core v" + GetAppVersion().Result);
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see " + GetAppWebsiteURL());
            System.Console.WriteLine("");
        }

        internal void ShowACMEInfo()
        {
            /*
                        var certifyManager = new CertifyManager();
                        string vaultInfo = certifyManager.GetVaultSummary();
                        string acmeInfo = certifyManager.GetAcmeSummary();

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        System.Console.WriteLine("Let's Encrypt ACME API: " + acmeInfo);
                        System.Console.WriteLine("ACMESharp Vault: " + vaultInfo);
            */
            System.Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.White;
        }

        internal void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("Usage: certify <command> \n");
            System.Console.WriteLine("certify renew : renew certificates for all auto renewed managed sites");
            System.Console.WriteLine("certify list : list managed sites and current running/not running status in IIS");

            System.Console.WriteLine("\n");
        }

        internal async Task PerformAutoRenew()
        {
            if (_tc == null) InitTelematics();
            if (_tc != null)
            {
                _tc.TrackEvent("CLI_BeginAutoRenew");
            }

            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("\nPerforming Auto Renewals..\n");

            //go through list of items configured for auto renew, perform renewal and report the result
            var results = await _certifyClient.BeginAutoRenewal();
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

        internal void ListManagedSites()
        {
            var managedSites = _certifyClient.GetManagedSites(new ManagedSiteFilter()).Result;

            foreach (var site in managedSites)
            {
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine($"{site.Name},{site.DateExpiry}");
            }
        }

        internal void RunCertDiagnostics()
        {
            var managedSites = _certifyClient.GetManagedSites(new ManagedSiteFilter()).Result;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Running cert diagnostics..");

            foreach (var site in managedSites)
            {
                if (!String.IsNullOrEmpty(site.CertificatePath))
                {
                    if (System.IO.File.Exists(site.CertificatePath))
                    {
                        Console.WriteLine($"{site.Name}");
                        var fileCert = CertificateManager.LoadCertificate(site.CertificatePath);

                        if (fileCert != null)
                        {
                            try
                            {
                                var storedCert = CertificateManager.GetCertificateFromStore(site.RequestConfig.PrimaryDomain);
                                if (storedCert != null)
                                {
                                    Console.WriteLine($"Stored cert " + storedCert.FriendlyName);
                                    var test = fileCert.PrivateKey.KeyExchangeAlgorithm;
                                    Console.WriteLine(test.ToString());

                                    var access = CertificateManager.GetUserAccessInfoForCertificatePrivateKey(storedCert);
                                    foreach (System.Security.AccessControl.AuthorizationRule a in access.GetAccessRules(true, false, typeof(System.Security.Principal.NTAccount)))
                                    {
                                        Console.WriteLine("Access: " + a.IdentityReference.Value.ToString());
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Stored cert not found");
                                }
                            }
                            catch (Exception exp)
                            {
                                Console.WriteLine(exp.ToString());
                            }
                        }
                    }
                    else
                    {
                        //Console.WriteLine($"{site.Name} certificate file does not exist: {site.CertificatePath}");
                    }
                }
            }

            Console.WriteLine("-----------");
        }

        internal void CheckCertAccess()
        {
        }

        internal async Task ImportCSV(string[] args)
        {
            InitPlugins();
            if (!IsRegistered())
            {
                Console.WriteLine("Import is only available in the registered version of this application.");
            }

            var filename = args[args.Length - 1];

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Importing CSV: " + filename);

            var currentManagedSites = await _certifyClient.GetManagedSites(new ManagedSiteFilter() { });
            var rows = System.IO.File.ReadAllLines(filename);
            foreach (var row in rows)
            {
                // SiteId, Name, Domain;Domain2;Domain3
                string[] values = row.Split(',');
                string siteId = values[0].Trim();
                string siteName = values[1].Trim();
                string[] domains = values[2].Trim().Split(';');

                var newManagedSite = new ManagedSite();
                newManagedSite.Id = Guid.NewGuid().ToString();
                newManagedSite.GroupId = siteId;
                newManagedSite.Name = siteName;
                newManagedSite.IncludeInAutoRenew = true;
                newManagedSite.ItemType = ManagedItemType.SSL_LetsEncrypt_LocalIIS;
                newManagedSite.RequestConfig.ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP;
                newManagedSite.RequestConfig.PerformAutoConfig = true;
                newManagedSite.RequestConfig.PerformChallengeFileCopy = true;
                newManagedSite.RequestConfig.PerformExtensionlessConfigChecks = true;
                newManagedSite.RequestConfig.PerformTlsSniBindingConfigChecks = true;
                newManagedSite.RequestConfig.PerformAutomatedCertBinding = true;
                newManagedSite.RequestConfig.EnableFailureNotifications = true;

                bool isPrimaryDomain = true;
                List<string> sans = new List<string>();
                foreach (var d in domains)
                {
                    if (!String.IsNullOrWhiteSpace(d))
                    {
                        newManagedSite.DomainOptions.Add(new DomainOption { Domain = d.Trim(), IsPrimaryDomain = isPrimaryDomain, IsSelected = true, Title = d });

                        if (isPrimaryDomain)
                        {
                            newManagedSite.RequestConfig.PrimaryDomain = d.Trim();
                        }

                        sans.Add(d.Trim());

                        isPrimaryDomain = false;
                    }
                }
                newManagedSite.RequestConfig.SubjectAlternativeNames = sans.ToArray();

                // TODO:check for dupes

                // add managed site
                Console.WriteLine("Creating Managed Site: " + newManagedSite.Name);
                await _certifyClient.UpdateManagedSite(newManagedSite);
            }
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
            // -Alias test7_examplesite_co_uk636213616564101276 -Label Identifier:test7.examplesite.co.uk
            // Complete-ACMEChallenge -Ref test7_examplesite_co_uk636213616564101276 -ChallengeType
            // http-01 -Handler manual
            // -Regenerate Submit-ACMEChallenge -Ref test7_examplesite_co_uk636213616564101276
            // -Challenge http-01 Update-ACMEIdentifier -Ref test7_examplesite_co_uk636213616564101276
            // Update-ACMEIdentifier -Ref test7_examplesite_co_uk636213616564101276 New-ACMECertificate
            // -Identifier test7_examplesite_co_uk636213616564101276 -Alias
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