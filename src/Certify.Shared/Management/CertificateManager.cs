using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Models.Providers;
using Certify.Utils;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace Certify.Management
{
    public static class CertificateManager
    {
        public static X509Certificate2 GenerateSelfSignedCertificate(string domain, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            // configure generators
            var random = new SecureRandom(new CryptoApiRandomGenerator());
            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);

            // create self-signed certificate
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.SetSubjectDN(new X509Name($"CN={domain}"));
            certificateGenerator.SetIssuerDN(new X509Name($"CN={domain}"));
            certificateGenerator.SetSerialNumber(serialNumber);
            certificateGenerator.SetNotBefore(dateFrom ?? DateTime.UtcNow);
            certificateGenerator.SetNotAfter(dateTo ?? DateTime.UtcNow.AddMinutes(5));
            certificateGenerator.AddExtension(X509Extensions.SubjectAlternativeName.Id, false, new DerSequence(new Asn1Encodable[] { new GeneralName(GeneralName.DnsName, domain) }));
            certificateGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(new KeyPurposeID[] { KeyPurposeID.IdKPServerAuth, KeyPurposeID.IdKPClientAuth }));
            certificateGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyEncipherment | KeyUsage.DigitalSignature));

            var keyPair = keyPairGenerator.GenerateKeyPair();
            certificateGenerator.SetPublicKey(keyPair.Public);
            var bouncy_cert = certificateGenerator.Generate(new Asn1SignatureFactory("SHA256WithRSA", keyPair.Private, random));

            // get private key into machine key store
            var csp = new RSACryptoServiceProvider(
                new CspParameters
                {
                    KeyContainerName = Guid.NewGuid().ToString(),
                    KeyNumber = 1,
                    Flags = CspProviderFlags.UseMachineKeyStore
                });

            var rp = DotNetUtilities.ToRSAParameters((RsaPrivateCrtKeyParameters)keyPair.Private);
            csp.ImportParameters(rp);

            // convert from bouncy cert to X509Certificate2
            return new X509Certificate2(bouncy_cert.GetEncoded(), (string)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet)
            {
                FriendlyName = domain + " [Certify] Self Signed - " + bouncy_cert.NotBefore + " to " + bouncy_cert.NotAfter,
                PrivateKey = csp
            };
        }

        public static bool VerifyCertificateSAN(System.Security.Cryptography.X509Certificates.X509Certificate certificate, string sni)
        {
            // check subject alternate name (must have exactly 1, equal to sni)
            var x509 = DotNetUtilities.FromX509Certificate(certificate);
            var sans = X509ExtensionUtilities.GetSubjectAlternativeNames(x509);
            if (sans.Count != 1) return false;
            var san = (System.Collections.IList)((System.Collections.IList)sans)[0];
            var sniOK = san[0].Equals(GeneralName.DnsName) && san[1].Equals(sni);

            // if subject matches sni and SAN is ok, return true
            return x509.SubjectDN.ToString() == $"CN={sni}" && sniOK;
        }

        public static X509Certificate2 LoadCertificate(string filename)
        {
            var cert = new X509Certificate2();
            cert.Import(filename);
            return cert;
        }

        public static async Task<X509Certificate2> StoreCertificate(string host, string pfxFile, bool isRetry = false, bool enableRetryBehaviour = true)
        {
            // https://support.microsoft.com/en-gb/help/950090/installing-a-pfx-file-using-x509certificate-from-a-standard--net-appli
            var certificate = new X509Certificate2(pfxFile, "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            certificate.GetExpirationDateString();
            certificate.FriendlyName = host + " [Certify] - " + certificate.GetEffectiveDateString() + " to " + certificate.GetExpirationDateString();

            var cert = StoreCertificate(certificate);

            await Task.Delay(500);

            // now check if cert is accessible and private key is OK (in some cases cert is not
            // storing private key properly)
            var storedCert = GetCertificateByThumbprint(cert.Thumbprint);

            if (enableRetryBehaviour)
            {
                if (!isRetry)
                {
                    // hack/workaround - importing cert from system account causes private key to be
                    // transient. Re-import the same cert fixes it. re -try apply .net dev on why
                    // re-import helps with private key: https://stackoverflow.com/questions/40892512/add-a-generated-certificate-to-the-store-and-update-an-iis-site-binding
                    return await StoreCertificate(host, pfxFile, isRetry: true);
                }
            }

            if (storedCert == null)
            {
                throw new Exception("Certificate not found in store!");
            }
            else
            {
                try
                {
                    if (!storedCert.HasPrivateKey)
                    {
                        throw new Exception("Private key not available.");
                    }
                    else
                    {
                        return storedCert;
                    }
                }
                catch (Exception)
                {
                    throw new Exception("Certificate Private Key not available!");
                }
            }
        }

        public static List<X509Certificate2> GetCertificatesFromStore(string issuerName = null)
        {
            var store = GetDefaultStore();
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
            var list = new List<X509Certificate2>();
            var certCollection = !string.IsNullOrEmpty(issuerName) ?
                store.Certificates.Find(X509FindType.FindByIssuerName, issuerName, false)
                : store.Certificates;

            foreach (var c in certCollection)
            {
                list.Add(c);
            }

            store.Close();
            return list;
        }

        public static X509Certificate2 GetCertificateFromStore(string subjectName)
        {
            X509Certificate2 cert = null;

            var store = GetDefaultStore();

            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

            var results = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false);

            if (results.Count > 0)
            {
                cert = results[0];
            }

            store.Close();

            return cert;
        }

        public static X509Certificate2 GetCertificateByThumbprint(string thumbprint)
        {
            X509Certificate2 cert = null;

            var store = GetDefaultStore();
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

            var results = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

            if (results.Count > 0)
            {
                cert = results[0];
            }

            store.Close();

            return cert;
        }

        public static X509Certificate2 StoreCertificate(X509Certificate2 certificate)
        {
            var store = GetDefaultStore();

            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);

            store.Add(certificate);

            store.Close();

            return certificate;
        }

        public static void RemoveCertificate(X509Certificate2 certificate)
        {
            var store = GetDefaultStore();
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            store.Remove(certificate);
            store.Close();
        }

        /// <summary>
        /// For IIS to use a certificate it's process user must be able to encrypt outgoing traffic,
        /// so it needs the private key for our certificate. If a system user creates the certificate
        /// the default permission may not allow access to the private key.
        /// </summary>
        /// <param name="cert"> cert including private key </param>
        /// <param name="accountName"> user to grant read access for </param>
        public static void GrantUserAccessToCertificatePrivateKey(X509Certificate2 cert, string accountName)
        {

            if (cert.PrivateKey is RSACryptoServiceProvider rsa)
            {
                var privateKeyPath = GetMachineKeyLocation(rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                var file = new FileInfo(privateKeyPath + "\\" + rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                var fs = file.GetAccessControl();

                var account = new System.Security.Principal.NTAccount(accountName);
                fs.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.Read, AccessControlType.Allow));

                file.SetAccessControl(fs);
            }
        }

        public static FileSecurity GetUserAccessInfoForCertificatePrivateKey(X509Certificate2 cert)
        {

            if (cert.PrivateKey is RSACryptoServiceProvider rsa)
            {
                var privateKeyPath = GetMachineKeyLocation(rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                var file = new FileInfo(privateKeyPath + "\\" + rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                return file.GetAccessControl();
            }

            return null;
        }

        private static string GetMachineKeyLocation(string keyFileName)
        {
            var appDataPath =
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            var machineKeyPath = appDataPath + @"\Microsoft\Crypto\RSA\MachineKeys";

            var fileList = Directory.GetFiles(machineKeyPath, keyFileName);

            // if we have results, use this path
            if (fileList.Any())
            {
                return machineKeyPath;
            }

            //if no results from common app data path, try alternative
            appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            machineKeyPath = appDataPath + @"\Microsoft\Crypto\RSA\";
            fileList = Directory.GetDirectories(machineKeyPath);

            if (fileList.Any())
            {
                foreach (var filename in fileList)
                {
                    var dirList = Directory.GetFiles(filename, keyFileName);
                    if (dirList.Any())
                    {
                        return filename;
                    }
                }
            }

            //Could not access private key file.
            return null;
        }

        public static X509Store GetDefaultStore()
        {
            return new X509Store(StoreName.My, StoreLocation.LocalMachine);
        }

        public static bool IsCertificateInStore(X509Certificate2 cert)
        {
            var certExists = false;

            using (var store = GetDefaultStore())
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

                if (store.Certificates.Contains(cert))
                {
                    certExists = true;
                }

                store.Close();
            }

            return certExists;
        }

        /// <summary>
        /// Remove all certificate expired a month or more before the given date, with [Certify] in
        /// the friendly name, optionally where there are no existing bindings, vary by Cleanup Mode
        /// </summary>
        /// <param name="expiryBefore">  </param>
        public static List<string> PerformCertificateStoreCleanup(
            Models.CertificateCleanupMode cleanupMode,
            DateTime expiryBefore,
            string matchingName,
            List<string> excludedThumbprints,
            ILog log = null
            )
        {
            var removedCerts = new List<string>();

            try
            {
                // get all existing cert bindings
                var allCertBindings = new List<Models.BindingInfo>();

                // TODO: reinstate once we have reliable binding info (also some users get an FileNotFound dll loading exception accessing this functionality):
                // if (checkBindings) allCertBindings =  Certify.Utils.Networking.GetCertificateBindings();

                // get all certificates
                using (var store = GetDefaultStore())
                {
                    store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);

                    var certsToRemove = new List<X509Certificate2>();
                    foreach (var c in store.Certificates)
                    {
                        // cleanup either has to be expired only or has to be given a list of certificate thumbprints to preserve
                        // if cert is in the exclusion list then cleanup is skipped

                        if (
                            (excludedThumbprints == null && cleanupMode == Models.CertificateCleanupMode.AfterExpiry)
                            ||
                            (excludedThumbprints.Any() && !excludedThumbprints.Any(e => e.ToLower() == c.Thumbprint.ToLower()))
                            )
                        {
                            //bool isBound = false;
                            //if (checkBindings) isBound = allCertBindings.Any(b => b.CertificateHash == c.Thumbprint);

                            if (cleanupMode == Models.CertificateCleanupMode.AfterExpiry)
                            {
                                // queue removal of existing expired cert with [Certify] text in friendly name.
                                if (
                                     (string.IsNullOrEmpty(matchingName) || (c.FriendlyName.StartsWith(matchingName)))
                                     && c.FriendlyName.Contains("[Certify]")
                                     && c.NotAfter < expiryBefore
                                     )
                                {
                                    certsToRemove.Add(c);
                                }
                            }
                            else if (cleanupMode == Models.CertificateCleanupMode.AfterRenewal)
                            {
                                // queue removal of existing cert based on name match

                                if (
                                    (!string.IsNullOrEmpty(matchingName) && c.FriendlyName.StartsWith(matchingName))
                                    && c.FriendlyName.Contains("[Certify]")
                                    )
                                {
                                    certsToRemove.Add(c);
                                }

                            }
                            else if (cleanupMode == Models.CertificateCleanupMode.FullCleanup)
                            {
                                // queue removal of any Certify cert not in excluded list

                                if (
                                     (string.IsNullOrEmpty(matchingName) || (c.FriendlyName.StartsWith(matchingName)))
                                    && c.FriendlyName.Contains("[Certify]")
                                    )
                                {
                                    certsToRemove.Add(c);
                                }

                            }
                        }
                    }

                    // attempt to remove certs
                    foreach (var oldCert in certsToRemove)
                    {
                        try
                        {
                            store.Remove(oldCert);

                            removedCerts.Add($"{oldCert.FriendlyName} : {oldCert.Thumbprint}");

                            log?.Information($"Removing old cert: { oldCert.FriendlyName} : { oldCert.Thumbprint}");
                        }
                        catch (Exception exp)
                        {
                            // Couldn't remove it
                            log?.Error("Could not remove cert:" + oldCert.FriendlyName + " " + exp.ToString());
                        }
                    }
                    store.Close();
                }
            } catch (Exception exp)
            {
                log?.Error("Failed to perform certificate cleanup: " + exp.ToString());
            }

            return removedCerts;
        }
    }
}
