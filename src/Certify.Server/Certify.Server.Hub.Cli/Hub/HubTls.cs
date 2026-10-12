using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certify.Server.Hub.Cli.Cli;

namespace Certify.Server.Hub.Cli.Hub
{
    /// <summary>
    /// TLS validation for hub connections.
    ///
    /// The CLI sends passwords and API secrets, so unlike an agent's hub connection it validates the hub certificate by
    /// default. A hub on a private CA is trusted with --ca-cert, and --insecure turns validation off for testing.
    /// </summary>
    internal static class HubTls
    {
        public static SocketsHttpHandler CreateHandler(bool insecure, string? caCertPath)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };

            if (insecure)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }
            else if (!string.IsNullOrWhiteSpace(caCertPath))
            {
                var roots = LoadCertificates(caCertPath);
                handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) => IsTrustedByCustomRoots(certificate, chain, errors, roots);
            }

            return handler;
        }

        private static X509Certificate2Collection LoadCertificates(string path)
        {
            if (!File.Exists(path))
            {
                throw CliException.Usage($"CA certificate file not found: {path}");
            }

            var certificates = new X509Certificate2Collection();

            try
            {
                if (File.ReadAllText(path).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
                {
                    certificates.ImportFromPemFile(path);
                }
                else
                {
                    certificates.Add(X509CertificateLoader.LoadCertificateFromFile(path));
                }
            }
            catch (CryptographicException ex)
            {
                throw CliException.Usage($"Could not read a CA certificate from {path} - {ex.Message}");
            }

            return certificates;
        }

        private static bool IsTrustedByCustomRoots(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, X509Certificate2Collection roots)
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            // a custom root only changes which issuers are trusted, the certificate must still be for the host
            if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            {
                return false;
            }

            using var serverCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            using var customChain = new X509Chain();

            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.CustomTrustStore.AddRange(roots);
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            if (chain != null)
            {
                foreach (var element in chain.ChainElements)
                {
                    customChain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }

            return customChain.Build(serverCertificate);
        }
    }
}
