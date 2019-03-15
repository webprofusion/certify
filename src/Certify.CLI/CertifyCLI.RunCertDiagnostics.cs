using System;
using System.Linq;
using Certify.Management;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        public void RunCertDiagnostics(bool autoFix = false)
        {
            string stripNonNumericFromString(string input)
            {
                return new string(input.Where(c => char.IsDigit(c)).ToArray());
            }

            bool isNumeric(string input)
            {
                return int.TryParse(input, out _);
            }

            var managedCertificates = _certifyClient.GetManagedCertificates(new ManagedCertificateFilter()).Result;
            Console.ForegroundColor = ConsoleColor.White;

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

            Console.WriteLine("Running cert diagnostics..");

            var countSiteIdsFixed = 0;
            var countBindingRedeployments = 0;

            foreach (var site in managedCertificates)
            {

                var redeployRequired = false;
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

                        var update = _certifyClient.UpdateManagedCertificate(site);
                        update.ConfigureAwait(true);
                        update.Wait();

                        countSiteIdsFixed++;
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

                                // check if siteID has any special characters
                                if (redeployRequired && autoFix)
                                {

                                    //reapply current certificate bindings

                             
                                    

                                    var bindingApply = _certifyClient.ReapplyCertificateBindings(site.Id, false);
                                    bindingApply.ConfigureAwait(true);
                                    bindingApply.Wait();

                                    countBindingRedeployments++;

                                    var result = bindingApply.Result;
                                    if (!result.IsSuccess)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("\t Error: Failed to re-applying certificate bindings:" + site.Name);
                                        Console.ForegroundColor = ConsoleColor.White;
                                       
                                    } else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("\t Info: re-applied certificate bindings:" + site.Name);
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }

                                    System.Threading.Thread.Sleep(5000);
                                }

                            }
                            else
                            {
                                if (redeployRequired)
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                                    Console.WriteLine($"Warning: {site.Name} :: Stored cert not found, bindings cannot be redeployed");
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
                            Console.WriteLine($"{site.Name} has no current certificate are requires manual verification/redeploy of cert.");
                        }
                    }
                }
            }

            // TODO: get refresh of managed certs and for each current cert thumbprint, verify binding thumbprint match

            Console.WriteLine("-----------");
        }
    }
}
