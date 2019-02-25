using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        public void RunCertDiagnostics()
        {
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

            foreach (var site in managedCertificates)
            {
                if (!string.IsNullOrEmpty(site.CertificatePath))
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
    }
}
