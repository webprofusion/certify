using System.Collections.Generic;
using System.Linq;
using Certify.Models.Config;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class ProviderParameterTests
    {
        private static List<ProviderParameter> GetParameters(string typeValue) =>
        [
            new ProviderParameter { Key = "path", Value = "c:\\temp\\cert.pfx" },
            new ProviderParameter { Key = "type", Value = typeValue, Type = OptionType.Select },
            new ProviderParameter { Key = "export_pwd", Value = "credential-key", DependsOnKey = "type", DependsOnValues = ["pfxfull", "pemkey"] }
        ];

        [TestMethod, Description("A parameter applies only while the parameter it depends on has one of the listed values")]
        [DataRow("pfxfull", true)]
        [DataRow("pemkey", true)]
        [DataRow("pemcrt", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void TestIsApplicable(string typeValue, bool expected)
        {
            var parameters = GetParameters(typeValue);

            Assert.AreEqual(expected, parameters.First(p => p.Key == "export_pwd").IsApplicable(parameters));
            Assert.IsTrue(parameters.First(p => p.Key == "path").IsApplicable(parameters), "A parameter without a dependency always applies");
        }

        [TestMethod, Description("A parameter depending on a parameter which is not present does not apply")]
        public void TestIsApplicableWithMissingDependency()
        {
            var parameters = GetParameters("pfxfull").Where(p => p.Key != "type").ToList();

            Assert.IsFalse(parameters.First(p => p.Key == "export_pwd").IsApplicable(parameters));
        }

        [TestMethod, Description("Settings to store omit parameters which no longer apply")]
        public void TestGetApplicableSettings()
        {
            var applicable = ProviderParameter.GetApplicableSettings(GetParameters("pfxfull"));

            CollectionAssert.AreEqual(new[] { "path", "type", "export_pwd" }, applicable.Select(s => s.Key).ToArray());
            Assert.AreEqual("credential-key", applicable.First(s => s.Key == "export_pwd").Value);

            var notApplicable = ProviderParameter.GetApplicableSettings(GetParameters("pemcrt"));

            CollectionAssert.AreEqual(new[] { "path", "type" }, notApplicable.Select(s => s.Key).ToArray(), "A value which no longer applies should be cleared");
        }

        [TestMethod, Description("Clone copies the parameter dependency")]
        public void TestCloneCopiesDependency()
        {
            var original = GetParameters("pfxfull").First(p => p.Key == "export_pwd");

            var clone = (ProviderParameter)original.Clone();

            Assert.AreEqual(original.DependsOnKey, clone.DependsOnKey);
            CollectionAssert.AreEqual(original.DependsOnValues, clone.DependsOnValues);
            Assert.AreNotSame(original.DependsOnValues, clone.DependsOnValues);
        }
    }
}
