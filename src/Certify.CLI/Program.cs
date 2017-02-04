using System;
using System.Collections.Generic;
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
                p.PreviewAutoManage();
                //p.PerformCertRequestAndIISBinding("test.domain.com");
                //p.PerformVaultCleanup();
                System.Console.ReadKey();
                return 1;
            }

            return 0;
        }

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

        private bool PerformCertRequestAndIISBinding(string certDomain)
        {
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

            var vaultManager = new VaultManager(Properties.Settings.Default.VaultPath, LocalDiskVault.VAULT);

            //init vault if not already created
            vaultManager.InitVault(staging: true);

            var certifyManager = vaultManager.PowershellManager;

            //domain alias is used as an ID in both the vault and the LE server, it's specific to one authorization attempt and cannot be reused for renewal

            var domainIdentifierAlias = vaultManager.ComputeIdentifierAlias(certDomain);
            //get info on existing IIS site we want to create/update SSL binding for
            IISManager iisManager = new IISManager();
            var iisSite = iisManager.GetSiteBindingByDomain(certDomain);
            var certConfig = new CertRequestConfig()
            {
                Domain = certDomain,
                PerformChallengeFileCopy = true,
                WebsiteRootPath = Environment.ExpandEnvironmentVariables(iisSite.PhysicalPath)
            };

            //NOTE: to support a SAN certificate (multiple alternative domains on one site) the domain validation steps need to be repeat for each name:

            //register identifier with LE, get http challenge spec back
            //create challenge response answer file under site .well-known, auto configure web.config for extenstionless content, mark challenge prep completed
            var authState = vaultManager.BeginRegistrationAndValidation(certConfig, domainIdentifierAlias);

            //ask LE to check our answer to their authorization challenge (http), LE will then attempt to fetch our answer, if all accessible and correct (authorized) LE will then allow us to request a certificate
            certifyManager.SubmitChallenge(domainIdentifierAlias, "http-01");

            //
            certifyManager.UpdateIdentifier(domainIdentifierAlias);
            var identiferStatus = vaultManager.GetIdentifier(domainIdentifierAlias, true);
            var attempts = 0;
            var maxAttempts = 3;

            while (identiferStatus.Authorization.Status == "pending" && attempts < maxAttempts)
            {
                System.Threading.Thread.Sleep(2000); //wait a couple of seconds before checking again
                certifyManager.UpdateIdentifier(domainIdentifierAlias);
                identiferStatus = vaultManager.GetIdentifier(domainIdentifierAlias, true);
                attempts++;
            }

            if (identiferStatus.Authorization.Status != "valid")
            {
                //still pending or failed
                System.Diagnostics.Debug.WriteLine("LE Authorization problem: " + identiferStatus.Authorization.Status);
                return false;
            }
            else
            {
                //all good, we can request a certificate
                //if authorizing a SAN we would need to repeat the above until all domains are valid, then we can request cert
                var certAlias = "cert_" + domainIdentifierAlias;

                //register cert placeholder in vault
                certifyManager.NewCertificate(domainIdentifierAlias, certAlias, subjectAlternativeNameIdentifiers: null);

                //ask LE to issue a certificate for our domain(s)
                certifyManager.SubmitCertificate(certAlias);

                //LE may now have issued a certificate, this process may not be immediate
                var certDetails = vaultManager.GetCertificate(certAlias, reloadVaultConfig: true);
                attempts = 0;
                //cert not issued yet, wait and try again
                while ((certDetails == null || String.IsNullOrEmpty(certDetails.IssuerSerialNumber)) && attempts < maxAttempts)
                {
                    System.Threading.Thread.Sleep(2000); //wait a couple of seconds before checking again
                    certifyManager.UpdateCertificate(certAlias);
                    certDetails = vaultManager.GetCertificate(certAlias, reloadVaultConfig: true);
                    attempts++;
                }

                if (certDetails != null && !String.IsNullOrEmpty(certDetails.IssuerSerialNumber))
                {
                    //we have an issued certificate, we can go ahead and install it as required
                    System.Diagnostics.Debug.WriteLine("Received certificate issued by LE." + JsonConvert.SerializeObject(certDetails));

                    //if using cert in IIS, we need to export the certificate PFX file, install it as a certificate and setup the site binding to map to this cert
                    string certFolderPath = vaultManager.GetCertificateFilePath(certDetails.Id, LocalDiskVault.ASSET);
                    string pfxFile = certAlias + "-all.pfx";
                    string pfxPath = System.IO.Path.Combine(certFolderPath, pfxFile);

                    //create folder to export PFX to, if required
                    if (!System.IO.Directory.Exists(certFolderPath))
                    {
                        System.IO.Directory.CreateDirectory(certFolderPath);
                    }

                    //if file already exists we want to delet the old one
                    if (System.IO.File.Exists(pfxPath))
                    {
                        //delete existing PFX (if any)
                        System.IO.File.Delete(pfxPath);
                    }

                    //export the PFX file
                    vaultManager.ExportCertificate(certAlias, pfxOnly: true);

                    if (!System.IO.File.Exists(pfxPath))
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to export PFX. " + pfxPath);
                        return false;
                    }

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
                    System.Diagnostics.Debug.WriteLine("LE did not issue a valid certificate in the time allowed." + JsonConvert.SerializeObject(certDetails));
                    return false;
                }
            }
        }
    }
}