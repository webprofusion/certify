
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Config;
using Certify.Management;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Linq;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class CertificateStore : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition) => (currentDefinition ?? Definition);

        private bool _enableCertDoubleImportBehaviour { get; set; } = true;

        public async Task<List<ActionResult>> Execute(ILog log, object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, bool isPreviewOnly, DeploymentProviderDefinition definition)
        {
            var results = new List<ActionResult>();

            var managedCert = ManagedCertificate.GetManagedCertificate(subject);

            // check settings are valid before proceeding
            var validationResults = await Validate(managedCert, settings, credentials, definition);
            if (validationResults.Any())
            {
                return validationResults;
            }

            var requestedStore = settings.Parameters.FirstOrDefault(p => p.Key == "storetype")?.Value.Trim().ToLower();
            var friendlyName = settings.Parameters.FirstOrDefault(p => p.Key == "friendlyname")?.Value.Trim();

            //store cert against primary domain, optionally with custom friendly name
            var certStoreName = CertificateManager.DEFAULT_STORE_NAME;

            if (requestedStore != "default")
            {
                certStoreName = requestedStore;
            }

            X509Certificate2 storedCert = null;

            if (!isPreviewOnly)
            {
                try
                {
                    storedCert = await CertificateManager.StoreCertificate(
                        managedCert.RequestConfig.PrimaryDomain,
                        managedCert.CertificatePath,
                        isRetry: false,
                        enableRetryBehaviour: _enableCertDoubleImportBehaviour,
                        storeName: certStoreName,
                        customFriendlyName: friendlyName
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

        public async Task<List<ActionResult>> Validate(object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition)
        {
            var results = new List<ActionResult>();

            var requestedStore = settings.Parameters.FirstOrDefault(p => p.Key == "storetype")?.Value.Trim().ToLower();
            var friendlyName = settings.Parameters.FirstOrDefault(p => p.Key == "friendlyname")?.Value;

            if (!string.IsNullOrEmpty(requestedStore))
            {
                // check store name is valid

                if (!(requestedStore == "default" || requestedStore.ToLower() == "my" || requestedStore == "webhosting"))
                {
                    results.Add(new ActionResult($"Invalid Certificate Store Name: {requestedStore}", false));
                }
            }

            return results;
        }

        static CertificateStore()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.CertificateStore",
                Title = "Certificate Store (Local Machine)",
                DefaultTitle = "Store Certificate",
                IsExperimental = true,
                UsageType = DeploymentProviderUsage.PostRequest,
                Description = "Store certificate in the local Certificate Store",
                EnableRemoteOptions = false,
                RequiresCredentials = false,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="storetype", Name="Store", IsRequired=true, IsCredential=false, OptionsList="default=Default; My=Personal (My); WebHosting=Web Hosting", Value="default"  },
                     new ProviderParameter{ Key="friendlyname", Name="Custom Friendly Name", IsRequired=false, IsCredential=false,  Type= OptionType.String,  Description="(optional) custom friendly name for certificate in store."  },
                }
            };
        }
    }
}
