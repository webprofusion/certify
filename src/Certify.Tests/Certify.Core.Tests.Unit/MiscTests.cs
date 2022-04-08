using System;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class MiscTests
    {

        public MiscTests()
        {

        }

        [TestMethod, Description("Ensure source config with nulls deserialises as empty string instead of null")]
        public void TestLoadManagedCertificates()
        {
            var json = @"{ 'Id':null,'Title':null,'Description':null,'HelpUrl':'','ProviderParameters':[],'Config':null,'IsExperimental':false,'IsEnabled':true,'IsTestModeSupported':true,'HasDynamicParameters':false}";
            json = json.Replace("'", "\"");

            var provider = JsonConvert.DeserializeObject<ProviderDefinition>(json, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            Assert.AreEqual(String.Empty, provider.Id);

            provider = JsonConvert.DeserializeObject<ProviderDefinition>(json);
            Assert.IsNull(provider.Id);

            provider = System.Text.Json.JsonSerializer.Deserialize<ProviderDefinition>(json, new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            Assert.AreEqual(String.Empty, provider.Id);

            provider = System.Text.Json.JsonSerializer.Deserialize<ProviderDefinition>(json);
            Assert.IsNull(provider.Id);

        }

        [TestMethod, Description("Test null/blank coalesce of string")]
        public void TestNullOrBlankCoalesce()
        {
            string testValue = null;

            var result = testValue.WithDefault("ok");
            Assert.AreEqual(result, "ok");

            testValue = "test";
            result = testValue.WithDefault("ok");
            Assert.AreEqual(result, "test");

            var ca = new Models.CertificateAuthority();
            ca.Description = null;
            result = ca.Description.WithDefault("default");
            Assert.AreEqual(result, "default");

            ca = null;
            result = ca?.Description.WithDefault("default");
            Assert.AreEqual(result, null);
        }
    }
}
