using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The hub reports each managed certificate's last used CA and key type, so its items can be filtered and counted by them
    /// </summary>
    [TestClass]
    public class HubManagedItemGroupingTests
    {
        private const string CallerId = "sp-admin";
        private const string InstanceId = "instance-1";

        [TestMethod]
        public void GetKeyType_ReadsTheKeyTypeOfTheCertificate()
        {
            using var rsa2048 = RSA.Create(2048);
            using var rsa3072 = RSA.Create(3072);
            using var ec256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var ec384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

            Assert.AreEqual(StandardKeyTypes.RSA256, CertUtils.GetKeyType(SelfSigned(new CertificateRequest("CN=test", rsa2048, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));
            Assert.AreEqual(StandardKeyTypes.RSA256_3072, CertUtils.GetKeyType(SelfSigned(new CertificateRequest("CN=test", rsa3072, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));
            Assert.AreEqual(StandardKeyTypes.ECDSA256, CertUtils.GetKeyType(SelfSigned(new CertificateRequest("CN=test", ec256, HashAlgorithmName.SHA256))));
            Assert.AreEqual(StandardKeyTypes.ECDSA384, CertUtils.GetKeyType(SelfSigned(new CertificateRequest("CN=test", ec384, HashAlgorithmName.SHA384))));
        }

        [TestMethod]
        public void GetDisplayName_NamesStandardAndOtherRsaKeyTypes()
        {
            Assert.AreEqual("RSA 2048", StandardKeyTypes.GetDisplayName(StandardKeyTypes.RSA256));
            Assert.AreEqual("RSA 4096", StandardKeyTypes.GetDisplayName(StandardKeyTypes.RSA256_4096));
            Assert.AreEqual("RSA 8192", StandardKeyTypes.GetDisplayName("RS256_8192"));
            Assert.AreEqual("ECDSA P-384", StandardKeyTypes.GetDisplayName(StandardKeyTypes.ECDSA384));
            Assert.AreEqual("Unknown", StandardKeyTypes.GetDisplayName(null));
        }

        [TestMethod]
        public void GetIssuerName_NamesTheIssuerByCommonNameAndOrganization()
        {
            Assert.AreEqual("R11 (Let's Encrypt)", ManagedItemListing.GetIssuerName("CN=R11, O=Let's Encrypt, C=US"));
            Assert.AreEqual("Sectigo RSA Domain Validation Secure Server CA (Sectigo Limited)", ManagedItemListing.GetIssuerName("CN=Sectigo RSA Domain Validation Secure Server CA, O=Sectigo Limited, L=Salford, S=Greater Manchester, C=GB"));
            Assert.AreEqual("Example Issuing CA", ManagedItemListing.GetIssuerName("CN=Example Issuing CA, O=Example"), "The organization is not repeated where the common name includes it");
            Assert.AreEqual("Internal CA", ManagedItemListing.GetIssuerName("CN=Internal CA"));
            Assert.AreEqual("Example, Inc.", ManagedItemListing.GetIssuerName("O=\"Example, Inc.\", C=US"));
            Assert.IsNull(ManagedItemListing.GetIssuerName(null));
        }

        /// <summary>
        /// The CA which issued the current certificate is preferred over a later attempt with another CA, and the
        /// recorded key type of the certificate over the configured one
        /// </summary>
        [TestMethod]
        public async Task GetHubManagedItems_ReportsTheLastUsedCAAndKeyType()
        {
            var controller = CreateController();

            var result = await controller.GetHubManagedItems(null, null) as OkObjectResult;
            var items = ((ManagedCertificateSummaryResult)result!.Value!).Results.ToDictionary(r => r.Id);

            Assert.AreEqual(StandardCertAuthorities.LETS_ENCRYPT, items["issued-le"].CertificateAuthorityId);
            Assert.AreEqual("Let's Encrypt", items["issued-le"].CertificateAuthorityTitle);
            Assert.AreEqual(StandardKeyTypes.ECDSA256, items["issued-le"].KeyType);
            Assert.AreEqual("E6 (Let's Encrypt)", items["issued-le"].Issuer);
            Assert.IsNull(items["attempted-custom"].Issuer, "No issuer until a certificate is recorded");

            Assert.AreEqual("custom-ca", items["attempted-custom"].CertificateAuthorityId);
            Assert.AreEqual("Internal CA", items["attempted-custom"].CertificateAuthorityTitle, "A custom CA is named as its instance reported it");
            Assert.AreEqual("unknown-ca", items["attempted-unknown"].CertificateAuthorityTitle, "A CA nobody has named is shown by its id");
            Assert.AreEqual(StandardKeyTypes.RSA256_4096, items["attempted-custom"].KeyType, "The configured key type applies until a certificate is recorded");

            Assert.IsNull(items["external"].CertificateAuthorityId);
            Assert.IsNull(items["external"].KeyType);
        }

        [TestMethod]
        public async Task GetHubManagedItems_FiltersByCAAndKeyType()
        {
            var controller = CreateController();

            var byCA = await controller.GetHubManagedItems(null, null, certificateAuthority: StandardCertAuthorities.LETS_ENCRYPT) as OkObjectResult;
            CollectionAssert.AreEquivalent(new[] { "issued-le", "issued-le-rsa" }, ((ManagedCertificateSummaryResult)byCA!.Value!).Results.Select(r => r.Id).ToList());

            var byKeyType = await controller.GetHubManagedItems(null, null, keyType: StandardKeyTypes.RSA256) as OkObjectResult;
            CollectionAssert.AreEquivalent(new[] { "issued-le-rsa" }, ((ManagedCertificateSummaryResult)byKeyType!.Value!).Results.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public async Task GetHubManagedItemsBreakdown_CountsByCAAndKeyType()
        {
            var controller = CreateController();

            var result = await controller.GetHubManagedItemsBreakdown(null, null) as OkObjectResult;
            var breakdown = (ManagedCertificateBreakdown)result!.Value!;

            Assert.AreEqual(StandardCertAuthorities.LETS_ENCRYPT, breakdown.CertificateAuthorities[0].Key, "Largest group first");
            Assert.AreEqual(2, breakdown.CertificateAuthorities[0].Count);
            Assert.AreEqual("Internal CA", breakdown.CertificateAuthorities.Single(g => g.Key == "custom-ca").Title);
            Assert.AreEqual(1, breakdown.CertificateAuthorities.Single(g => g.Key == "custom-ca").Count);
            Assert.AreEqual(1, breakdown.CertificateAuthorities.Single(g => g.Key == null).Count);

            Assert.AreEqual(4, breakdown.KeyTypes.Count);
            Assert.AreEqual("ECDSA P-256", breakdown.KeyTypes.Single(g => g.Key == StandardKeyTypes.ECDSA256).Title);
            Assert.AreEqual(2, breakdown.KeyTypes.Single(g => g.Key == null).Count);
        }

        private static X509Certificate2 SelfSigned(CertificateRequest request)
        {
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        }

        private static HubController CreateController()
        {
            var client = new Mock<ICertifyInternalApiClient>();

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(true);

            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope { HasAccess = true, IsUnrestricted = true });

            client.Setup(c => c.GetHubManagedInstances(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ManagedInstanceInfo> { new() { InstanceId = InstanceId, Title = "Instance" } });

            client.Setup(c => c.GetTagCategories(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<TagCategory>());

            client.Setup(c => c.GetAllHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ItemTag>());

            var items = new ConcurrentDictionary<string, ManagedInstanceItems>();
            items[InstanceId] = new ManagedInstanceItems
            {
                InstanceId = InstanceId,
                Items =
                [
                    Item("issued-le", ca: StandardCertAuthorities.LETS_ENCRYPT, lastAttemptedCA: "custom-ca", keyType: StandardKeyTypes.ECDSA256, csrKeyAlg: StandardKeyTypes.RSA256),
                    Item("issued-le-rsa", ca: StandardCertAuthorities.LETS_ENCRYPT, keyType: StandardKeyTypes.RSA256),
                    Item("attempted-custom", lastAttemptedCA: "custom-ca", csrKeyAlg: StandardKeyTypes.RSA256_4096),
                    Item("attempted-unknown", lastAttemptedCA: "unknown-ca"),
                    Item("external")
                ]
            };

            var stateProvider = new Mock<IInstanceManagementStateProvider>();
            stateProvider.Setup(s => s.GetManagedInstanceItems(It.IsAny<string?>())).Returns(items);
            stateProvider.Setup(s => s.GetConnectedInstances()).Returns([]);
            stateProvider.Setup(s => s.GetManagedInstanceStatusSummaries()).Returns(new ConcurrentDictionary<string, StatusSummary>());

            // the instance's own custom CA, which the hub does not have
            stateProvider.Setup(s => s.GetInstanceCertificateAuthorityTitles(InstanceId))
                .Returns(new Dictionary<string, string> { ["custom-ca"] = "Internal CA" });

            var mgmtApi = new ManagementAPI(
                stateProvider.Object,
                new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                new Mock<Certify.Management.ICertifyManager>().Object,
                NullLogger<ManagementAPI>.Instance);

            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Sid, CallerId)],
                    ApiKeyAuthenticationDefaults.AuthenticationScheme))
            };

            var services = new ServiceCollection();
            services.AddProblemDetailsFactory();
            context.RequestServices = services.BuildServiceProvider();

            return new HubController(NullLogger<CertificateController>.Instance, client.Object, stateProvider.Object, mgmtApi)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };
        }

        private static ManagedCertificate Item(string id, string? ca = null, string? lastAttemptedCA = null, string? keyType = null, string? csrKeyAlg = null)
        {
            return new ManagedCertificate
            {
                Id = id,
                InstanceId = InstanceId,
                Name = id,
                CertificateCurrentCA = ca,
                LastAttemptedCA = lastAttemptedCA ?? ca,
                CertificateKeyType = keyType,
                CertificateIssuer = id == "issued-le" ? "CN=E6, O=Let's Encrypt, C=US" : null,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = $"{id}.example.com",
                    SubjectAlternativeNames = [$"{id}.example.com"],
                    CSRKeyAlg = csrKeyAlg
                }
            };
        }
    }
}
