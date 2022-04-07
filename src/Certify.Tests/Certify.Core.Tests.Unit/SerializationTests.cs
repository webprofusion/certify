using System;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class SerializationTests
    {

        public SerializationTests()
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

    }
}
