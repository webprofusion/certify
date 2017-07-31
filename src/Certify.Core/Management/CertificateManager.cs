using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public class CertificateManager
    {
        public X509Certificate2 GetCertificate(string filename)
        {
            var cert = new X509Certificate2();
            cert.Import(filename);
            return cert;
        }

        public X509Certificate2 StoreCertificate(string host, string pfxFile)
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            //TODO: remove old cert?
            var certificate = new X509Certificate2(pfxFile, "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            certificate.GetExpirationDateString();
            certificate.FriendlyName = host + " [Certify] - " + certificate.GetEffectiveDateString() + " to " + certificate.GetExpirationDateString();

            store.Add(certificate);
            store.Close();

            if (Properties.Settings.Default.LegacyRSASChannelSupport) {
                StoreCertificateLegacy(certificate);
            }

            return certificate;
        }

        
        public void StoreCertificateLegacy(X509Certificate2 certificate)
        {
            // https://blogs.technet.microsoft.com/jasonsla/2015/01/15/the-one-with-the-fba-redirect-loop/:
            // Exchange FBA does not support CNG certificates.Exchange only uses and supports the legacy
            // CryptoAPI which uses Cryptographic Service Providers(CSP).
            // The certificate should be registered using this legacy method on the machine to support Exchage Server.

            int exitCode = 0;
            string certExportPath = Environment.SpecialFolder.ApplicationData + "\\CertifyCertificateLegacyTemp.pfx";
            try {
                if (System.IO.File.Exists(certExportPath)) {
                    System.IO.File.Delete(certExportPath);
                }
            }
            catch {
                Debug.WriteLine("Certify Legacy Exchange Support: Unable to delete exported certificate.");
            }
            ProcessStartInfo export = new ProcessStartInfo() {
                Arguments = string.Format("-p Certify -ExportPFX {0} \"{1}\"", certificate.GetSerialNumberString(), certExportPath),
                FileName = "certutil"
            };
            using (Process proc = Process.Start(export)) {
                proc.WaitForExit();
                exitCode = proc.ExitCode;
            }
            if (!exitCode.Equals(0)) {
                Debug.WriteLine("Certify Legacy Exchange Support: Failed to export certificate from store.");
            }

            ProcessStartInfo delete = new ProcessStartInfo() {
                Arguments = string.Format("-delstore My \"{0}\"", certificate.GetSerialNumberString()),
                FileName = "certutil"
            };
            using (Process proc = Process.Start(delete)) {
                proc.WaitForExit();
                exitCode = proc.ExitCode;
            }
            if (!exitCode.Equals(0)) {
                Debug.WriteLine("Certify Legacy Exchange Support: Failed to delete certificate from store.");
            }

            ProcessStartInfo import = new ProcessStartInfo()
            {
                Arguments = string.Format("-p Certify -csp \"Microsoft RSA SChannel Cryptographic Provider\" -importpfx \"{0}\"", certExportPath),
                FileName = "certutil"
            };
            using (Process proc = Process.Start(export)) {
                proc.WaitForExit();
                exitCode = proc.ExitCode;
            }
            if (!exitCode.Equals(0)) {
                Debug.WriteLine("Certify Legacy Exchange Support: Failed to import certificate back to store.");
            }
        }

        public X509Store GetDefaultStore()
        {
            return new X509Store(StoreName.My, StoreLocation.LocalMachine);
        }

        /// <summary>
        /// Remove old certificates we have created previously (based on a matching prefix string,
        /// compared to our new certificate)
        /// </summary>
        /// <param name="certificate">The new cert to keep</param>
        /// <param name="hostPrefix">The cert friendly name prefix to match certs to clean up</param>
        public void CleanupCertificateDuplicates(X509Certificate2 certificate, string hostPrefix)
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