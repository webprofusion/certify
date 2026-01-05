using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certify.Management;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;

namespace Certify.Shared.Core.Utils.PKI
{
    /// <summary>
    /// Terminology from https://en.wikipedia.org/wiki/Chain_of_trust
    /// </summary>
    [Flags]
    public enum ExportFlags
    {
        EndEntityCertificate = 1,
        IntermediateCertificates = 4,
        RootCertificate = 6,
        PrivateKey = 8
    }

    public static class CertUtils
    {
        public static string CertDerToPem(byte[] der)
        {
            using var writer = new StringWriter();
            var certParser = new X509CertificateParser();
            using var pemWriter = new PemWriter(writer);
            pemWriter.WriteObject(certParser.ReadCertificate(der));
            writer.Flush();
            return writer.ToString();
        }

        /// <summary>
        /// Get PEM encoded cert bytes (intermediates only or full chain) from PFX bytes
        /// </summary>
        /// <param name="pfxData"></param>
        /// <param name="pwd">private key password</param>
        /// <param name="flags">Flags for component types to export</param>
        /// <returns></returns>
        public static byte[] GetCertComponentsAsPEMBytes(byte[] pfxData, string pwd, ExportFlags flags)
        {
            var pem = GetCertComponentsAsPEMString(pfxData, pwd, flags);
            return System.Text.Encoding.ASCII.GetBytes(pem);
        }

        public static string GetCertComponentsAsPEMString(byte[] pfxData, string pwd, ExportFlags flags)
        {
            // See also https://www.digicert.com/ssl-support/pem-ssl-creation.htm

            X509Certificate2 cert = null;

            try
            {
#if NET9_0_OR_GREATER
            try
            {
                cert = X509CertificateLoader.LoadPkcs12(pfxData, pwd);
            }
            catch (CryptographicException)
            {
                // try again using blank pwd
                cert = X509CertificateLoader.LoadPkcs12(pfxData, "");
            }
#else
                cert = new X509Certificate2(pfxData, pwd);
#endif

                using var writer = new StringWriter();
                var certParser = new X509CertificateParser();
                var pemWriter = new PemWriter(writer);

                //output in order of private key, primary cert, intermediates, root

                if (flags.HasFlag(ExportFlags.PrivateKey))
                {
                    var key = GetCertKeyPem(pfxData, pwd);
                    writer.Write(key);
                }

                // Try to get certificates from the original PFX chain first
                var originalCerts = GetCertificatesFromPfx(pfxData, pwd);

                // Always build the system chain as we may need it for missing components
                var builtChain = new X509Chain();
                builtChain.Build(cert);

                // Export end entity certificate (leaf) - always prefer from PFX if available
                if (flags.HasFlag(ExportFlags.EndEntityCertificate))
                {
                    if (originalCerts != null && originalCerts.Count > 0)
                    {
                        // Use the end entity cert from PFX
                        pemWriter.WriteObject(originalCerts[0]);
                    }
                    else if (builtChain.ChainElements.Count > 0)
                    {
                        // Fallback to built chain
                        var certBytes = builtChain.ChainElements[0].Certificate.Export(X509ContentType.Cert);
                        pemWriter.WriteObject(certParser.ReadCertificate(certBytes));
                    }
                }

                // Export intermediate certificates - blend from both sources
                if (flags.HasFlag(ExportFlags.IntermediateCertificates))
                {
                    ExportIntermediateCertificates(originalCerts, builtChain, certParser, pemWriter);
                }

                // Export root certificate - typically only available from built chain
                if (flags.HasFlag(ExportFlags.RootCertificate))
                {
                    ExportRootCertificate(originalCerts, builtChain, certParser, pemWriter);
                }

                writer.Flush();

                return writer.ToString();
            }
            finally
            {
                //cleanup cert so temp RSA keys get removed on disk
                cert?.Dispose();
                cert = null;
            }
        }

        /// <summary>
        /// Extract certificates from PFX in their original order without rebuilding the chain
        /// </summary>
        /// <param name="pfxData">PFX file bytes</param>
        /// <param name="pwd">Password for the PFX</param>
        /// <returns>List of certificates in the order they appear in the PFX, or null if extraction fails</returns>
        private static System.Collections.Generic.List<Org.BouncyCastle.X509.X509Certificate> GetCertificatesFromPfx(byte[] pfxData, string pwd)
        {
            try
            {
                var certificates = new System.Collections.Generic.List<Org.BouncyCastle.X509.X509Certificate>();

                var pkcsStore = new Pkcs12StoreBuilder().Build();
                pkcsStore.Load(new MemoryStream(pfxData), pwd.ToCharArray());

                // Find the end entity certificate (the one with a private key)
                var keyAlias = pkcsStore.Aliases
                                        .OfType<string>()
                                        .Where(a => pkcsStore.IsKeyEntry(a))
                                        .FirstOrDefault();

                if (keyAlias != null)
                {
                    // Get the certificate chain in the order stored in the PFX
                    var certChain = pkcsStore.GetCertificateChain(keyAlias);

                    if (certChain != null && certChain.Length > 0)
                    {
                        foreach (var entry in certChain)
                        {
                            certificates.Add(entry.Certificate);
                        }

                        return certificates;
                    }
                }

                // Fallback: if no key entry found or chain is empty, try to get all certificates
                foreach (string alias in pkcsStore.Aliases)
                {
                    if (pkcsStore.IsCertificateEntry(alias))
                    {
                        var certEntry = pkcsStore.GetCertificate(alias);
                        if (certEntry?.Certificate != null)
                        {
                            certificates.Add(certEntry.Certificate);
                        }
                    }
                }

                return certificates.Count > 0 ? certificates : null;
            }
            catch
            {
                // If we fail to extract from PFX, return null to trigger fallback to built chain
                return null;
            }
        }

        /// <summary>
        /// Export intermediate certificates, blending from PFX and built chain sources
        /// </summary>
        private static void ExportIntermediateCertificates(
            System.Collections.Generic.List<Org.BouncyCastle.X509.X509Certificate> originalCerts,
            X509Chain builtChain,
            X509CertificateParser certParser,
            PemWriter pemWriter)
        {
            var exportedThumbprints = new System.Collections.Generic.HashSet<string>();

            // First, export intermediates from the original PFX (if available)
            // These are at positions 1 to (count-2), excluding first (end entity) and last (typically root, if present)
            if (originalCerts != null && originalCerts.Count > 1)
            {
                // Determine how many intermediates we have in the PFX
                // If we have 2 certs, the second could be intermediate or root - we'll export it as intermediate
                // If we have 3+ certs, we exclude the last one (assuming it's root)
                var intermediateCount = originalCerts.Count == 2 ? 1 : originalCerts.Count - 2;

                for (var i = 1; i <= intermediateCount; i++)
                {
                    if (i < originalCerts.Count)
                    {
                        var cert = originalCerts[i];
                        var thumbprint = System.BitConverter.ToString(cert.GetEncoded()).Replace("-", "");

                        pemWriter.WriteObject(cert);
                        exportedThumbprints.Add(thumbprint);
                    }
                }
            }

            // Now supplement with any missing intermediates from the built chain
            // Skip index 0 (end entity) and last index (root)
            for (var i = 1; i < builtChain.ChainElements.Count - 1; i++)
            {
                var chainElement = builtChain.ChainElements[i];
                var certBytes = chainElement.Certificate.Export(X509ContentType.Cert);
                var thumbprint = System.BitConverter.ToString(certBytes).Replace("-", "");

                // Only add if we haven't already exported this certificate
                if (!exportedThumbprints.Contains(thumbprint))
                {
                    pemWriter.WriteObject(certParser.ReadCertificate(certBytes));
                    exportedThumbprints.Add(thumbprint);
                }
            }
        }

        /// <summary>
        /// Export root certificate, preferring built chain as PFX typically doesn't include root
        /// </summary>
        private static void ExportRootCertificate(
            System.Collections.Generic.List<Org.BouncyCastle.X509.X509Certificate> originalCerts,
            X509Chain builtChain,
            X509CertificateParser certParser,
            PemWriter pemWriter)
        {
            Org.BouncyCastle.X509.X509Certificate rootCert = null;

            // Check if the PFX contains what appears to be a root certificate
            // This would be the last certificate in a chain of 3+ certificates
            if (originalCerts != null && originalCerts.Count >= 3)
            {
                var lastCert = originalCerts[originalCerts.Count - 1];

                // Verify it's actually a root (self-signed: issuer == subject)
                if (lastCert.IssuerDN.Equivalent(lastCert.SubjectDN))
                {
                    rootCert = lastCert;
                }
            }

            // If we didn't find a root in the PFX, use the one from the built chain
            if (rootCert == null && builtChain.ChainElements.Count > 0)
            {
                var rootElement = builtChain.ChainElements[builtChain.ChainElements.Count - 1];
                var certBytes = rootElement.Certificate.Export(X509ContentType.Cert);
                rootCert = certParser.ReadCertificate(certBytes);
            }

            // Export the root certificate
            if (rootCert != null)
            {
                pemWriter.WriteObject(rootCert);
            }
        }

        /// <summary>
        /// Get PEM encoded private key bytes from PFX bytes
        /// </summary>
        /// <param name="pfxData"></param>
        /// <param name="pwd"></param>
        /// <returns></returns>
        public static string GetCertKeyPem(byte[] pfxData, string pwd)
        {
            var pkcsStore = new Pkcs12StoreBuilder().Build();
            pkcsStore.Load(new MemoryStream(pfxData), pwd.ToCharArray());

            var keyAlias = pkcsStore.Aliases
                                    .OfType<string>()
                                    .Where(a => pkcsStore.IsKeyEntry(a))
                                    .FirstOrDefault();

            var key = pkcsStore.GetKey(keyAlias).Key;

            using (var writer = new StringWriter())
            {
                new PemWriter(writer).WriteObject(key);
                writer.Flush();
                return writer.ToString();
            }
        }

        /// <summary>
        /// For a given PFX, calculate the Base64Url encoded ARI CertID based on 
        /// base64url(Authority Key Identifier) + "." + base64url(Serial). 
        /// See draft-ietf-acme-ari-03
        /// </summary>
        /// <param name="sourceCert"></param>
        /// <returns>ARI Certificate ID</returns>
        public static string GetARICertIdBase64(X509Certificate2 sourceCert)
        {
            // we use BC for the AKI and Serial because native netfx and dotnet are inconsistent and may returns the serial in reverse
            var cert = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(sourceCert.GetRawCertData());

            return GetARICertIdBase64(cert);
        }

        public static string GetARICertIdBase64(Org.BouncyCastle.X509.X509Certificate cert)
        {
            // https://letsencrypt.org/2024/04/25/guide-to-integrating-ari-into-existing-acme-clients

            try
            {
                var certAKI = AuthorityKeyIdentifier.GetInstance(cert.GetExtensionValue(X509Extensions.AuthorityKeyIdentifier).GetOctets());
                var certAKIbytes = certAKI.GetKeyIdentifier();

                var certSerialBytes = cert.SerialNumber.ToByteArray();
                var certId = $"{Util.ToUrlSafeBase64String(certAKIbytes)}.{Util.ToUrlSafeBase64String(certSerialBytes)}";

                return certId;
            }
            catch (Exception)
            {
                // if we cannot compute the certId (AKI not present etc), return null
                return null;
            }
        }

        /// <summary>
        /// Decode a PEM string containing certificates and/or keys into key-value pairs for debugging
        /// </summary>
        /// <param name="pem">PEM encoded string</param>
        /// <returns>List of dictionaries representing attributes of each object in the PEM</returns>
        public static System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> DecodePemToAttributes(string pem)
        {
            var results = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();

            using (var reader = new StringReader(pem))
            {
                var pemReader = new PemReader(reader);
                object obj;
                while ((obj = pemReader.ReadObject()) != null)
                {
                    var attributes = new System.Collections.Generic.Dictionary<string, object>();

                    if (obj is Org.BouncyCastle.X509.X509Certificate cert)
                    {
                        attributes["type"] = "certificate";
                        attributes["subject"] = cert.SubjectDN.ToString();
                        attributes["issuer"] = cert.IssuerDN.ToString();
                        attributes["serialNumber"] = cert.SerialNumber.ToString();
                        attributes["notBefore"] = cert.NotBefore.ToString("O");
                        attributes["notAfter"] = cert.NotAfter.ToString("O");
                        attributes["signatureAlgorithm"] = cert.SigAlgName;
                        attributes["publicKeyAlgorithm"] = cert.GetPublicKey().GetType().Name;

                        // Extensions
                        var extensions = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
                        foreach (var extOid in cert.GetCriticalExtensionOids())
                        {
                            var oid = new Org.BouncyCastle.Asn1.DerObjectIdentifier(extOid);
                            var extDict = new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["oid"] = extOid,
                                ["critical"] = true,
                                ["value"] = System.Convert.ToBase64String(cert.GetExtensionValue(oid).GetOctets())
                            };
                            extensions.Add(extDict);
                        }

                        foreach (var extOid in cert.GetNonCriticalExtensionOids())
                        {
                            var oid = new Org.BouncyCastle.Asn1.DerObjectIdentifier(extOid);
                            var extDict = new System.Collections.Generic.Dictionary<string, object>
                            {
                                ["oid"] = extOid,
                                ["critical"] = false,
                                ["value"] = System.Convert.ToBase64String(cert.GetExtensionValue(oid).GetOctets())
                            };
                            extensions.Add(extDict);
                        }

                        attributes["extensions"] = extensions;
                    }
                    else if (obj is Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair key)
                    {
                        attributes["type"] = "key";
                        attributes["algorithm"] = key.GetType().Name;
                        if (key.Private is Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters rsaKey)
                        {
                            attributes["keyType"] = "RSA";
                            attributes["modulusLength"] = rsaKey.Modulus.BitLength;
                        }
                        else if (key.Private is Org.BouncyCastle.Crypto.Parameters.ECKeyParameters ecKey)
                        {
                            attributes["keyType"] = "EC";
                            attributes["curve"] = ecKey.Parameters.Curve.ToString();
                        }
                        else if (key.Private is Org.BouncyCastle.Crypto.Parameters.DsaKeyParameters dsaKey)
                        {
                            attributes["keyType"] = "DSA";
                            attributes["parameters"] = dsaKey.Parameters.ToString();
                        }
                    }
                    else
                    {
                        attributes["type"] = "unknown";
                        attributes["objectType"] = obj.GetType().Name;
                    }

                    results.Add(attributes);
                }
            }

            return results;
        }
    }
}
