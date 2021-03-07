
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class CertificateStore : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null) => (currentDefinition ?? Definition);

        static CertificateStore()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.CertificateStore",
                Title = "Certificate Store (Local Machine)",
                DefaultTitle = "Store Certificate",
                IsExperimental = false,
                UsageType = DeploymentProviderUsage.PostRequest,
                Description = "Store certificate in the local Certificate Store with custom name. Note that standard Deployment already includes storing the certificate in the local computer store. ",
                SupportedContexts = DeploymentContextType.LocalAsService | DeploymentContextType.LocalAsUser,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="storetype", Name="Store", IsRequired=true, IsCredential=false, OptionsList="default=Default; My=Personal (My); WebHosting=Web Hosting", Value="default"  },
                     new ProviderParameter{ Key="friendlyname", Name="Custom Friendly Name", IsRequired=false, IsCredential=false,  Type= OptionType.String,  Description="(optional) custom friendly name for certificate in store."  },
                }
            };
        }

        private bool _enableCertDoubleImportBehaviour { get; set; } = true;

        public async Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams)
        {
            var results = new List<ActionResult>();

            var managedCert = ManagedCertificate.GetManagedCertificate(execParams.Subject);

            // check settings are valid before proceeding
            var validationResults = await Validate(execParams);
            if (validationResults.Any())
            {
                return validationResults;
            }

            var requestedStore = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "storetype")?.Value?.Trim().ToLower();
            var friendlyName = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "friendlyname")?.Value?.Trim();

            //store cert against primary domain, optionally with custom friendly name
            var certStoreName = CertificateManager.DEFAULT_STORE_NAME;

            if (requestedStore != "default")
            {
                certStoreName = requestedStore;
            }
            else
            {
                certStoreName = "My";
            }

            X509Certificate2 storedCert = null;

            var certPwd = "";

            if (!string.IsNullOrWhiteSpace(managedCert.CertificatePasswordCredentialId))
            {
                var cred = await execParams.CredentialsManager.GetUnlockedCredentialsDictionary(managedCert.CertificatePasswordCredentialId);
                if (cred != null)
                {
                    certPwd = cred["password"];
                }
            }

            if (!execParams.IsPreviewOnly)
            {
                try
                {
                    storedCert = await CertificateManager.StoreCertificate(
                        managedCert.RequestConfig.PrimaryDomain,
                        managedCert.CertificatePath,
                        isRetry: false,
                        enableRetryBehaviour: _enableCertDoubleImportBehaviour,
                        storeName: certStoreName,
                        customFriendlyName: friendlyName,
                        certPwd
                       );

                    if (storedCert != null)
                    {
                        // certHash = storedCert.GetCertHash();

                        results.Add(new ActionResult("Certificate stored OK", true));
                    }
                }
                catch (Exception exp)
                {
                    results.Add(new ActionResult("Error storing certificate :: " + exp.Message, false));
                }
            }
            else
            {
                results.Add(new ActionResult($"Would store certificate in Local Certificate Store [{certStoreName}]", true));
            }

            return results;
        }

        public async Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams)
        {
            var results = new List<ActionResult>();

            var requestedStore = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "storetype")?.Value.Trim().ToLower();
            var friendlyName = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "friendlyname")?.Value;

            if (!string.IsNullOrEmpty(requestedStore))
            {
                // check store name is valid

                if (!(requestedStore == "default" || requestedStore.ToLower() == "my" || requestedStore == "webhosting"))
                {
                    results.Add(new ActionResult($"Invalid Certificate Store Name: {requestedStore}", false));
                }
            }

            return await Task.FromResult(results);
        }

    }
}
