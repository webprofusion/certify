using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.ACME.Anvil;
using Certify.ACME.Anvil.Acme;
using Certify.ACME.Anvil.Acme.Resource;
using Certify.Models;
using Certify.Models.Config;
using Certify.Management;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// A custom CSR can be used for its subject fields only, which are copied into the generated CSR
    /// along with the certificate's own identifiers and key.
    /// </summary>
    [TestClass]
    public class CustomCsrSubjectTests
    {
        private static string BuildCustomCsrPem()
        {
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest("CN=old.example.com, O=\"Example, Inc.\", L=Brisbane, C=AU", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("old.example.com");
            request.CertificateExtensions.Add(sanBuilder.Build());

            return request.CreateSigningRequestPem();
        }

        private static async Task<string> GetCertificateRequestPreview(string? customCsr, bool fieldsOnly)
        {
            var item = new ManagedCertificate { Id = "preview-test" };
            item.RequestConfig.PrimaryDomain = "new.example.com";
            item.RequestConfig.SubjectAlternativeNames = new[] { "new.example.com" };
            item.RequestConfig.Challenges.Add(new CertRequestChallengeConfig { ChallengeType = SupportedChallengeTypes.CHALLENGE_TYPE_HTTP });
            item.RequestConfig.CustomCSR = customCsr;
            item.RequestConfig.CustomCSRFieldsOnly = fieldsOnly;
            item.DomainOptions.Add(new DomainOption { Domain = "new.example.com", IsPrimaryDomain = true, IsSelected = true });

            var certifyManager = new Mock<ICertifyManager>();
            certifyManager.Setup(m => m.GetCertificateAuthorities()).ReturnsAsync(new List<CertificateAuthority>());
            certifyManager.Setup(m => m.GetDeploymentProviders()).ReturnsAsync(new List<DeploymentProviderDefinition>());
            certifyManager.Setup(m => m.DeployCertificate(It.IsAny<ManagedCertificate>(), It.IsAny<System.IProgress<RequestProgressState>?>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync(new CertificateRequestResult());

            var credentialsManager = new Mock<ICredentialsManager>();
            credentialsManager.Setup(m => m.GetCredentials(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(new List<StoredCredential>());

            var steps = await new PreviewManager().GeneratePreview(item, null!, certifyManager.Object, credentialsManager.Object);

            return steps.Single(s => s.Category == "CertificateRequest").Description;
        }

        [TestMethod, Description("The preview lists the subject fields copied from the custom CSR, without its CN or identifiers")]
        public async Task PreviewListsCopiedSubjectFields()
        {
            var preview = await GetCertificateRequestPreview(BuildCustomCsrPem(), fieldsOnly: true);

            Assert.Contains("signing algorithm", preview);
            Assert.Contains("will be copied from your custom CSR", preview);
            Assert.Contains("| Organization (O) | Example, Inc. |", preview);
            Assert.Contains("| Locality (L) | Brisbane |", preview);
            Assert.Contains("| Country (C) | AU |", preview);
            Assert.DoesNotContain("Common Name (CN)", preview);
            Assert.DoesNotContain("old.example.com", preview);
        }

        [TestMethod, Description("The preview shows a custom CSR used as-is with its subject fields and identifiers")]
        public async Task PreviewDescribesCustomCsrUsedAsIs()
        {
            var preview = await GetCertificateRequestPreview(BuildCustomCsrPem(), fieldsOnly: false);

            Assert.Contains("as-is", preview);
            Assert.DoesNotContain("signing algorithm", preview);
            Assert.Contains("| Common Name (CN) | old.example.com |", preview);
            Assert.Contains("| Organization (O) | Example, Inc. |", preview);
            Assert.Contains("* old.example.com", preview);
        }

        [TestMethod, Description("The preview reports a custom CSR which cannot be read")]
        public async Task PreviewReportsUnreadableCustomCsr()
        {
            var preview = await GetCertificateRequestPreview("-----BEGIN CERTIFICATE REQUEST-----\nnotacsr\n-----END CERTIFICATE REQUEST-----", fieldsOnly: true);

            Assert.Contains("The custom CSR could not be read", preview);
        }

        [TestMethod, Description("The preview does not mention a custom CSR when none is set")]
        public async Task PreviewWithoutCustomCsr()
        {
            var preview = await GetCertificateRequestPreview(null, fieldsOnly: false);

            Assert.Contains("signing algorithm", preview);
            Assert.DoesNotContain("custom CSR", preview);
        }

        [TestMethod, Description("Subject fields exclude the CN and keep values which need escaping")]
        public void GetCsrSubjectFieldsExcludesCommonName()
        {
            var fields = CSRUtils.GetCsrSubjectFields(BuildCustomCsrPem());

            Assert.IsNotNull(fields);
            Assert.DoesNotContain("CN=", fields);
            Assert.Contains("O=Example\\, Inc.", fields);
            Assert.Contains("L=Brisbane", fields);
            Assert.Contains("C=AU", fields);
        }

        [TestMethod, Description("A CSR with only a CN has no subject fields to copy")]
        public void GetCsrSubjectFieldsReturnsNullForCommonNameOnly()
        {
            using var rsa = RSA.Create(2048);
            var pem = new CertificateRequest("CN=old.example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem();

            Assert.IsNull(CSRUtils.GetCsrSubjectFields(pem));
        }

        [TestMethod, Description("The generated CSR has the custom subject fields with our own identifiers and key")]
        public async Task FinalizeCopiesSubjectFieldsWithOwnIdentifiersAndKey()
        {
            byte[]? finalizedCsr = null;

            var orderCtxMock = new Mock<IOrderContext>();
            orderCtxMock.Setup(m => m.Finalize(It.IsAny<byte[]>()))
                .Callback<byte[]>(csr => finalizedCsr = csr)
                .ReturnsAsync(new Order { Status = OrderStatus.Valid });

            var key = KeyFactory.NewKey(KeyAlgorithm.ES256);

            var csrInfo = new CsrInfo { SubjectFields = CSRUtils.GetCsrSubjectFields(BuildCustomCsrPem()) };

            await orderCtxMock.Object.Finalize(csrInfo, key, new[] { new Identifier { Value = "new.example.com", Type = IdentifierType.Dns } });

            Assert.IsNotNull(finalizedCsr);

            var identifiers = CSRUtils.DecodeCsrSubjects(finalizedCsr).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
            CollectionAssert.AreEqual(new[] { "new.example.com" }, identifiers);

            var subject = CSRUtils.DecodeCsr(finalizedCsr).GetCertificationRequestInfo().Subject;
            // the custom CSR's field order is kept (.NET encodes its distinguished name string in reverse)
            Assert.AreEqual("C=AU,L=Brisbane,O=Example\\, Inc.", subject.ToString());

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(key.ToDer(), out _);

            var request = CertificateRequest.LoadSigningRequest(finalizedCsr, HashAlgorithmName.SHA256);
            CollectionAssert.AreEqual(ecdsa.ExportSubjectPublicKeyInfo(), request.PublicKey.ExportSubjectPublicKeyInfo());
        }

        [TestMethod, Description("The custom CSR is only used as-is when not limited to its subject fields")]
        public void IsCustomCSRUsedAsIs()
        {
            var config = new CertRequestConfig();
            Assert.IsFalse(config.IsCustomCSRUsedAsIs);

            config.CustomCSR = BuildCustomCsrPem();
            Assert.IsTrue(config.IsCustomCSRUsedAsIs);

            config.CustomCSRFieldsOnly = true;
            Assert.IsFalse(config.IsCustomCSRUsedAsIs);
        }
    }
}
