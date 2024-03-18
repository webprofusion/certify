using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Certify.Models;
using Certify.Models.Providers;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace Certify.Management
{
    public static class CertificateManager
    {
        public const string ROOT_STORE_NAME = "Root";
        public const string CA_STORE_NAME = "CA";
        public const string DEFAULT_STORE_NAME = "My";
        public const string WEBHOSTING_STORE_NAME = "WebHosting";
        public const string DISALLOWED_STORE_NAME = "Disallowed";
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        private static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static X509Certificate2 GenerateSelfSignedCertificate(string domain, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null, string suffix = "[Certify]", string subject = null, string keyType = StandardKeyTypes.RSA256)
        {

            // configure generators
            var random = new SecureRandom(new CryptoApiRandomGenerator());
            IAsymmetricCipherKeyPairGenerator keyPairGenerator = null;
            KeyGenerationParameters keyGenerationParameters = null;
            var sigType = "SHA256WITHRSA";
            if (keyType == StandardKeyTypes.RSA256)
            {
                keyPairGenerator = new RsaKeyPairGenerator();
                keyGenerationParameters = new KeyGenerationParameters(random, 2048);
                sigType = "SHA256WITHRSA";
            }
            else if (keyType == StandardKeyTypes.ECDSA256)
            {
                keyGenerationParameters = new KeyGenerationParameters(random, 256);
                keyPairGenerator = new ECKeyPairGenerator();
                sigType = "SHA256WITHECDSA";
            }

            if (keyPairGenerator == null || keyGenerationParameters == null)
            {
                throw new NotSupportedException("Key type not supported");
            }

            keyPairGenerator.Init(keyGenerationParameters);

            // create self-signed certificate
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            var certificateGenerator = new X509V3CertificateGenerator();
            certificateGenerator.SetSubjectDN(new X509Name($"CN={(subject ?? domain)}"));
            certificateGenerator.SetIssuerDN(new X509Name($"CN={(subject ?? domain)}"));
            certificateGenerator.SetSerialNumber(serialNumber);
            certificateGenerator.SetNotBefore((dateFrom ?? DateTime.UtcNow).DateTime);
            certificateGenerator.SetNotAfter((dateTo ?? DateTime.UtcNow.AddMinutes(5)).DateTime);
            certificateGenerator.AddExtension(X509Extensions.SubjectAlternativeName.Id, false, new DerSequence(new Asn1Encodable[] { new GeneralName(GeneralName.DnsName, domain) }));
            certificateGenerator.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(new KeyPurposeID[] { KeyPurposeID.id_kp_serverAuth, KeyPurposeID.id_kp_clientAuth }));
            certificateGenerator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyEncipherment | KeyUsage.DigitalSignature));

            var keyPair = keyPairGenerator.GenerateKeyPair();
            certificateGenerator.SetPublicKey(keyPair.Public);
            var bouncy_cert = certificateGenerator.Generate(new Asn1SignatureFactory(sigType, keyPair.Private, random));

            var store = new Pkcs12StoreBuilder().Build();

            var friendlyName = domain + " " + suffix + " Self Signed - " + bouncy_cert.NotBefore + " to " + bouncy_cert.NotAfter;

            // Add the certificate.
            var certificateEntry = new X509CertificateEntry(bouncy_cert);
            store.SetCertificateEntry(friendlyName, certificateEntry);

            // Add the private key.
            store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(keyPair.Private), new[] { certificateEntry });

            using (var stream = new MemoryStream())
            {
                const string password = "";
                store.Save(stream, password.ToCharArray(), random);

                var finalExportableCertificate =
                    new X509Certificate2(stream.ToArray(),
                                         password,
                                         X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                return finalExportableCertificate;
            }
        }

        public static bool VerifyCertificateSAN(System.Security.Cryptography.X509Certificates.X509Certificate certificate, string sni)
        {
            // check subject alternate name (must have exactly 1, equal to sni)
            var x509 = DotNetUtilities.FromX509Certificate(certificate);

            var sans = x509.GetSubjectAlternativeNames();
            if (sans.Count != 1)
            {
                return false;
            }

            var san = (System.Collections.IList)((System.Collections.IList)sans)[0];
            var sniOK = san[0].Equals(GeneralName.DnsName) && san[1].Equals(sni);

            // if subject matches sni and SAN is ok, return true
            return x509.SubjectDN.ToString() == $"CN={sni}" && sniOK;
        }

        /// <summary>
        /// Gets the certificate the file is signed with.
        /// </summary>
        /// <param name="filename"> 
        /// The path of the signed file from which to create the X.509 certificate.
        /// </param>
        /// <returns> The certificate the file is signed with </returns>
        public static X509Certificate2 GetFileCertificate(string filename)
        {
            // https://blogs.msdn.microsoft.com/windowsmobile/2006/05/17/programmatically-checking-the-authenticode-signature-on-a-file/
            X509Certificate2 cert;

            try
            {
                cert = new X509Certificate2(System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(filename));

                CheckCertChain(cert);

            }
            catch (CryptographicException e)
            {
                Console.WriteLine("Error {0} : {1}", e.GetType(), e.Message);
                Console.WriteLine("Couldn't parse the certificate." +
                                  "Be sure it is an X.509 certificate");
                return null;
            }

            return cert;
        }

        /// <summary>
        /// Check validity and revocation status of a certificate chain
        /// </summary>
        /// <param name="cert"></param>
        /// <returns></returns>
        public static string[] CheckCertChain(string filename)
        {
            var cert = LoadCertificate(filename);
            return CheckCertChain(cert);
        }
        /// <summary>
        /// Check validity and revocation status of a certificate chain
        /// </summary>
        /// <param name="cert"></param>
        /// <returns></returns>
        public static string[] CheckCertChain(X509Certificate2 cert)
        {
            var chain = new X509Chain();
            var chainPolicy = new X509ChainPolicy()
            {
                RevocationMode = X509RevocationMode.Online,
                RevocationFlag = X509RevocationFlag.EndCertificateOnly
            };
            chain.ChainPolicy = chainPolicy;

            var results = new List<string>();

            try
            {
                var buildOK = chain.Build(cert);
                foreach (var chainElement in chain.ChainElements)
                {
                    foreach (var chainStatus in chainElement.ChainElementStatus)
                    {
                        results.Add($"{chainElement.Certificate.Subject} :: {chainStatus.StatusInformation}");
                        System.Diagnostics.Debug.WriteLine(chainStatus.StatusInformation);
                    }
                }
            }
            catch (Exception exp)
            {
                results.Add(exp.Message);
            }

            return results.ToArray();
        }

        /// <summary>
        /// Load certificate as PFX from file, extract issuer cert and End Entity cert, then query Ocsp status
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static async Task<CertificateStatusType> CheckOcspRevokedStatus(string filename, string pwd, ILog log = null)
        {
            if (string.IsNullOrEmpty(filename) || !File.Exists(filename))
            {
                return CertificateStatusType.Unknown;
            }

            try
            {
                var cert = LoadCertificate(filename, pwd);

                var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.Build(cert);

                Org.BouncyCastle.X509.X509Certificate issuerCert = null;

                if (chain.ChainElements.Count > 1)
                {
                    var issuer = chain.ChainElements[1].Certificate;
                    issuerCert = new X509CertificateParser().ReadCertificate(issuer.RawData);
                }

                if (cert != null)
                {
                    var endEntityCert = new X509CertificateParser().ReadCertificate(cert.RawData);
                    return await Shared.Utils.OcspUtils.Query(endEntityCert, issuerCert, log);
                }

                return CertificateStatusType.Unknown;
            }
            catch (Exception)
            {
                log?.Warning("Failed to Check Ocsp Revoked Status {file}", filename);

                return CertificateStatusType.Unknown;
            }
        }

        public static X509Certificate2 LoadCertificate(string filename, string pwd = "", bool throwOnError = false)
        {
            try
            {
                var cert = new X509Certificate2(filename, pwd, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                return cert;
            }
            catch (Exception exp)
            {
                if (throwOnError)
                {
                    throw;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LoadCertificate: Failed to load certificate: {filename}" + exp.Message);
                    return null;
                }
            }
        }

        public static Org.BouncyCastle.X509.X509Certificate ReadCertificateFromPem(string pemFile)
        {
            var x509CertificateParser = new X509CertificateParser();
            var cert = x509CertificateParser.ReadCertificate(File.ReadAllBytes(pemFile));

            return cert;
        }

        private static X509Store GetStore(string storeName, bool useMachineStore = true)
        {
            if (IsWindows)
            {
                if (useMachineStore)
                {
                    return GetMachineStore(storeName);
                }

                return GetUserStore(storeName);
            }
            else if (IsLinux)
            {
                // See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/StorePal.OpenSsl.cs#L142
                return GetUserStore(storeName);
            }
            else if (IsMac)
            {
                // See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/StorePal.macOS.cs#L108
                if (!useMachineStore || (storeName == CA_STORE_NAME || storeName == WEBHOSTING_STORE_NAME))
                {
                    return GetUserStore(storeName);
                }
                else if (storeName == DEFAULT_STORE_NAME || storeName == ROOT_STORE_NAME || storeName == DISALLOWED_STORE_NAME)
                {
                    return GetMachineStore(storeName);
                }

                throw new CryptographicException($"Could not open X509Store {storeName} in LocalMachine on OSX");
            }

            throw new PlatformNotSupportedException($"Could not open X509Store for unsupported OS {RuntimeInformation.OSDescription}");
        }

        private static void OpenStoreForReadWrite(X509Store store, string storeName)
        {
            if (IsWindows)
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            }
            else if (IsLinux)
            {
                // See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/StorePal.OpenSsl.cs#L142
                if (storeName == DEFAULT_STORE_NAME || storeName == WEBHOSTING_STORE_NAME)
                {
                    store.Open(OpenFlags.ReadWrite);
                }
                else
                {
                    store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
                }
            }
            else if (IsMac)
            {
                // See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/X509Certificates/StorePal.macOS.cs#L108
                if (storeName == CA_STORE_NAME || storeName == WEBHOSTING_STORE_NAME)
                {
                    store.Open(OpenFlags.ReadWrite);
                }
                else
                {
                    store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
                }
            }
        }

        public static bool StoreCertificateFromPem(string pem, string storeName, bool useMachineStore = true)
        {
            try
            {
                var x509CertificateParser = new X509CertificateParser();
                var cert = x509CertificateParser.ReadCertificate(System.Text.UTF8Encoding.UTF8.GetBytes(pem));

                var certToStore = new X509Certificate2(DotNetUtilities.ToX509Certificate(cert));
                using (var store = GetStore(storeName, useMachineStore))
                {
                    OpenStoreForReadWrite(store, storeName);
                    store.Add(certToStore);
                    store.Close();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<X509Certificate2> StoreCertificate(
                string host,
                string pfxFile,
                bool isRetry = false,
                bool enableRetryBehaviour = true,
                string storeName = DEFAULT_STORE_NAME,
                string customFriendlyName = null,
                string pwd = ""
            )
        {
            // https://support.microsoft.com/en-gb/help/950090/installing-a-pfx-file-using-x509certificate-from-a-standard--net-appli
            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(pfxFile, pwd, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            }
            catch (CryptographicException)
            {
                // retry  with blank pwd, may be transitional
                certificate = new X509Certificate2(pfxFile, "", X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);

                // success using blank pwd, continue with blank pwd
                pwd = "";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!string.IsNullOrEmpty(customFriendlyName))
                {
                    certificate.FriendlyName = customFriendlyName;
                }
                else
                {
                    certificate.GetExpirationDateString();

                    certificate.FriendlyName = host + " [Certify] - " + certificate.GetEffectiveDateString() + " to " + certificate.GetExpirationDateString();

                }
            }

            var cert = StoreCertificate(certificate, storeName);

            await Task.Delay(500);

            // now check if cert is accessible and private key is OK (in some cases cert is not
            // storing private key properly)
            var storedCert = GetCertificateByThumbprint(cert.Thumbprint, storeName);

            if (enableRetryBehaviour)
            {
                if (!isRetry)
                {
                    // hack/workaround - importing cert from system account causes private key to be
                    // transient. Re-import the same cert fixes it. re -try apply .net dev on why
                    // re-import helps with private key: https://stackoverflow.com/questions/40892512/add-a-generated-certificate-to-the-store-and-update-an-iis-site-binding
                    return await StoreCertificate(host, pfxFile, isRetry: true, storeName: storeName, customFriendlyName: customFriendlyName, pwd: pwd);
                }
            }

            if (storedCert == null)
            {
                throw new Exception("Certificate not found in store!");
            }
            else
            {
                if (!storedCert.HasPrivateKey)
                {
                    throw new Exception("Certificate Private key not available.");
                }
                else
                {
                    return storedCert;
                }
            }
        }

        public static List<X509Certificate2> GetCertificatesFromStore(string issuerName = null, string storeName = DEFAULT_STORE_NAME, bool useMachineStore = true)
        {
            var list = new List<X509Certificate2>();

            using (var store = GetStore(storeName, useMachineStore))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

                var certCollection = !string.IsNullOrEmpty(issuerName) ?
                    store.Certificates.Find(X509FindType.FindByIssuerName, issuerName, false)
                    : store.Certificates;

                foreach (var c in certCollection)
                {
                    list.Add(c);
                }

                store.Close();
            }

            return list;
        }

        public static X509Certificate2 GetCertificateFromStore(string subjectName, string storeName = DEFAULT_STORE_NAME)
        {
            X509Certificate2 cert = null;

            using (var store = GetStore(storeName))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

                var results = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false);

                if (results.Count > 0)
                {
                    cert = results[0];
                }

                store.Close();
            }

            return cert;
        }

        public static X509Certificate2 GetCertificateByThumbprint(string thumbprint, string storeName = DEFAULT_STORE_NAME, bool useMachineStore = true)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return null;
            }

            X509Certificate2 cert = null;

            using (var store = GetStore(storeName, useMachineStore))
            {
                store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly);

                var results = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

                if (results.Count > 0)
                {
                    cert = results[0];
                }

                store.Close();
            }

            return cert;
        }

        public static X509Certificate2 StoreCertificate(X509Certificate2 certificate, string storeName = DEFAULT_STORE_NAME)
        {
            using (var store = GetStore(storeName))
            {
                OpenStoreForReadWrite(store, storeName);

                store.Add(certificate);

                store.Close();
            }

            return certificate;
        }

        public static void RemoveCertificate(X509Certificate2 certificate, string storeName = DEFAULT_STORE_NAME)
        {
            using (var store = GetStore(storeName))
            {
                OpenStoreForReadWrite(store, storeName);
                store.Remove(certificate);
                store.Close();
            }
        }

        private static SecurityIdentifier TranslateToSecurityIdentifier(string val, ILog log)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    if (val.StartsWith("S-1-", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return new SecurityIdentifier(val);
                    }
                    else
                    {

                        return new NTAccount(val).Translate(typeof(SecurityIdentifier)) as SecurityIdentifier;
                    }
                }
                catch (Exception exp)
                {
                    log?.Error("Failed to translate account {val} to SID: {exp}", val, exp);
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        public static bool HasUserAccessToCertificatePrivateKey(X509Certificate2 cert, string accountName, string fileSystemRights = "fullcontrol", ILog log = null)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var keyPath = GetCertificatePrivateKeyPath(cert);
                if (keyPath != null)
                {
                    var userSid = TranslateToSecurityIdentifier(accountName, log);

                    if (userSid == null)
                    {
                        return false;
                    }

                    var file = new FileInfo(keyPath);

                    var fs = file.GetAccessControl();

                    var existing = fs.GetAccessRules(true, true, typeof(SecurityIdentifier));

                    foreach (FileSystemAccessRule rule in existing)
                    {
                        if (
                            rule.IdentityReference == userSid && rule.AccessControlType != AccessControlType.Deny
                            && (
                                (fileSystemRights == "read" && rule.FileSystemRights.HasFlag(FileSystemRights.Read))
                                ||
                                ((fileSystemRights == "fullcontrol" || fileSystemRights == "read") && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl))
                            )
                        )
                        {
                            // required permission already present
                            return true;
                        }
                    }

                    // no matching rule found
                    return false;
                }
                else
                {
                    // private key file path not found
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// If a system user creates the certificate the default permission may not allow access to the private key. Granting read permission to the key enables a service user to use the cert + key normally.
        /// </summary>
        /// <param name="cert"> cert including private key </param>
        /// <param name="accountName"> user to grant read access for </param>
        public static bool GrantUserAccessToCertificatePrivateKey(X509Certificate2 cert, string accountName, string fileSystemRights = "fullcontrol", ILog log = null)
        {
            if (HasUserAccessToCertificatePrivateKey(cert, accountName, fileSystemRights, log))
            {
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var keyPath = GetCertificatePrivateKeyPath(cert);

                if (keyPath != null)
                {
                    var userSid = TranslateToSecurityIdentifier(accountName, log);

                    if (userSid == null)
                    {
                        return false;
                    }

                    var file = new FileInfo(keyPath);

                    var fs = file.GetAccessControl();
                    var rightsToGrant = fileSystemRights == "fullcontrol" ? FileSystemRights.FullControl : FileSystemRights.Read;

                    try
                    {
                        fs.AddAccessRule(new FileSystemAccessRule(userSid, rightsToGrant, AccessControlType.Allow));

                        file.SetAccessControl(fs);
                    }
                    catch (Exception exp)
                    {
                        log?.Error("Failed to grant access to certificate private key: {exp}", exp);
                        return false;
                    }

                    // access rule applied
                    return true;
                }
                else
                {
                    log?.Warning("Could not locate private key file for certificate in order to grant access.");
                    // private key file path not found
                    return false;
                }
            }
            else
            {
                log?.Warning("Could not locate private key file for certificate.");
                // platform not supported
                return false;
            }
        }

        public static string GetCertificatePrivateKeyPath(X509Certificate2 cert)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var rsa = cert.GetRSAPrivateKey();

                if (rsa is RSA)
                {
                    if (rsa is RSACryptoServiceProvider rsaProvider)
                    {
                        var path = GetWindowsPrivateKeyLocation(rsaProvider.CspKeyContainerInfo.UniqueKeyContainerName);

                        if (path == null)
                        {
                            return null;
                        }

                        return Path.Combine(path, rsaProvider.CspKeyContainerInfo.UniqueKeyContainerName);
                    }
                    else if (rsa is RSACng rsaCng)
                    {
                        var path = GetWindowsPrivateKeyLocation(rsaCng.Key.UniqueName);

                        if (path == null)
                        {
                            return null;
                        }

                        return Path.Combine(path, rsaCng.Key.UniqueName);
                    }
                }
                else if (cert.GetECDsaPrivateKey() is ECDsa ecdsa)
                {
                    if (ecdsa is ECDsaCng ecdsaCng)
                    {
                        var path = GetWindowsPrivateKeyLocation(ecdsaCng.Key.UniqueName);

                        if (path == null)
                        {
                            return null;
                        }

                        return Path.Combine(path, ecdsaCng.Key.UniqueName);
                    }
                }
            }

            return null;
        }

        public static FileSecurity GetUserAccessInfoForCertificatePrivateKey(X509Certificate2 cert)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var filePath = GetCertificatePrivateKeyPath(cert);

                if (filePath != null)
                {
                    var file = new FileInfo(filePath);
                    return file.GetAccessControl();
                }
            }

            return null;
        }

        private static string GetWindowsPrivateKeyLocation(string keyFileName)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            var machineKeyPath = Path.Combine(appDataPath, "Microsoft", "Crypto", "RSA", "MachineKeys");

            var fileList = Directory.GetFiles(machineKeyPath, keyFileName);

            // if we have results, use this path
            if (fileList.Any())
            {
                return machineKeyPath;
            }

            // if EC/CNG key may be under /keys
            machineKeyPath = Path.Combine(appDataPath, "Microsoft", "Crypto", "Keys");

            fileList = Directory.GetFiles(machineKeyPath, keyFileName);

            if (fileList.Any())
            {
                return machineKeyPath;
            }

            //if no results from common app data path, try alternative use specific app data (files may be under user specific subfolder)
            appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            machineKeyPath = Path.Combine(appDataPath, "Microsoft", "Crypto", "RSA");
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

            appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            machineKeyPath = Path.Combine(appDataPath, "Microsoft", "Crypto", "Keys");
            fileList = Directory.GetFiles(machineKeyPath);

            if (fileList.Any())
            {
                return machineKeyPath;
            }

            //Could not access private key file.
            return null;
        }

        public static X509Store GetMachineStore(string storeName = DEFAULT_STORE_NAME) => new X509Store(storeName, StoreLocation.LocalMachine);
        public static X509Store GetUserStore(string storeName = DEFAULT_STORE_NAME) => new X509Store(storeName, StoreLocation.CurrentUser);

        public static bool IsCertificateInStore(X509Certificate2 cert, string storeName = DEFAULT_STORE_NAME)
        {
            var certExists = false;

            using (var store = GetStore(storeName))
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
            DateTimeOffset expiryBefore,
            string matchingName,
            List<string> excludedThumbprints,
            ILog log = null,
            string storeName = DEFAULT_STORE_NAME
            )
        {
            var removedCerts = new List<string>();

            try
            {
                // get all existing cert bindings
                var allCertBindings = new List<Models.BindingInfo>();

                // TODO: reinstate once we have reliable binding info (also some users get an FileNotFound dll loading exception accessing this functionality):
                // if (checkBindings) allCertBindings =  Certify.Utils.Networking.GetCertificateBindings();

                if (string.IsNullOrWhiteSpace(storeName))
                {
                    storeName = DEFAULT_STORE_NAME;
                }

                // get all certificates
                using (var store = GetStore(storeName))
                {
                    OpenStoreForReadWrite(store, storeName);

                    var certsToRemove = new List<X509Certificate2>();
                    foreach (var c in store.Certificates)
                    {
                        // cleanup either has to be expired only or has to be given a list of certificate thumbprints to preserve
                        // if cert is in the exclusion list then cleanup is skipped

                        if (
                            (excludedThumbprints == null && cleanupMode == Models.CertificateCleanupMode.AfterExpiry)
                            ||
                            (excludedThumbprints != null && excludedThumbprints.Any() && !excludedThumbprints.Any(e => e.ToLower() == c.Thumbprint.ToLower()))
                            )
                        {
                            if (cleanupMode == Models.CertificateCleanupMode.AfterExpiry)
                            {
                                // queue removal of existing expired cert with [Certify] text in friendly name.
                                if (
                                     (string.IsNullOrWhiteSpace(matchingName) || (c.FriendlyName.StartsWith(matchingName)))
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
                                    (!string.IsNullOrWhiteSpace(matchingName) && c.FriendlyName.StartsWith(matchingName))
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
                                     (string.IsNullOrWhiteSpace(matchingName) || (c.FriendlyName.StartsWith(matchingName)))
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

                            log?.Information($"Removing old cert: {oldCert.FriendlyName} : {oldCert.Thumbprint}");
                        }
                        catch (Exception exp)
                        {
                            // Couldn't remove it
                            log?.Error("Could not remove cert:" + oldCert.FriendlyName + " " + exp.ToString());
                        }
                    }

                    store.Close();
                }
            }
            catch (Exception exp)
            {
                log?.Error("Failed to perform certificate cleanup: " + exp.ToString());
            }

            return removedCerts;
        }

        /// <summary>
        /// Disable usage of a given certificate within the store. Used to disabled CA intermediate certificates without deleting them (so they don't just get re-imported and enabled again)
        /// </summary>
        /// <param name="thumbprint"></param>
        /// <param name="sourceStore"></param>
        /// <returns></returns>
        public static bool DisableCertificateUsage(string thumbprint, string sourceStore, bool useMachineStore = true)
        {
            var disabled = false;

            using (var store = GetStore(sourceStore, useMachineStore))
            {
                OpenStoreForReadWrite(store, sourceStore);

                foreach (var c in store.Certificates)
                {
                    if (c.Thumbprint == thumbprint)
                    {
                        disabled = Security.WinTrust.WinCrypto.DisableCertificateUsageFlags(c);
                    }
                }

                store.Close();
            }

            return disabled;
        }

        public static bool MoveCertificate(string thumbprint, string sourceStore, string destStore, bool useMachineStore = true)
        {
            var certsToMove = new List<X509Certificate2>();
            using (var store = GetStore(sourceStore, useMachineStore))
            {
                OpenStoreForReadWrite(store, sourceStore);
                foreach (var c in store.Certificates)
                {
                    if (c.Thumbprint == thumbprint)
                    {
                        certsToMove.Add(c);
                    }
                }

                foreach (var c in certsToMove)
                {
                    store.Remove(c);
                }

                store.Close();
            }

            if (certsToMove.Any())
            {
                using (var store = GetStore(destStore, useMachineStore))
                {
                    OpenStoreForReadWrite(store, destStore);
                    foreach (var c in certsToMove)
                    {
                        var foundCerts = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                        if (foundCerts.Count == 0)
                        {
                            store.Add(c);
                        }
                    }

                    store.Close();
                }

                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
