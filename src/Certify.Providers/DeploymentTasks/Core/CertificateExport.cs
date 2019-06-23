using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models.Config;
using Certify.Models.Providers;
using Certify.Providers.Deployment.Core.Shared;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.X509;

namespace Certify.Providers.DeploymentTasks
{
    // For formats see: https://serverfault.com/questions/9708/what-is-a-pem-file-and-how-does-it-differ-from-other-openssl-generated-key-file
    public class CertificateExport : DeploymentTaskProviderBase, IDeploymentTaskProvider
    {
        public ProviderParameter StorePath = new ProviderParameter();

        /// <summary>
        /// Terminology from https://en.wikipedia.org/wiki/Chain_of_trust
        /// </summary>
        [Flags]
        enum ExportFlags
        {
            EndEntityCertificate = 1,
            IntermediateCertificates = 4,
            RootCertificate = 6,
            PrivateKey = 8
        }

        public static new DeploymentProviderDefinition Definition { get; }

        static private Dictionary<string, string> ExportTypes = new Dictionary<string, string> {
            {"pemcrt", "PEM - Primary Certificate (e.g. .crt)" },
            {"pemchain", "PEM - Intermediate Certificate Chain + Root CA Cert (e.g. .chain)" },
            {"pemkey", "PEM - Private Key (e.g. .key)" },
            {"pemfull", "PEM - Full Certificate Chain" },
            {"pemcrtpartialchain", "PEM - Primary Certficate + Intermediate Certificate Chain (e.g. .crt)" },
            {"pfxfull", "PFX (PKCX#12), Full certificate including private key" }
        };

        static CertificateExport()
        {
            string optionsList = string.Join(";", ExportTypes.Select(e => e.Key + "=" + e.Value));

            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.CertificateExport",
                Title = "Export Certificate",
                IsExperimental = true,
                Description = "Deploy latest certificate to a file (locally or remote)",
                ProviderParameters =

                    new List<ProviderParameter> {
                        new ProviderParameter { Key = "path", Name = "Destination Path", IsRequired = true, IsCredential = false, },
                        new ProviderParameter { Key = "type", Name = "Export As", IsRequired = true, IsCredential = false, Value = "pfx", Type=OptionType.Select, OptionsList = optionsList },
                        }
            };
        }

        public override async Task<List<ActionResult>> Execute(
                ILog log,
                Models.ManagedCertificate managedCert,
                DeploymentTaskConfig settings,
                Dictionary<string, string> credentials,
                bool isPreviewOnly,
                DeploymentProviderDefinition definition = null
            )
        {
            if (definition == null)
            {
                definition = CertificateExport.Definition;
            }
            // prepare collection of files in the required formats

            // copy files to the required destination (local, UNC or SFTP)

            //var sftp = new Deployment.Core.Shared.SftpClient(config);

            var pfxData = File.ReadAllBytes(managedCert.CertificatePath);

            // prepare list of files to copy

            var destPath = settings.Parameters.FirstOrDefault(c => c.Key == "path")?.Value.Trim();
            var exportType = settings.Parameters.FirstOrDefault(c => c.Key == "type")?.Value.Trim();
            var files = new Dictionary<string, byte[]>();

            var certPwd = "";

            if (credentials != null && credentials.Any(c => c.Key == "cert_pwd_key"))
            {
                var credKey = credentials.First(c => c.Key == "cert_pwd_key");

                // TODO: fetch credentials for cert pwd

            }

            //TODO: bytes for each file
            if (exportType == "pfxfull")
            {
                files.Add(destPath, pfxData);
            }
            else if (exportType == "pemkey")
            {
                files.Add(destPath, GetCertComponentsAsPEMBytes(pfxData, certPwd, ExportFlags.PrivateKey));
            }
            else if (exportType == "pemchain")
            {
                files.Add(destPath, GetCertComponentsAsPEMBytes(pfxData, certPwd, ExportFlags.IntermediateCertificates));
            }
            else if (exportType == "pemcrt")
            {
                files.Add(destPath, GetCertComponentsAsPEMBytes(pfxData, certPwd, ExportFlags.EndEntityCertificate));
            }
            else if (exportType == "pemcrtpartialchain")
            {
                files.Add(destPath, GetCertComponentsAsPEMBytes(pfxData, certPwd, ExportFlags.EndEntityCertificate | ExportFlags.IntermediateCertificates));
            }
            else if (exportType == "pemfull")
            {
                files.Add(destPath, GetCertComponentsAsPEMBytes(pfxData, certPwd, ExportFlags.PrivateKey | ExportFlags.EndEntityCertificate | ExportFlags.IntermediateCertificates | ExportFlags.RootCertificate));
            }

            // copy to destination

            bool copiedOk = false;
            if (settings.ChallengeProvider == "Certify.StandardChallenges.SSH")
            {
                // sftp file copy
                var sshConfig = SshClient.GetConnectionConfig(settings, credentials);

                var sftp = new SftpClient(sshConfig);
                string remotePath = destPath;

                if (isPreviewOnly)
                {
                    log.Information($"{definition.Title}: (Preview) would copy file via sftp to {remotePath}");
                }
                else
                {
                    // copy via sftp
                    copiedOk = sftp.CopyLocalToRemote(files);

                    log.Information($"{definition.Title}: copied file via sftp to {remotePath}");
                }
            }
            else if (settings.ChallengeProvider == "Certify.StandardChallenges.Windows")
            {
                // windows remote file copy
            }
            else if (settings.ChallengeProvider == "Certify.StandardChallenges.Local")
            {
                // local file copy (may still require credentials)

                var windowsFileClient = new WindowsNetworkFileClient(null);
                copiedOk = windowsFileClient.CopyLocalToRemote(files);
            }

            return new List<ActionResult>{
                await Task.FromResult(new ActionResult { IsSuccess = copiedOk })
            };
        }

        /// <summary>
        /// Get PEM encoded cert bytes (intermediates only or full chain) from PFX bytes
        /// </summary>
        /// <param name="pfxData"></param>
        /// <param name="pwd">private key password</param>
        /// <param name="flags">Flags for component types to export</param>
        /// <returns></returns>
        private byte[] GetCertComponentsAsPEMBytes(byte[] pfxData, string pwd, ExportFlags flags)
        {
            // See also https://www.digicert.com/ssl-support/pem-ssl-creation.htm

            var cert = new X509Certificate2(pfxData, pwd);
            var chain = new X509Chain();
            chain.Build(cert);

            using (var writer = new StringWriter())
            {
                var certParser = new X509CertificateParser();
                var pemWriter = new PemWriter(writer);

                //output in order of private key, primary cert, intermediates, root

                if (flags.HasFlag(ExportFlags.PrivateKey))
                {
                    var key = GetCertKeyPem(pfxData, pwd);
                    writer.Write(key);
                }

                int i = 0;
                foreach (var c in chain.ChainElements)
                {
                    if (i == 0 && flags.HasFlag(ExportFlags.EndEntityCertificate))
                    {
                        // first cert is end entity cert (primary certificate)
                        var o = c.Certificate.Export(X509ContentType.Cert);
                        pemWriter.WriteObject(certParser.ReadCertificate(o));

                    }
                    else if (i == chain.ChainElements.Count - 1 && flags.HasFlag(ExportFlags.RootCertificate))
                    {
                        // last cert is root ca public cert
                        var o = c.Certificate.Export(X509ContentType.Cert);
                        pemWriter.WriteObject(certParser.ReadCertificate(o));
                    }
                    else if (flags.HasFlag(ExportFlags.IntermediateCertificates))
                    {
                        // intermediate cert(s), if any
                        var o = c.Certificate.Export(X509ContentType.Cert);
                        pemWriter.WriteObject(certParser.ReadCertificate(o));
                    }
                    i++;
                }

                writer.Flush();

                return System.Text.ASCIIEncoding.ASCII.GetBytes(writer.ToString());
            }
        }

        /// <summary>
        /// Get PEM encoded private key bytes from PFX bytes
        /// </summary>
        /// <param name="pfxData"></param>
        /// <param name="pwd"></param>
        /// <returns></returns>
        private string GetCertKeyPem(byte[] pfxData, string pwd)
        {
            using (var s = new MemoryStream(pfxData))
            {

                var pkcsStore = new Pkcs12Store(s, pwd.ToCharArray());
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
        }
    }
}
