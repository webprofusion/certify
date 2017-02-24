using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACMESharp.Vault.Providers;
using Certify.Management;
using Certify.Models;
using Newtonsoft.Json;

namespace Certify.CLI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowVersion();
                ShowHelp();

                var p = new Program();
                //p.PreviewAutoManage();
                //p.PerformVaultCleanup();
                System.Console.ReadKey();
                return 1;
            }

            return 0;
        }

        private readonly IdnMapping _idnMapping = new IdnMapping();

        private void PerformVaultCleanup()
        {
            var vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, LocalDiskVault.VAULT);

            //init vault if not already created
            vaultManager.InitVault(staging: true);

            vaultManager.CleanupVault();
        }

        private static void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Certify SSL Manager - CLI v1.0.0");
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see https://certify.webprofusion.com");
            System.Console.WriteLine("");
        }

        private static void ShowHelp()
        {
            System.Console.WriteLine("Usage: \n\n");
            System.Console.WriteLine("-h --help : show this help information");
            System.Console.WriteLine("-r --renew : renew certificates for all managed sites");
            System.Console.WriteLine("-l --list : list managed sites");
            System.Console.WriteLine("-p --preview : auto scan and preview proposed list of managed sites");
            System.Console.WriteLine("\n\n");
        }

        /// <summary>
        /// Auto scan and preview list of sites to manage
        /// </summary>
        private void PreviewAutoManage()
        {
            var siteManager = new SiteManager();
            var siteList = siteManager.Preview();

            if (siteList == null || siteList.Count == 0)
            {
                System.Console.WriteLine("No Sites configured or access denied.");
            }
            else
            {
                foreach (var s in siteList)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    System.Console.WriteLine(String.Format("{0} ({1}): Create single certificate for {2} bindings: \n", s.SiteName, s.SiteType.ToString(), s.SiteBindings.Count));

                    Console.ResetColor();
                    foreach (var b in s.SiteBindings)
                    {
                        System.Console.WriteLine("\t" + b.Hostname + " \n");
                    }
                }
            }

            siteManager.StoreSettings();
        }

        private void PerformAutoRenew()
        {
            //go through list of items configured for auto renew, perform renewal and report the result
        }

        private bool PerformCertRequestAndIISBinding(string certDomain, string[] alternativeNames)
        {
            // ACME service requires international domain names in ascii mode
            certDomain = _idnMapping.GetAscii(certDomain);

            //create cert and binding it

            //Typical command sequence for a new certificate

            //Initialize-ACMEVault -BaseURI https://acme-staging.api.letsencrypt.org/

            // Get-Module -ListAvailable ACMESharp
            // New-ACMEIdentifier -Dns test7.examplesite.co.uk -Alias test7_examplesite_co_uk636213616564101276 -Label Identifier:test7.examplesite.co.uk
            // Complete-ACMEChallenge -Ref test7_examplesite_co_uk636213616564101276 -ChallengeType http-01 -Handler manual  -Regenerate
            // Submit-ACMEChallenge -Ref test7_examplesite_co_uk636213616564101276 -Challenge http-01
            // Update-ACMEIdentifier -Ref test7_examplesite_co_uk636213616564101276
            // Update-ACMEIdentifier -Ref test7_examplesite_co_uk636213616564101276
            // New-ACMECertificate -Identifier test7_examplesite_co_uk636213616564101276 -Alias cert_test7_examplesite_co_uk636213616564101276 -Generate
            // Update-ACMEIdentifier -Ref test7_examplesite_co_uk636213616564101276
            // Update-ACMEIdentifier -Ref test7_examplesite_co_uk636213616564101276
            // Get-ACMECertificate -Ref = ac22dbfe - b75f - 4cac-9247-b40c1d9bf9eb -ExportPkcs12 C:\ProgramData\ACMESharp\sysVault\99-ASSET\ac22dbfe-b75f-4cac-9247-b40c1d9bf9eb-all.pfx -Overwrite

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
            certifyManager.usePowershell = false;

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
        }
    }
}