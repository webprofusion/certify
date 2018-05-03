using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Certify.Core.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class BindingMatchTests
    {
        public List<BindingInfo> _allSites { get; set; }

        [TestInitialize]
        public void Setup()
        {
            _allSites = new List<BindingInfo>
            {
                // Site 1 : test.com and www.test.com bindings, both existing https
                new BindingInfo{ Name="TestDotCom", Host="test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="1"},
                new BindingInfo{ Name="TestDotCom", Host="www.test.com", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="1"},
                // Site 2 : test.co.uk and www.test.co.uk bindings, no existing https
                new BindingInfo{ Name="Test.co.uk", Host="test.co.uk", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="2"},
                new BindingInfo{ Name="Test.co.uk", Host="www.test.co.uk", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="2"},
                // Site 3 : test.com.au and www.test.com.au bindings, http and existing https
                new BindingInfo{ Name="Test.com.au", Host="test.com.au", IP="*", HasCertificate=true, Protocol="https", Port=443, Id="3"},
                new BindingInfo{ Name="Test.com.au", Host="www.test.com.au", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="3"},
                         new BindingInfo{ Name="Test.com.au", Host="dev.www.test.com.au", IP="*", HasCertificate=true, Protocol="http", Port=80, Id="3"}
            };
        }

        [TestMethod, Description("Ensure binding add/update decisions are correct based on deployment criteria")]
        public async Task BindingMatchTestsVarious()
        {
            /*
                Single Site - Default Match
                www.test.com
                test.com*/

            /*

            All Sites(*.test.com)
            All Sites(*.test.co.uk)
            */

            var managedCertificate = new ManagedCertificate

            {
                Id = Guid.NewGuid().ToString(),
                Name = "TestSite..",
                GroupId = "test",

                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "*.test.com",
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                        new List<CertRequestChallengeConfig>
                        {
                            new CertRequestChallengeConfig{
                                ChallengeType="dns-01"
                            }
                        }),
                    PerformAutomatedCertBinding = true,
                    WebsiteRootPath = "c:\\inetpub\\wwwroot"
                },
                ItemType = ManagedCertificateType.SSL_LetsEncrypt_LocalIIS
            };

            var bindingManager = new BindingDeploymentManager();

            var deploymentTarget = new MockBindingDeploymentTarget();

            deploymentTarget.AllBindings = _allSites;

            var preview = await bindingManager.StoreAndDeployManagedCertificate(deploymentTarget, managedCertificate, null, false, isPreviewOnly: true);

            foreach (var a in preview)
            {
                System.Diagnostics.Debug.WriteLine(a.Description);
            }
        }
    }
}
