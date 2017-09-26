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
using Org.BouncyCastle.X509.Extension;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

        public static X509Certificate2 StoreCertificate(string host, string pfxFile)
        {
            var certificate = new X509Certificate2(pfxFile, "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            certificate.GetExpirationDateString();
            certificate.FriendlyName = host + " [Certify] - " + certificate.GetEffectiveDateString() + " to " + certificate.GetExpirationDateString();

            return StoreCertificate(certificate);
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