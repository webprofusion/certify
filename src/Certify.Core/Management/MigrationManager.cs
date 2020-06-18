using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Core.Management
{
    public class ImportExportContent
    {
        public List<ManagedCertificate> ManagedCertificates { get; set; }
        public List<EncryptedContent> CertificateFiles { get; set; }
        public List<StoredCredential> StoredCredentials { get; set; }
        public List<CertificateAuthority> CertificateAuthorities { get; set; }
    }

    public class ImportExportPackage
    {
        public int FormatVersion { get; set; } = 1;
        public string Description { get; set; } = "Certify The Web - Exported App Settings";
        public string SourceName { get; set; }

        public DateTime ExportDate { get; set; }

        public ImportExportContent Content { get; set; }
    }

    public class EncryptedContent
    {
        public string Filename { get; set; }
        public byte[] Content { get; set; }
        public string Scheme { get; set; }
    }

    public class ExportSettings
    {
        public bool ExportAllStoredCredentials { get; set; }
        public string EncryptionSecret { get; set; }
    }

    public class ImportSettings
    {
        public string EncryptionSecret { get; set; }
    }

    /// <summary>
    /// Perform/preview import and export
    /// </summary>
    public class MigrationManager
    {
        private ItemManager _itemManager;
        private CredentialsManager _credentialsManager;

        public MigrationManager(ItemManager itemManager, CredentialsManager credentialsManager)
        {
            _itemManager = itemManager;
            _credentialsManager = credentialsManager;
        }

        /// <summary>
        /// Export the managed certificates and related settings for the given filter
        /// </summary>
        /// <param name="filter"></param>
        /// <returns>Package of exported settings</returns>
        public async Task<ImportExportPackage> GetExportPackage(ManagedCertificateFilter filter, ExportSettings settings, bool isPreview)
        {
            var export = new ImportExportPackage
            {
                SourceName = Environment.MachineName,
                ExportDate = DateTime.Now
            };

            // export managed certs, related certificate files, stored credentials

            // deployment tasks with local script or path references will need to copy the scripts separately. Need a summary of items to copy.

            var managedCerts = await new ItemManager().GetManagedCertificates(filter);

            export.Content = new ImportExportContent
            {
                ManagedCertificates = managedCerts,
                CertificateFiles = new List<EncryptedContent>(),
                CertificateAuthorities = new List<CertificateAuthority>(),
                StoredCredentials = new List<StoredCredential>()
            };


            // for each managed cert, export the current certificate files (if present)
            foreach (var c in managedCerts)
            {
                if (!string.IsNullOrEmpty(c.CertificatePath))
                {
                    var certBytes = System.IO.File.ReadAllBytes(c.CertificatePath);

                    var encryptedBytes = EncryptBytes(certBytes, settings.EncryptionSecret);
                    var content = new EncryptedContent { Filename = c.CertificatePath, Scheme = "Default", Content = encryptedBytes };

                    export.Content.CertificateFiles.Add(content);
                }
            }


            // for each managed cert, check used stored credentials (DNS challenges or deployment tasks)

            // for each managed cert, check and summarise used local scripts
            return export;
        }

        private byte[] EncryptBytes(byte[] source, string secret)
        {
            RijndaelManaged rmCrypto = new RijndaelManaged();

            byte[] key = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 };
            byte[] iv = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 };

            rmCrypto.Padding = PaddingMode.PKCS7;
            using (MemoryStream mstream = new MemoryStream())
            using (CryptoStream cryptoStream = new CryptoStream(mstream, rmCrypto.CreateEncryptor(key, iv), CryptoStreamMode.Write))
            {
                
                cryptoStream.Write(source, 0, source.Length);
                cryptoStream.FlushFinalBlock();
                cryptoStream.Close();
                return mstream.ToArray();
            }
        }

        private byte[] DecryptBytes(byte[] source, string secret)
        {
            using (RijndaelManaged rmCrypto = new RijndaelManaged())
            {

                byte[] key = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 };
                byte[] iv = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16 };

                rmCrypto.Padding = PaddingMode.PKCS7;
   

                using (var decryptor = rmCrypto.CreateDecryptor(key, iv))
                {
                    using (var memoryStream = new MemoryStream(source))
                    {
                        using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                        {
                            var decryptedBytes = new byte[source.Length];
                            var decryptedByteCount = cryptoStream.Read(decryptedBytes, 0, decryptedBytes.Length);
                            memoryStream.Close();
                            cryptoStream.Close();

                            return decryptedBytes;
                        }
                    }
                }

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="package"></param>
        /// <param name="isPreviewMode"></param>
        /// <returns></returns>
        public async Task<List<ActionStep>> PerformImport(ImportExportPackage package, ImportSettings settings, bool isPreviewMode)
        {
            // apply import
            var steps = new List<ActionStep>();

            // import managed certs, certificate files, stored credentials, CAs


            // managed certs
            var managedCertImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.ManagedCertificates)
            {
                if (!isPreviewMode)
                {
                    // perform actual import
                }

                managedCertImportSteps.Add(new ActionStep { Title = c.Name, Key = c.Id });
            }
            steps.Add(new ActionStep { Title = "Import Managed Certificates", Category = "Import", Substeps = managedCertImportSteps, Key = "ManagedCerts" });

            // certificate files
            var certFileImportSteps = new List<ActionStep>();
            foreach (var c in package.Content.CertificateFiles)
            {
                var pfxBytes = DecryptBytes(c.Content, settings.EncryptionSecret);

                var cert = new X509Certificate2(pfxBytes);


                if (!isPreviewMode)
                {
                    // perform actual import
                    cert.Verify();
                }
                else
                {
                    // verify cert decrypt
                    cert.Verify();
                }

                certFileImportSteps.Add(new ActionStep { Title = $"Importing PFX {cert.Subject}, expiring {cert.NotAfter}", Key = c.Filename });
            }

            steps.Add(new ActionStep { Title = "Import Certificate Files", Category = "Import", Substeps = certFileImportSteps, Key = "CertFiles" });

            // store and apply current certificates to bindings
            return steps;
        }
    }
}
