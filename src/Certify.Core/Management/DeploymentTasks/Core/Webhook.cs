
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Utils;

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
                IsExperimental = false,
                Description = "Call a custom webhook on renewal success or failure",
                SupportedContexts = DeploymentContextType.LocalAsService,
                UsageType = DeploymentProviderUsage.Any,
                ProviderParameters = new System.Collections.Generic.List<ProviderParameter>
                {
                     new ProviderParameter{ Key="url", Name="Webhook URL", IsRequired=true, IsCredential=false , Description="The url for the webhook request" },
                     new ProviderParameter{ Key="method", Name="Http Method", IsRequired=true, IsCredential=false , Description="The http method for the webhook request", OptionsList="GET;POST;", Value="POST" },
                     new ProviderParameter{ Key="contenttype", Name="Content Type", IsRequired=true, IsCredential=false , Description="The http content type header for the webhook request", Value="application/json" },
                     new ProviderParameter{ Key="contentbody", Name="Content Body", IsRequired=true, IsCredential=false , Description="The http body template for the webhook request" },
                }
            };
        }

        public async Task<List<ActionResult>> Execute(DeploymentTaskExecutionParams execParams)
        {
            var managedCert = ManagedCertificate.GetManagedCertificate(execParams.Subject);

            try
            {
                var webhookConfig = new WebhookConfig
                {
                    Url = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "url")?.Value,
                    Method = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "method")?.Value,
                    ContentType = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "contenttype")?.Value,
                    ContentBody = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "contentbody")?.Value
                };

                if (!execParams.IsPreviewOnly)
                {
                    var webHookResult = await SendRequest(webhookConfig, managedCert, managedCert?.LastRenewalStatus != RequestState.Error);

                    var msg = $"Webhook invoked: Url: {webhookConfig.Url}, Success: {webHookResult.Success}, StatusCode: {webHookResult.StatusCode}";

                    if (webHookResult.Success)
                    {
                        execParams.Log.Information(msg);
                    }
                    else
                    {
                        execParams.Log.Error(msg);
                    }

                    return new List<ActionResult> { new ActionResult(msg, webHookResult.Success) };
                }
                else
                {
                    return await Validate(execParams);
                }
            }
            catch (Exception exp)
            {
                return new List<ActionResult> { new ActionResult("Webhook call failed: " + exp.ToString(), false) };
            }
        }

        public async Task<List<ActionResult>> Validate(DeploymentTaskExecutionParams execParams)
        {
            var results = new List<ActionResult>();

            var url = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "url")?.Value;
            var method = execParams.Settings.Parameters.FirstOrDefault(p => p.Key == "method")?.Value;

            if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var result))
            {
                results.Add(new ActionResult($"The webhook url must be a valid url.", false));
            }

            if (string.IsNullOrEmpty(method))
            {
                results.Add(new ActionResult($"The webhook HTTP method must be selected.", false));
            }
            else if (method != Certify.Models.Utils.Webhook.METHOD_GET && method != Certify.Models.Utils.Webhook.METHOD_POST)
            {
                results.Add(new ActionResult($"The webhook HTTP method must be GET or POST.", false));
            }

            if (execParams.IsPreviewOnly && !results.Any())
            {
                results.Add(new ActionResult($"Would send a webhook {method} request to {url}", true));
            }

            return await Task.FromResult(results);
        }

        /// <summary>
        /// Sends an HTTP Request with the requested parameters 
        /// </summary>
        /// <param name="url"></param>
        /// <param name="method"></param>
        /// <param name="contentType"></param>
        /// <param name="body"></param>
        /// <returns> A named Tuple with Success boolean and int StatusCode of the HTTP Request </returns>
        public static async Task<WebhookResult> SendRequest(WebhookConfig config, ManagedCertificate item, bool forSuccess)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", Management.Util.GetUserAgent());

                HttpRequestMessage message;

                var url = ParseValues(config.Url, item?.RequestConfig, forSuccess, true);

                switch (config.Method)
                {
                    case Certify.Models.Utils.Webhook.METHOD_GET:
                        message = new HttpRequestMessage(HttpMethod.Get, url);
                        break;

                    case Certify.Models.Utils.Webhook.METHOD_POST:
                        message = new HttpRequestMessage(HttpMethod.Post, url)
                        {
                            Content = new StringContent(
                                ParseValues(!string.IsNullOrEmpty(config.ContentBody) ? config.ContentBody : Certify.Models.Utils.Webhook.DEFAULT_BODY, item?.RequestConfig, forSuccess, false),
                                Encoding.UTF8,
                                string.IsNullOrEmpty(config.ContentType) ? "application/json" : config.ContentType
                                )
                        };
                        break;

                    default:
                        throw new ArgumentException("Method must be GET or POST", "method");
                }

                var resp = await client.SendAsync(message);

                return new WebhookResult(resp.IsSuccessStatusCode, (int)resp.StatusCode);
            }
        }

        /// <summary>
        /// Certificate request settings which may be used as template variables. Other settings (such as a custom private key or CSR) are never substituted.
        /// </summary>
        internal static readonly string[] AllowedTemplateProperties =
        {
            nameof(CertRequestConfig.PrimaryDomain),
            nameof(CertRequestConfig.SubjectAlternativeNames),
            nameof(CertRequestConfig.SubjectIPAddresses),
            nameof(CertRequestConfig.ChallengeType),
            nameof(CertRequestConfig.WebsiteRootPath),
            nameof(CertRequestConfig.PreferredChain),
            nameof(CertRequestConfig.AcmeProfile),
            nameof(CertRequestConfig.PreferredExpiryDays),
            nameof(CertRequestConfig.CSRKeyAlg),
            nameof(CertRequestConfig.BindingIPAddress),
            nameof(CertRequestConfig.BindingPort),
            nameof(CertRequestConfig.BindingUseSNI),
            nameof(CertRequestConfig.IncludeCN),
            nameof(CertRequestConfig.DeploymentSiteOption),
            nameof(CertRequestConfig.DeploymentBindingOption),
            nameof(CertRequestConfig.DeploymentTargetType),
        };

        /// <summary>
        /// Provides templating variable replacement for Config values 
        /// </summary>
        /// <param name="template"></param>
        /// <param name="config"></param>
        /// <param name="forSuccess"></param>
        /// <returns></returns>
        internal static string ParseValues(string template, CertRequestConfig config, bool forSuccess, bool url_encode)
        {
            if (string.IsNullOrEmpty(template))
            {
                return template;
            }

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (config != null)
            {
                foreach (var propName in AllowedTemplateProperties)
                {
                    var objValue = typeof(CertRequestConfig).GetProperty(propName).GetValue(config);

                    vars[propName] = objValue is Array array
                        ? string.Join(" ", array.Cast<object>())
                        : Convert.ToString(objValue, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                }

                // ChallengeType can be multiple values, use the first one present
                vars[nameof(CertRequestConfig.ChallengeType)] = config.Challenges?.FirstOrDefault()?.ChallengeType ?? config.ChallengeType ?? "";

                vars[nameof(CertRequestConfig.SubjectAlternativeNames)] = string.Join(",", config.SubjectAlternativeNames ?? new string[] { config.PrimaryDomain });
            }

            vars["success"] = forSuccess ? "true" : "false";

            // process the template and replace values, leaving unknown variables as they are
            return Regex.Replace(template, @"\$(\w+)", m =>
            {
                if (!vars.TryGetValue(m.Groups[1].Value, out var value))
                {
                    return m.Value;
                }

                return url_encode ? WebUtility.UrlEncode(value) : value.Replace(@"\", @"\\");
            });
        }
    }
}
