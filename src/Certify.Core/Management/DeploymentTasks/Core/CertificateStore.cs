
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
                Title = "Deploy to Certificate Store (Local Machine)",
                DefaultTitle = "Store Certificate",
                IsExperimental = false,
                UsageType = DeploymentProviderUsage.PostRequest,
                Description = "[NOTE: not normally required as cert is stored automatically by standard auto deployment.] This task stores the certificate in the local Certificate Store with custom name.",
                SupportedContexts = DeploymentContextType.LocalAsService,
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
            var certStoreName = string.IsNullOrEmpty(requestedStore) || requestedStore == "default" ? CertificateManager.DEFAULT_STORE_NAME : requestedStore;

            if (execParams.Credentials?.Any() == true)
            {
                execParams.Log?.Warning("Certificate Store: this task always runs as the Certify service account, the credentials selected for the task are not used.");
            }

            X509Certificate2 storedCert = null;

            var certPwd = "";

            if (!string.IsNullOrWhiteSpace(managedCert?.CertificatePasswordCredentialId))
            {
                var cred = await execParams.CredentialsManager.GetUnlockedCredentialsDictionary(managedCert.CertificatePasswordCredentialId);
                if (cred != null)
                {
                    certPwd = cred["password"];
                }
            }

            try
            {
                storedCert = await CertificateManager.StoreCertificate(
                    managedCert.RequestConfig.PrimaryDomain,
                    managedCert.CertificatePath,
                    isRetry: false,
                    enableRetryBehaviour: _enableCertDoubleImportBehaviour,
                    storeName: certStoreName,
                    customFriendlyName: friendlyName,
                    pwd: certPwd,
                    storeIntermediates: CoreAppSettings.Current.StoreCertificateIntermediates,
                    keyStorageProviderName: CoreAppSettings.Current.WindowsKeyStorageProvider,
                    log: execParams.Log
                   );

                if (storedCert != null)
                {
                    results.Add(new ActionResult("Certificate stored OK", true));
                }
            }
            catch (Exception exp)
            {
                results.Add(new ActionResult("Error storing certificate :: " + exp.Message, false));
            }

            return results;
        }

        public async Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams)
        {
            var results = new List<ActionResult>();

            var requestedStore = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "storetype")?.Value?.Trim().ToLower();

            if (!string.IsNullOrEmpty(requestedStore))
            {
                // check store name is valid

                if (!(requestedStore == "default" || requestedStore == "my" || requestedStore == "webhosting"))
                {
                    results.Add(new ActionResult($"Invalid Certificate Store Name: {requestedStore}", false));
                }
            }

            if (execParams.IsPreviewOnly && !results.Any())
            {
                var certStoreName = string.IsNullOrEmpty(requestedStore) || requestedStore == "default" ? CertificateManager.DEFAULT_STORE_NAME : requestedStore;
                results.Add(new ActionResult($"Would store certificate in Local Certificate Store [{certStoreName}]", true));
            }

            return await Task.FromResult(results);
        }
    }
}
