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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Certify.Management
{
    public static class CertificateManager
    {
        public static X509Certificate2 GenerateTlsSni01Certificate(string domain)
        {
            // configure generators
            var random = new SecureRandom(new CryptoApiRandomGenerator());
            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);

            // create self-signed certificate
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.SetSubjectDN(new X509Name($"CN={domain}"));
            certificateGenerator.SetIssuerDN(new X509Name($"CN={domain}"));
            certificateGenerator.SetSerialNumber(serialNumber);
            certificateGenerator.SetNotBefore(DateTime.UtcNow);
            certificateGenerator.SetNotAfter(DateTime.UtcNow.AddMinutes(5));
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
                FriendlyName = domain,
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

        public static List<X509Certificate2> GetCertificatesFromStore()
        {
            var store = GetDefaultStore();
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);
            List<X509Certificate2> list = new List<X509Certificate2>();
            foreach (var c in store.Certificates.Find(X509FindType.FindByIssuerName, "Let's Encrypt", false))
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
            X509Certificate2Collection results = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false);
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
            X509Certificate2Collection results = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
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
            //TODO: remove old cert?
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
            RSACryptoServiceProvider rsa = cert.PrivateKey as RSACryptoServiceProvider;

            if (rsa != null)
            {
                string privateKeyPath = GetMachineKeyLocation(rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                FileInfo file = new FileInfo(privateKeyPath + "\\" + rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                var fs = file.GetAccessControl();

                var account = new System.Security.Principal.NTAccount(accountName);
                fs.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.Read, AccessControlType.Allow));

                file.SetAccessControl(fs);
            }
        }

        public static FileSecurity GetUserAccessInfoForCertificatePrivateKey(X509Certificate2 cert)
        {
            RSACryptoServiceProvider rsa = cert.PrivateKey as RSACryptoServiceProvider;

            if (rsa != null)
            {
                string privateKeyPath = GetMachineKeyLocation(rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                FileInfo file = new FileInfo(privateKeyPath + "\\" + rsa.CspKeyContainerInfo.UniqueKeyContainerName);

                return file.GetAccessControl();
            }
            return null;
        }

        private static string GetMachineKeyLocation(string keyFileName)
        {
            string appDataPath =
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string machineKeyPath = appDataPath + @"\Microsoft\Crypto\RSA\MachineKeys";
            string[] fileList = Directory.GetFiles(machineKeyPath, keyFileName);

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
                foreach (string filename in fileList)
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

        /// <summary>
        /// Remove old certificates we have created previously (based on a matching prefix string,
        /// compared to our new certificate)
        /// </summary>
        /// <param name="certificate"> The new cert to keep </param>
        /// <param name="hostPrefix"> The cert friendly name prefix to match certs to clean up </param>
        public static void CleanupCertificateDuplicates(X509Certificate2 certificate, string hostPrefix)
        {
            // TODO: remove distinction, this is legacy from the old version which didn't have a
            //       clear app specific prefix

            if (certificate.FriendlyName.Length < 10) return;

            var store = GetDefaultStore();
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);

            var certsToRemove = new List<X509Certificate2>();
            foreach (var c in store.Certificates)
            {
                // queue removal of any existing cert which has same hostname prefix and includes
                // [Certify] text.
                if (c.FriendlyName.StartsWith(hostPrefix, StringComparison.InvariantCulture) && c.FriendlyName.Contains("[Certify]") && c.GetCertHashString() != certificate.GetCertHashString())
                {
                    certsToRemove.Add(c);
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