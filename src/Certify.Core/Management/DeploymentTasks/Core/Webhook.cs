
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Config;

namespace Certify.Providers.DeploymentTasks.Core
{
    public class Webhook : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition) => (currentDefinition ?? Definition);

        public async Task<List<ActionResult>> Execute(ILog log, ManagedCertificate managedCert, DeploymentTaskConfig settings, Dictionary<string, string> credentials, bool isPreviewOnly, DeploymentProviderDefinition definition)
        {
            var config = managedCert.RequestConfig;

            var webHookResult = await Certify.Shared.Utils.Webhook.SendRequest(config, managedCert.LastRenewalStatus != RequestState.Error);
            log.Information($"Webhook invoked: Url: {config.WebhookUrl}, Success: {webHookResult.Success}, StatusCode: {webHookResult.StatusCode}");

            throw new System.NotImplementedException();
        }

        public Task<List<ActionResult>> Validate(ManagedCertificate managedCert, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition)
        {
            throw new System.NotImplementedException();
        }

        static Webhook()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Webhook",
                Title = "Webhook",
                IsExperimental = true,
                Description = "Call a custom webhook on renewal success or failure",
                EnableRemoteOptions = false,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="url", Name="Webhook URL", IsRequired=true, IsCredential=false , Description="The url for the webhook request" },
                     new ProviderParameter{ Key="trigger", Name="Webhook URL", IsRequired=true, IsCredential=false , Description="The trigger for the webhook (None, Success, Error)", OptionsList="None;Success;Error", Value="None" },
                     new ProviderParameter{ Key="method", Name="Http Method", IsRequired=true, IsCredential=false , Description="The http method for the webhook request", OptionsList="GET;POST;", Value="POST" },
                     new ProviderParameter{ Key="contenttype", Name="Content Type", IsRequired=true, IsCredential=false , Description="The http content type header for the webhook request", Value="application/json" },
                     new ProviderParameter{ Key="contentbody", Name="Content Body", IsRequired=true, IsCredential=false , Description="The http body template for the webhook request" },
                }
            };
        }
    }
}
