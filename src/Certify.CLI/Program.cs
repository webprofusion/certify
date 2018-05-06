using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Microsoft.ApplicationInsights;

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
                    p.ListManagedCertificates();
                }

                if (args.Contains("diag", StringComparer.InvariantCultureIgnoreCase))
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
            System.Console.WriteLine("certify list : list managed certificates and current running/not running status in IIS");
            System.Console.WriteLine("certify diag : check existing ssl bindings and managed certificate integrity");
            System.Console.WriteLine("certify importcsv : import managed certificates from a CSV file.");
            System.Console.WriteLine("\n\n");
            System.Console.WriteLine("\n\n");
            System.Console.WriteLine("For help, see the docs at https://docs.certifytheweb.com");

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

        internal void ListManagedCertificates()
        {
            var managedCertificates = _certifyClient.GetManagedCertificates(new ManagedCertificateFilter()).Result;

            foreach (var site in managedCertificates)
            {
                Console.ForegroundColor = ConsoleColor.White;

                Console.WriteLine($"{site.Name},{site.DateExpiry}");
            }
        }

        internal void RunCertDiagnostics()
        {
            var managedCertificates = _certifyClient.GetManagedCertificates(new ManagedCertificateFilter()).Result;
            Console.ForegroundColor = ConsoleColor.White;

            Console.WriteLine("Checking existing bindings..");

            var bindingConfig = Certify.Utils.Networking.GetCertificateBindings();

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

            Console.WriteLine("Running cert diagnostics..");

            foreach (var site in managedCertificates)
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
                                    Console.WriteLine($"Stored cert :: " + storedCert.FriendlyName);
                                    var test = fileCert.PrivateKey.KeyExchangeAlgorithm;
                                    Console.WriteLine(test.ToString());

                                    var access = CertificateManager.GetUserAccessInfoForCertificatePrivateKey(storedCert);
                                    foreach (System.Security.AccessControl.AuthorizationRule a in access.GetAccessRules(true, false, typeof(System.Security.Principal.NTAccount)))
                                    {
                                        Console.WriteLine("\t Access: " + a.IdentityReference.Value.ToString());
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"{site.RequestConfig.PrimaryDomain} :: Stored cert not found");
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
                //return;
            }

            var filename = args[args.Length - 1];

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Importing CSV: " + filename);

            var currentManagedCertificates = await _certifyClient.GetManagedCertificates(new ManagedCertificateFilter() { });
            var rows = System.IO.File.ReadAllLines(filename);
            var csvHasHeaders = false;
            int rowID = 0;

            // set default column index values
            int? siteIdIdx = 0,
                 nameIdx = 1,
                 domainsIdx = 2,
                 primaryDomainIdx = null,
                 includeInAutoRenewIdx = null,
                 performAutoConfigIdx = null,
                 performChallengeFileCopyIdx = null,
                 performExtensionlessConfigChecksIdx = null,
                 performTlsSniBindingConfigChecksIdx = null,
                 performAutomatedCertBindingIdx = null,
                 enableFailureNotificationsIdx = null,
                 preRequestPowerShellScriptIdx = null,
                 postRequestPowerShellScriptIdx = null,
                 webhookTriggerIdx = null,
                 webhookMethodIdx = null,
                 webhookUrlIdx = null,
                 webhookContentTypeIdx = null,
                 webhookContentBodyIdx = null;

            foreach (var row in rows)
            {
                try
                {
                    // does the first row contain headers in the csv file?
                    if ((rowID == 0) && row.Contains("siteid") && row.Contains("domains"))
                    {
                        csvHasHeaders = true;
                    }

                    // first row contains headers, we need to figure out the position of each column
                    if ((rowID == 0) && csvHasHeaders)
                    {
                        string[] columnTitles = row.Split(',');
                        int colID = 0;

                        foreach (var title in columnTitles)
                        {
                            // because we never know how people are going to put data in the csv,
                            // convert titles to lowercase before searching for the column index
                            var cleanTitle = title.Trim().ToLower();

                            // set the column ids
                            switch (cleanTitle)
                            {
                                case "siteid":
                                    siteIdIdx = colID;
                                    break;

                                case "name":
                                    nameIdx = colID;
                                    break;

                                case "domains":
                                    domainsIdx = colID;
                                    break;

                                case "primarydomain":
                                    primaryDomainIdx = colID;
                                    break;

                                case "includeinautorenew":
                                    includeInAutoRenewIdx = colID;
                                    break;

                                case "performautoconfig":
                                    performAutoConfigIdx = colID;
                                    break;

                                case "performchallengefilecopy":
                                    performChallengeFileCopyIdx = colID;
                                    break;

                                case "performextensionlessconfigchecks":
                                    performExtensionlessConfigChecksIdx = colID;
                                    break;

                                case "performtlssnibindingconfigchecks":
                                    performTlsSniBindingConfigChecksIdx = colID;
                                    break;

                                case "performautomatedcertbinding":
                                    performAutomatedCertBindingIdx = colID;
                                    break;

                                case "enablefailurenotifications":
                                    enableFailureNotificationsIdx = colID;
                                    break;

                                case "prerequestpowershellscript":
                                    preRequestPowerShellScriptIdx = colID;
                                    break;

                                case "postrequestpowershellscript":
                                    postRequestPowerShellScriptIdx = colID;
                                    break;

                                case "webhooktrigger":
                                    webhookTriggerIdx = colID;
                                    break;

                                case "webhookmethod":
                                    webhookMethodIdx = colID;
                                    break;

                                case "webhookurl":
                                    webhookUrlIdx = colID;
                                    break;

                                case "webhookcontenttype":
                                    webhookContentTypeIdx = colID;
                                    break;

                                case "webhookcontentbody":
                                    webhookContentBodyIdx = colID;
                                    break;
                            }

                            colID++;
                        }
                    }
                    else
                    {
                        // required fields SiteId, Name, Domain;Domain2;Domain3
                        string[] values = Regex.Split(row, @",(?![^\{]*\})"); // get all values separated by commas except those found between {}
                        string siteId = values[(int)siteIdIdx].Trim();
                        string siteName = values[(int)nameIdx].Trim();
                        string[] domains = values[(int)domainsIdx].Trim().Split(';');

                        // optional fields
                        bool IncludeInAutoRenew = true,
                             PerformAutoConfig = true,
                             PerformChallengeFileCopy = true,
                             PerformExtensionlessConfigChecks = true,
                             PerformTlsSniBindingConfigChecks = true,
                             PerformAutomatedCertBinding = true,
                             EnableFailureNotifications = true;
                        string primaryDomain = "",
                               PreRequestPowerShellScript = "",
                               PostRequestPowerShellScript = "",
                               WebhookTrigger = Webhook.ON_NONE,
                               WebhookMethod = "",
                               WebhookUrl = "",
                               WebhookContentType = "",
                               WebhookContentBody = "";

                        if (primaryDomainIdx != null) primaryDomain = values[(int)primaryDomainIdx].Trim();
                        if (includeInAutoRenewIdx != null) IncludeInAutoRenew = Convert.ToBoolean(values[(int)includeInAutoRenewIdx].Trim());
                        if (performAutoConfigIdx != null) PerformAutoConfig = Convert.ToBoolean(values[(int)performAutoConfigIdx].Trim());
                        if (performChallengeFileCopyIdx != null) PerformChallengeFileCopy = Convert.ToBoolean(values[(int)performChallengeFileCopyIdx].Trim());
                        if (performExtensionlessConfigChecksIdx != null) PerformExtensionlessConfigChecks = Convert.ToBoolean(values[(int)performExtensionlessConfigChecksIdx].Trim());
                        if (performTlsSniBindingConfigChecksIdx != null) PerformTlsSniBindingConfigChecks = Convert.ToBoolean(values[(int)performTlsSniBindingConfigChecksIdx].Trim());
                        if (performAutomatedCertBindingIdx != null) PerformAutomatedCertBinding = Convert.ToBoolean(values[(int)performAutomatedCertBindingIdx].Trim());
                        if (enableFailureNotificationsIdx != null) EnableFailureNotifications = Convert.ToBoolean(values[(int)enableFailureNotificationsIdx].Trim());
                        if (preRequestPowerShellScriptIdx != null) PreRequestPowerShellScript = values[(int)preRequestPowerShellScriptIdx].Trim();
                        if (postRequestPowerShellScriptIdx != null) PostRequestPowerShellScript = values[(int)postRequestPowerShellScriptIdx].Trim();
                        if (webhookTriggerIdx != null)
                        {
                            WebhookTrigger = values[(int)webhookTriggerIdx].Trim();

                            // the webhook trigger text is case sensitive
                            switch (WebhookTrigger.ToLower())
                            {
                                case "none":
                                    WebhookTrigger = Webhook.ON_NONE;
                                    break;

                                case "on success":
                                    WebhookTrigger = Webhook.ON_SUCCESS;
                                    break;

                                case "on error":
                                    WebhookTrigger = Webhook.ON_ERROR;
                                    break;

                                case "on success or error":
                                    WebhookTrigger = Webhook.ON_SUCCESS_OR_ERROR;
                                    break;
                            }

                            if (webhookMethodIdx != null)
                            {
                                var tmpWebhookMethod = values[(int)webhookMethodIdx].Trim();
                                WebhookMethod = tmpWebhookMethod.ToUpper();

                                if (WebhookMethod == "POST")
                                {
                                    if (webhookUrlIdx != null)
                                    {
                                        WebhookContentType = values[(int)webhookContentTypeIdx].Trim();
                                    }

                                    if (webhookContentBodyIdx != null)
                                    {
                                        WebhookContentBody = values[(int)webhookContentBodyIdx].Trim();

                                        // cleanup json values from csv conversion
                                        WebhookContentBody = Regex.Replace(WebhookContentBody, @"(""|'')|(""|'')", "");
                                        WebhookContentBody = WebhookContentBody.Replace("\"\"", "\"");
                                    }
                                }
                            }

                            if (webhookUrlIdx != null) WebhookUrl = values[(int)webhookUrlIdx].Trim();
                        }

                        var newManagedCertificate = new ManagedCertificate();
                        newManagedCertificate.Id = Guid.NewGuid().ToString();
                        newManagedCertificate.GroupId = siteId;
                        newManagedCertificate.Name = siteName;
                        newManagedCertificate.IncludeInAutoRenew = IncludeInAutoRenew;
                        newManagedCertificate.ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS;
                        newManagedCertificate.RequestConfig.Challenges = new System.Collections.ObjectModel.ObservableCollection<CertRequestChallengeConfig>(
                            new List<CertRequestChallengeConfig> {
                                new CertRequestChallengeConfig {
                                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP
                            }
                        });
                        newManagedCertificate.RequestConfig.PerformAutoConfig = PerformAutoConfig;
                        newManagedCertificate.RequestConfig.PerformChallengeFileCopy = PerformChallengeFileCopy;
                        newManagedCertificate.RequestConfig.PerformExtensionlessConfigChecks = PerformExtensionlessConfigChecks;
                        newManagedCertificate.RequestConfig.PerformTlsSniBindingConfigChecks = PerformTlsSniBindingConfigChecks;
                        newManagedCertificate.RequestConfig.PerformAutomatedCertBinding = PerformAutomatedCertBinding;
                        newManagedCertificate.RequestConfig.EnableFailureNotifications = EnableFailureNotifications;
                        newManagedCertificate.RequestConfig.PreRequestPowerShellScript = PreRequestPowerShellScript;
                        newManagedCertificate.RequestConfig.PostRequestPowerShellScript = PostRequestPowerShellScript;
                        newManagedCertificate.RequestConfig.WebhookTrigger = WebhookTrigger;
                        newManagedCertificate.RequestConfig.WebhookMethod = WebhookMethod;
                        newManagedCertificate.RequestConfig.WebhookUrl = WebhookUrl;
                        newManagedCertificate.RequestConfig.WebhookContentType = WebhookContentType;
                        newManagedCertificate.RequestConfig.WebhookContentBody = WebhookContentBody;

                        bool isPrimaryDomain = true;

                        // if we have passed in a primary domain into the csv file, use that instead
                        // of the first domain in the list
                        if (primaryDomain != "")
                        {
                            isPrimaryDomain = false;
                        }

                        List<string> sans = new List<string>();
                        foreach (var d in domains)
                        {
                            if (!String.IsNullOrWhiteSpace(d))
                            {
                                var cleanDomainName = d.Trim();

                                if ((isPrimaryDomain) || (cleanDomainName == primaryDomain.Trim()))
                                {
                                    newManagedCertificate.RequestConfig.PrimaryDomain = cleanDomainName;
                                    isPrimaryDomain = true;
                                }

                                bool sanExists = false;

                                // check for existing SAN entry
                                foreach (var site in currentManagedCertificates)
                                {
                                    if (!sanExists)
                                    {
                                        var filtered = site.DomainOptions.Where(options => options.Domain == cleanDomainName);

                                        if (filtered.Count() > 0)
                                        {
                                            Console.WriteLine("Processing Row: " + rowID + " - Domain entry (" + cleanDomainName + ") already exists in certificate (" + site.Name + ")");
                                            sanExists = true;
                                        }
                                    }
                                }

                                // if the current san entry doesn't exist in our certificate list,
                                // let's add it
                                if (!sanExists)
                                {
                                    newManagedCertificate.DomainOptions.Add(new DomainOption { Domain = cleanDomainName, IsPrimaryDomain = isPrimaryDomain, IsSelected = true, Title = d });

                                    sans.Add(cleanDomainName);
                                }

                                isPrimaryDomain = false;
                            }
                        }

                        // if the new certificate to be imported has sans, then add the certificate
                        // request to the system
                        if (sans.Count() > 0)
                        {
                            newManagedCertificate.RequestConfig.SubjectAlternativeNames = sans.ToArray();

                            // add managed site
                            Console.WriteLine("Creating Managed Certificate: " + newManagedCertificate.Name);
                            await _certifyClient.UpdateManagedCertificate(newManagedCertificate);

                            // add the new certificate request to our in-memory list
                            currentManagedCertificates.Add(newManagedCertificate);
                        }
                    }
                }
                catch (Exception exp)
                {
                    Console.WriteLine("There was a problem importing row " + rowID + " - " + exp.ToString());
                }

                rowID++;
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
