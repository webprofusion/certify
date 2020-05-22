
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Providers;
using Certify.Models.Config;
using System;
using System.Linq;
using System.Threading;

namespace Certify.Providers.DeploymentTasks.Core
{
    /// <summary>
    /// Provider Webhook deployment task. Webhook testing can be performed with `npx http-echo-server`
    /// </summary>
    public class Webhook : IDeploymentTaskProvider
    {
        public static DeploymentProviderDefinition Definition { get; }
        public DeploymentProviderDefinition GetDefinition(DeploymentProviderDefinition currentDefinition = null) => (currentDefinition ?? Definition);

        static Webhook()
        {
            Definition = new DeploymentProviderDefinition
            {
                Id = "Certify.Providers.DeploymentTasks.Webhook",
                Title = "Webhook",
                IsExperimental = true,
                Description = "Call a custom webhook on renewal success or failure",
                SupportedContexts = DeploymentContextType.LocalAsService,
                UsageType = DeploymentProviderUsage.Any,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="url", Name="Webhook URL", IsRequired=true, IsCredential=false , Description="The url for the webhook request" },
                     new ProviderParameter{ Key="trigger", Name="Webhook Trigger", IsRequired=true, IsCredential=false , Description="The trigger for the webhook (None, Success, Error)", OptionsList="None;Success;Error", Value="None" },
                     new ProviderParameter{ Key="method", Name="Http Method", IsRequired=true, IsCredential=false , Description="The http method for the webhook request", OptionsList="GET;POST;", Value="POST" },
                     new ProviderParameter{ Key="contenttype", Name="Content Type", IsRequired=true, IsCredential=false , Description="The http content type header for the webhook request", Value="application/json" },
                     new ProviderParameter{ Key="contentbody", Name="Content Body", IsRequired=true, IsCredential=false , Description="The http body template for the webhook request" },
                }
            };
        }

        public async Task<List<ActionResult>> Execute(ILog log, object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, bool isPreviewOnly, DeploymentProviderDefinition definition, CancellationToken cancellationToken)
        {
            var managedCert = ManagedCertificate.GetManagedCertificate(subject);

            try
            {
                var webhookConfig = new Shared.Utils.Webhook.WebhookConfig
                {
                    Url = settings.Parameters.FirstOrDefault(p => p.Key == "url")?.Value,
                    Method = settings.Parameters.FirstOrDefault(p => p.Key == "method")?.Value,
                    ContentType = settings.Parameters.FirstOrDefault(p => p.Key == "contenttype")?.Value,
                    ContentBody = settings.Parameters.FirstOrDefault(p => p.Key == "contentbody")?.Value
                };

                if (!isPreviewOnly)
                {
                    var webHookResult = await Certify.Shared.Utils.Webhook.SendRequest(webhookConfig, managedCert, managedCert.LastRenewalStatus != RequestState.Error);

                    var msg = $"Webhook invoked: Url: {webhookConfig.Url}, Success: {webHookResult.Success}, StatusCode: {webHookResult.StatusCode}";

                    log.Information(msg);

                    return new List<ActionResult> { new ActionResult(msg, true) };
                }
                else
                {
                    return await Validate(managedCert, settings, credentials, definition);
                }
            }
            catch (Exception exp)
            {
                return new List<ActionResult> { new ActionResult("Webhook call failed: " + exp.ToString(), false) };
            }
        }

        public async Task<List<ActionResult>> Validate(object subject, DeploymentTaskConfig settings, Dictionary<string, string> credentials, DeploymentProviderDefinition definition)
        {
            var results = new List<ActionResult>();

            var url = settings.Parameters.FirstOrDefault(p => p.Key == "url")?.Value;
            var method = settings.Parameters.FirstOrDefault(p => p.Key == "method")?.Value;

            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var result))
            {
                results.Add(new ActionResult($"The webhook url must be a valid url.", false));
            }

            if (string.IsNullOrEmpty(method))
            {
                results.Add(new ActionResult($"The webhook HTTP method must be a selected.", false));
            }

            return results;
        }

    }
}
