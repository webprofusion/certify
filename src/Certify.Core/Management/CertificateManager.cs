using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public static class CertificateManager
    {
        public static X509Certificate2 LoadCertificate(string filename)
        {
            var cert = new X509Certificate2();
            cert.Import(filename);
            return cert;
        }

        public static X509Certificate2 StoreCertificate(string host, string pfxFile)
        {
            var certificate = new X509Certificate2(pfxFile, "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            certificate.GetExpirationDateString();
            certificate.FriendlyName = host + " [Certify] - " + certificate.GetEffectiveDateString() + " to " + certificate.GetExpirationDateString();

            store.Add(certificate);
            store.Close();
            return certificate;
        }

        public static X509Store GetDefaultStore()
        {
            return new X509Store(StoreName.My, StoreLocation.LocalMachine);
        }

        /// <summary>
        /// Remove old certificates we have created previously (based on a matching prefix string,
        /// compared to our new certificate)
        /// </summary>
        /// <param name="certificate">The new cert to keep</param>
        /// <param name="hostPrefix">The cert friendly name prefix to match certs to clean up</param>
        public static void CleanupCertificateDuplicates(X509Certificate2 certificate, string hostPrefix)
        {
            // TODO: remove distinction, this is legacy from the old version which didn't have a
            //       clear app specific prefix
            bool requireCertifySpecificCerts = false;

            if (certificate.FriendlyName.Length < 10) return;

            var store = GetDefaultStore();
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);

            var certsToRemove = new List<X509Certificate2>();
            foreach (var c in store.Certificates)
            {
                //TODO: add tests for this then remove the check because these two branches are the same, obviously not as intended
                if (requireCertifySpecificCerts)
                {
                    if (c.FriendlyName.StartsWith(hostPrefix, StringComparison.InvariantCulture) && c.GetCertHashString() != certificate.GetCertHashString())
                    {
                        //going to remove certs with same friendly name
                        certsToRemove.Add(c);
                    }
                }
                else
                {
                    if (c.FriendlyName.StartsWith(hostPrefix, StringComparison.InvariantCulture) && c.GetCertHashString() != certificate.GetCertHashString())
                    {
                        //going to remove certs with same friendly name
                        certsToRemove.Add(c);
                    }
                }
            }

            // attempt to remove certs
            foreach (var oldCert in certsToRemove)
            {
                try
                {
                    store.Remove(oldCert);
                }
                catch (Exception exp)
                {
                    // Couldn't remove it
                    System.Diagnostics.Debug.WriteLine("Could not remove cert:" + oldCert.FriendlyName + " " + exp.ToString());
                }
            }

            store.Close();
        }
    }
}