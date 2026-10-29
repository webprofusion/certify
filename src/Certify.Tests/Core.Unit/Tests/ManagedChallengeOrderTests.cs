using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for a hub's own certificate orders using its managed challenges (the ManagedAcme and
    /// DNS01.ManagedChallengeHub providers), as opposed to remote callers of the managed challenge API.
    /// </summary>
    [TestClass]
    public class ManagedChallengeOrderTests
    {
        private CertifyManager _manager;
        private MemoryObjectStore _store;
        private MockLog _log;

        [TestInitialize]
        public void Setup()
        {
            _store = new MemoryObjectStore();
            _manager = new CertifyManager();
            _log = new MockLog();

            typeof(CertifyManager)
                .GetField("_configStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_manager, _store);

            typeof(CertifyManager)
                .GetProperty("_serviceLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_manager, _log);

            // the hub writes through the challenge's DNS provider, which is resolved from the loaded plugins
            new PluginManager().LoadPlugins([PluginManager.PLUGINS_DNS_PROVIDERS]);
        }

        private static PendingAuthorization Authorization(string identifier) => new()
        {
            Identifier = new CertIdentifierItem { IdentifierType = CertIdentifierType.Dns, Value = identifier },
            Challenges =
            [
                new AuthorizationChallengeItem
                {
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    Key = $"_acme-challenge.{identifier}",
                    Value = $"token-{identifier}"
                }
            ]
        };

        /// <summary>
        /// Only the first identifier's challenge used to be performed, so an order for app.example.com and
        /// www.app.example.com failed validation for www on its first attempt and succeeded on the retry, once the
        /// first authorization was valid. The propagation wait also ran inside the challenge step, once per
        /// identifier, rather than in the order's Propagation stage.
        /// </summary>
        [TestMethod]
        [Description("A hub's own order performs a managed challenge for every identifier and leaves the propagation wait to the order")]
        public async Task PrepareChallenges_ManagedChallengeHub_PerformsEveryIdentifier()
        {
            await _store.Add<ManagedChallenge>(nameof(ManagedChallenge), new ManagedChallenge
            {
                Id = "challenge-example",
                Title = "example.com",
                ChallengeConfig = new CertRequestChallengeConfig
                {
                    ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                    DomainMatch = "*.example.com",
                    ChallengeProvider = "DNS01.Manual"
                }
            });

            var managedCertificate = new ManagedCertificate
            {
                Id = "cert-1",
                Name = "app.example.com",
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = "app.example.com",
                    SubjectAlternativeNames = ["app.example.com", "www.example.com", "other.test"],
                    Challenges = new ObservableCollection<CertRequestChallengeConfig>(
                    [
                        new CertRequestChallengeConfig
                        {
                            ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_DNS,
                            ChallengeProvider = "DNS01.ManagedChallengeHub"
                        }
                    ])
                }
            };

            var authorizations = new List<PendingAuthorization>
            {
                Authorization("app.example.com"),
                Authorization("www.example.com"),
                Authorization("other.test")
            };

            await _manager.PrepareAutomatedChallengeResponses(_log, managedCertificate, authorizations, new CertificateRequestResult(managedCertificate), managedCertificate.RequestConfig, progress: null);

            var app = authorizations[0];
            var www = authorizations[1];
            var other = authorizations[2];

            Assert.IsFalse(app.IsFailure, app.AuthorizationError);
            Assert.IsFalse(www.IsFailure, www.AuthorizationError);
            Assert.IsNotNull(app.AttemptedChallenge);
            Assert.IsNotNull(www.AttemptedChallenge);

            Assert.AreEqual(1, _log.LogEntries.Count(e => e.Contains("Creating TXT Record '_acme-challenge.app.example.com'")));
            Assert.AreEqual(1, _log.LogEntries.Count(e => e.Contains("Creating TXT Record '_acme-challenge.www.example.com'")));

            // the manual provider reports no propagation delay of its own, which is not passed on as a negative wait
            Assert.AreEqual(0, app.AttemptedChallenge.PropagationSeconds);

            // no managed challenge matches, so the order stops before submitting challenges to the CA
            Assert.IsTrue(other.IsFailure);
            StringAssert.Contains(other.AuthorizationError, "No matching challenge found");
        }
    }
}
