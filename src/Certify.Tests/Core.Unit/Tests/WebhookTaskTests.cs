using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Certify.Config;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers.DeploymentTasks;
using Certify.Providers.DeploymentTasks.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Webhook task template variables and validation
    /// </summary>
    [TestClass]
    public class WebhookTaskTests
    {
        private static CertRequestConfig GetConfig() => new CertRequestConfig
        {
            PrimaryDomain = "example.com",
            SubjectAlternativeNames = new[] { "example.com", "www.example.com" },
            WebsiteRootPath = @"C:\inetpub\wwwroot",
            CustomPrivateKey = "-----BEGIN PRIVATE KEY-----secret",
            CustomCSR = "-----BEGIN CERTIFICATE REQUEST-----csr",
            PostRequestPowerShellScript = @"C:\scripts\secret.ps1",
            Challenges = new ObservableCollection<CertRequestChallengeConfig> { new CertRequestChallengeConfig { ChallengeType = "dns-01" } }
        };

        [TestMethod, Description("A variable at the very end of the template is replaced")]
        public void TestVariableAtEndOfTemplateIsReplaced()
        {
            var url = Webhook.ParseValues("https://hooks.example.com/notify?domain=$PrimaryDomain", GetConfig(), forSuccess: true, url_encode: true);

            Assert.AreEqual("https://hooks.example.com/notify?domain=example.com", url);

            Assert.AreEqual("false", Webhook.ParseValues("$Success", GetConfig(), forSuccess: false, url_encode: false));
        }

        [TestMethod, Description("Variable names are case insensitive and special values are computed")]
        public void TestSpecialVariables()
        {
            var body = Webhook.ParseValues("{\"ok\":\"$success\",\"sans\":\"$SubjectAlternativeNames\",\"type\":\"$CHALLENGETYPE\",\"root\":\"$WebsiteRootPath\"}", GetConfig(), forSuccess: true, url_encode: false);

            Assert.AreEqual("{\"ok\":\"true\",\"sans\":\"example.com,www.example.com\",\"type\":\"dns-01\",\"root\":\"C:\\\\inetpub\\\\wwwroot\"}", body);
        }

        [TestMethod, Description("Values substituted into the URL are URL encoded, including computed values")]
        public void TestUrlValuesAreEncoded()
        {
            var url = Webhook.ParseValues("https://hooks.example.com/?sans=$SubjectAlternativeNames", GetConfig(), forSuccess: true, url_encode: true);

            Assert.AreEqual("https://hooks.example.com/?sans=example.com%2Cwww.example.com", url);
        }

        [TestMethod, Description("Settings outside the allowed list, such as a custom private key or CSR, are never substituted")]
        public void TestSensitiveSettingsAreNotSubstituted()
        {
            var template = "$CustomPrivateKey $CustomCSR $PostRequestPowerShellScript $WebhookUrl $Unknown";

            var body = Webhook.ParseValues(template, GetConfig(), forSuccess: true, url_encode: false);

            Assert.AreEqual(template, body);
        }

        [TestMethod, Description("Every allowed variable resolves to a CertRequestConfig property")]
        public void TestAllowedVariablesExist()
        {
            foreach (var name in Webhook.AllowedTemplateProperties)
            {
                Assert.IsNotNull(typeof(CertRequestConfig).GetProperty(name), name);
            }
        }

        [TestMethod, Description("The template is processed when the managed certificate has no request config")]
        public void TestNullConfig()
        {
            Assert.AreEqual("true $PrimaryDomain", Webhook.ParseValues("$Success $PrimaryDomain", null, forSuccess: true, url_encode: false));
        }

        [TestMethod, Description("The unused Webhook Trigger parameter is no longer part of the task definition")]
        public void TestDefinitionHasNoTriggerParameter()
        {
            Assert.IsFalse(Webhook.Definition.ProviderParameters.Any(p => p.Key == "trigger"));
        }

        [TestMethod, Description("Validate checks the method and describes the request when previewing")]
        public async Task TestValidate()
        {
            var provider = new Webhook();

            var invalid = await provider.Validate(GetExecParams("https://hooks.example.com/", "PUT", isPreviewOnly: true));
            Assert.AreEqual(1, invalid.Count);
            Assert.IsFalse(invalid[0].IsSuccess);

            var preview = await provider.Validate(GetExecParams("https://hooks.example.com/", "POST", isPreviewOnly: true));
            Assert.AreEqual(1, preview.Count);
            Assert.IsTrue(preview[0].IsSuccess);
            StringAssert.Contains(preview[0].Message, "Would send a webhook POST request");

            var valid = await provider.Validate(GetExecParams("https://hooks.example.com/", "GET", isPreviewOnly: false));
            Assert.AreEqual(0, valid.Count);
        }

        private static DeploymentTaskExecutionParams GetExecParams(string url, string method, bool isPreviewOnly)
        {
            var config = new DeploymentTaskConfig
            {
                TaskTypeId = StandardTaskTypes.WEBHOOK,
                Parameters = new List<ProviderParameterSetting>
                {
                    new("url", url),
                    new("method", method)
                }
            };

            return new DeploymentTaskExecutionParams(null, null, new ManagedCertificate { RequestConfig = GetConfig() }, config, null, isPreviewOnly, Webhook.Definition, new DeploymentContext(), CancellationToken.None);
        }
    }
}
