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

        public static new DeploymentProviderDefinition Definition { get; }

        static private Dictionary<string, string> ExportTypes = new Dictionary<string, string> {
            {"pemcrt", "PEM - Primary Certificate (e.g. .crt)" },
            {"pemchain", "PEM - Intermediate Certificate Chain (e.g. .chain)" },
            {"pemkey", "PEM - Private Key (e.g. .key)" },
            {"pemfull", "PEM - Full Certificate Chain" },
            {"pemchainpartial", "PEM - Primary Certficate + Intermediate Certificate Chain (e.g. .crt)" },
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
                files.Add(destPath, GetCertKeyPem(pfxData, certPwd));
            }
            else if (exportType == "pemchain")
            {
                files.Add(destPath, GetCertChain(pfxData, false));
            }
            else if (exportType == "pemcrt")
            {
                files.Add(destPath, GetCertPem(pfxData, false));
            }
            else if (exportType == "pemchainpartial")
            {
                files.Add(destPath, GetCertChain(pfxData, true));
            }

            // copy to destination

            bool copiedOk = false;
            if (settings.ChallengeProvider == "Certify.StandardChallenges.SSH")
            {
                // sftp file copy
                var sshConfig = SshClient.GetConnectionConfig(settings, credentials);

                var sftp = new SftpClient(sshConfig);
                string remotePath = "/";

                if (isPreviewOnly)
                {
                    log.Information($"{definition.Title}: (Preview) would copy file via sftp to {remotePath}");
                }
                else
                {
                    // copy via sftp
                    var copiedOK = sftp.CopyLocalToRemote(new Dictionary<string, string>
                    {
                        {managedCert.CertificatePath, remotePath }
                    });

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
        /// <param name="fullChain"></param>
        /// <returns></returns>
        private byte[] GetCertChain(byte[] pfxData, bool fullChain)
        {
            // See also https://www.digicert.com/ssl-support/pem-ssl-creation.htm


            var certCollection = new X509Certificate2Collection();
            certCollection.Import(pfxData);

            var leafCert = certCollection[certCollection.Count - 1];

            using (var writer = new StringWriter())
            {
                var certParser = new X509CertificateParser();
                var pemWriter = new PemWriter(writer);

                foreach (var c in certCollection)
                {
                    if (c.Thumbprint != leafCert.Thumbprint || fullChain == true)
                    {
                        var export = c.Export(X509ContentType.Cert);

                        var o = certParser.ReadCertificate(export);
                        pemWriter.WriteObject(o);
                    }
                }

                return System.Text.ASCIIEncoding.ASCII.GetBytes(writer.ToString());
            }
        }

        /// <summary>
        /// Get PEM encoded cert bytes from PFX bytes
        /// </summary>
        /// <param name="pfxData"></param>
        /// <param name="fullChain"></param>
        /// <returns></returns>
        private byte[] GetCertPem(byte[] pfxData, bool fullChain)
        {

            var certCollection = new X509Certificate2Collection();
            certCollection.Import(pfxData);

            var leafCert = certCollection[certCollection.Count - 1];

            using (var writer = new StringWriter())
            {
                var certParser = new X509CertificateParser();
                var pemWriter = new PemWriter(writer);

                var export = leafCert.Export(X509ContentType.Cert);

                var o = certParser.ReadCertificate(export);
                pemWriter.WriteObject(o);

                return System.Text.ASCIIEncoding.ASCII.GetBytes(writer.ToString());
            }
        }

        /// <summary>
        /// Get PEM encoded private key bytes from PFX bytes
        /// </summary>
        /// <param name="pfxData"></param>
        /// <param name="pwd"></param>
        /// <returns></returns>
        private byte[] GetCertKeyPem(byte[] pfxData, string pwd)
        {

            using (var s = new MemoryStream(pfxData))
            {

                var pkcs12store = new Pkcs12Store(s, pwd.ToCharArray());
                var keyAlias = pkcs12store.Aliases
                                        .OfType<string>()
                                        .Where(a => pkcs12store.IsKeyEntry(a))
                                        .FirstOrDefault();

                var key = pkcs12store.GetKey(keyAlias).Key;

                if (key.IsPrivate)
                {
                    using (var writer = new StringWriter())
                    {
                        var pemWriter = new PemWriter(writer);

                        pemWriter.WriteObject(key);
                        writer.Flush();
                        return System.Text.ASCIIEncoding.ASCII.GetBytes(writer.ToString());
                    }
                }

                // no key found
                return null;
            }
        }
    }
}
