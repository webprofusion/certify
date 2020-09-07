using System;
using System.Collections.Generic;
using System.Text;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Config.Migration
{
    public class ImportExportContent
    {
        public List<ManagedCertificate> ManagedCertificates { get; set; }
        public List<EncryptedContent> CertificateFiles { get; set; }
        public List<EncryptedContent> Scripts { get; set; }
        public List<StoredCredential> StoredCredentials { get; set; }
        public List<CertificateAuthority> CertificateAuthorities { get; set; }
    }

    public class ImportExportPackage
    {
        public int FormatVersion { get; set; } = 1;
        public Version SystemVersion { get; set; }
        public string Description { get; set; } = "Certify The Web - Exported App Settings";
        public string SourceName { get; set; }
        public DateTime ExportDate { get; set; }
        public ImportExportContent Content { get; set; }

        public EncryptedContent EncryptionValidation { get; set; }
        public string EncryptionSalt { get; set; }
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

    public class ExportRequest
    {
        public ManagedCertificateFilter Filter { get; set; }
        public ExportSettings Settings { get; set; }
        public bool IsPreviewMode { get; set; }
    }


    public class ImportRequest
    {
        public ImportExportPackage Package { get; set; }
        public ImportSettings Settings { get; set; }
        public bool IsPreviewMode { get; set; }
    }
}
