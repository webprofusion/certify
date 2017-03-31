using ACMESharp;
using ACMESharp.JOSE;
using ACMESharp.PKI;
using ACMESharp.PKI.RSA;
using ACMESharp.POSH.Util;
using ACMESharp.Util;
using ACMESharp.Vault;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Profile;
using ACMESharp.Vault.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Certify.ACMESharpCompat
{
    public class ACMESharpUtils
    {
        //these elements are adpated from ACMESHarp.POSH and related projects
        public const string WELL_KNOWN_LE = "LetsEncrypt";
        public const string WELL_KNOWN_LESTAGE = "LetsEncrypt-STAGING";

        public static readonly IReadOnlyDictionary<string, string> WELL_KNOWN_BASE_SERVICES =
                new ReadOnlyDictionary<string, string>(new IndexedDictionary<string, string>
                {
                    { WELL_KNOWN_LE, "https://acme-v01.api.letsencrypt.org/" },
                    { WELL_KNOWN_LESTAGE, "https://acme-staging.api.letsencrypt.org/"},
                });

        public static IVault GetVault(string profileName = null)
        {
            profileName = VaultProfileManager.ResolveProfileName(profileName);
            if (string.IsNullOrEmpty(profileName))
                throw new InvalidOperationException("unable to resolve effective profile name");

            var profile = VaultProfileManager.GetProfile(profileName);
            if (profile == null)
                throw new InvalidOperationException("unable to resolve effective profile")
                        .With(nameof(profileName), profileName);

            var provider = VaultExtManager.GetProvider(profile.ProviderName, null);
            if (provider == null)
                throw new InvalidOperationException("unable to resolve Vault Provider")
                        .With(nameof(profileName), profileName)
                        .With(nameof(profile.ProviderName), profile.ProviderName);

            return provider.GetVault(profile.VaultParameters);
        }

        public static IPkiTool GetPkiTool(string name)
        {
            return string.IsNullOrEmpty(name)
                ? PkiToolExtManager.GetPkiTool()
                : PkiToolExtManager.GetPkiTool(name);
        }

        public static CertificateInfo SubmitCertificate(string certificateRef, string pkiTool = null, string vaultProfile= null)
        {
            bool force = false;

            using (var vlt = GetVault(vaultProfile))
            {
                vlt.OpenStorage();
                var v = vlt.LoadVault();

                if (v.Registrations == null || v.Registrations.Count < 1)
                    throw new InvalidOperationException("No registrations found");

                var ri = v.Registrations[0];
                var r = ri.Registration;

                if (v.Certificates == null || v.Certificates.Count < 1)
                    throw new InvalidOperationException("No certificates found");

                var ci = v.Certificates.GetByRef(certificateRef, throwOnMissing: false);
                if (ci == null)
                    throw new Exception("Unable to find a Certificate for the given reference");

                using (var cp = GetPkiTool(
                            StringHelper.IfNullOrEmpty(pkiTool, v.PkiTool)))
                {
                    if (!string.IsNullOrEmpty(ci.GenerateDetailsFile))
                    {
                        // Generate a private key and CSR:
                        //    Key:  RSA 2048-bit
                        //    MD:   SHA 256
                        //    CSR:  Details pulled from CSR Details JSON file

                        CsrDetails csrDetails;
                        var csrDetailsAsset = vlt.GetAsset(VaultAssetType.CsrDetails, ci.GenerateDetailsFile);
                        using (var s = vlt.LoadAsset(csrDetailsAsset))
                        {
                            csrDetails = JsonHelper.Load<CsrDetails>(s);
                        }

                        var keyGenFile = $"{ci.Id}-gen-key.json";
                        var keyPemFile = $"{ci.Id}-key.pem";
                        var csrGenFile = $"{ci.Id}-gen-csr.json";
                        var csrPemFile = $"{ci.Id}-csr.pem";

                        var keyGenAsset = vlt.CreateAsset(VaultAssetType.KeyGen, keyGenFile, getOrCreate: force);
                        var keyPemAsset = vlt.CreateAsset(VaultAssetType.KeyPem, keyPemFile, isSensitive: true, getOrCreate: force);
                        var csrGenAsset = vlt.CreateAsset(VaultAssetType.CsrGen, csrGenFile, getOrCreate: force);
                        var csrPemAsset = vlt.CreateAsset(VaultAssetType.CsrPem, csrPemFile, getOrCreate: force);

                        var genKeyParams = new RsaPrivateKeyParams();

                        var genKey = cp.GeneratePrivateKey(genKeyParams);
                        using (var s = vlt.SaveAsset(keyGenAsset))
                        {
                            cp.SavePrivateKey(genKey, s);
                        }
                        using (var s = vlt.SaveAsset(keyPemAsset))
                        {
                            cp.ExportPrivateKey(genKey, EncodingFormat.PEM, s);
                        }

                        // TODO: need to surface details of the CSR params up higher
                        var csrParams = new CsrParams
                        {
                            Details = csrDetails
                        };
                        var genCsr = cp.GenerateCsr(csrParams, genKey, Crt.MessageDigest.SHA256);
                        using (var s = vlt.SaveAsset(csrGenAsset))
                        {
                            cp.SaveCsr(genCsr, s);
                        }
                        using (var s = vlt.SaveAsset(csrPemAsset))
                        {
                            cp.ExportCsr(genCsr, EncodingFormat.PEM, s);
                        }

                        ci.KeyPemFile = keyPemFile;
                        ci.CsrPemFile = csrPemFile;
                    }

                    byte[] derRaw;

                    var asset = vlt.GetAsset(VaultAssetType.CsrPem, ci.CsrPemFile);
                    // Convert the stored CSR in PEM format to DER
                    using (var source = vlt.LoadAsset(asset))
                    {
                        var csr = cp.ImportCsr(EncodingFormat.PEM, source);
                        using (var target = new MemoryStream())
                        {
                            cp.ExportCsr(csr, EncodingFormat.DER, target);
                            derRaw = target.ToArray();
                        }
                    }

                    var derB64u = JwsHelper.Base64UrlEncode(derRaw);

                    try
                    {
                        using (var c = ClientHelper.GetClient(v, ri))
                        {
                            c.Init();
                            c.GetDirectory(true);

                            ci.CertificateRequest = c.RequestCertificate(derB64u);
                        }
                    }
                    catch (AcmeClient.AcmeWebException ex)
                    {
                        //throw new Exce(PoshHelper.CreateErrorRecord(ex, ci));
                        //TODO: parse exception
                        throw ex;
                        //return;
                    }

                    if (!string.IsNullOrEmpty(ci.CertificateRequest.CertificateContent))
                    {
                        var crtDerFile = $"{ci.Id}-crt.der";
                        var crtPemFile = $"{ci.Id}-crt.pem";

                        var crtDerBytes = ci.CertificateRequest.GetCertificateContent();

                        var crtDerAsset = vlt.CreateAsset(VaultAssetType.CrtDer, crtDerFile);
                        var crtPemAsset = vlt.CreateAsset(VaultAssetType.CrtPem, crtPemFile);

                        using (Stream source = new MemoryStream(crtDerBytes),
                                derTarget = vlt.SaveAsset(crtDerAsset),
                                pemTarget = vlt.SaveAsset(crtPemAsset))
                        {
                            var crt = cp.ImportCertificate(EncodingFormat.DER, source);

                            cp.ExportCertificate(crt, EncodingFormat.DER, derTarget);
                            ci.CrtDerFile = crtDerFile;

                            cp.ExportCertificate(crt, EncodingFormat.PEM, pemTarget);
                            ci.CrtPemFile = crtPemFile;
                        }

                        // Extract a few pieces of info from the issued
                        // cert that we like to have quick access to
                        var x509 = new X509Certificate2(ci.CertificateRequest.GetCertificateContent());
                        ci.SerialNumber = x509.SerialNumber;
                        ci.Thumbprint = x509.Thumbprint;
                        ci.SignatureAlgorithm = x509.SignatureAlgorithm?.FriendlyName;
                        ci.Signature = x509.GetCertHashString();
                    }
                }

                vlt.SaveVault(v);
                return ci;
            }
        }

        internal static void UpdateIdentifier(string domainIdentifierAlias)
        {
            throw new NotImplementedException();
        }

        internal static object NewCertificate(string certAlias, string domainIdentifierAlias, string[] subjectAlternativeNameIdentifiers)
        {
            throw new NotImplementedException();
        }

        internal static void NewIdentifier(string identifierAlias, string domain, string v)
        {
            throw new NotImplementedException();
        }

        internal static void NewRegistration(string contact)
        {
            throw new NotImplementedException();
        }

        internal static void RegistrationAcceptTOS()
        {
            throw new NotImplementedException();
        }
    }
}
