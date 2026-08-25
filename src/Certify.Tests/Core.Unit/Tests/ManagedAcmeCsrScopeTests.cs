using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Certify.Models.Hub;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Managed ACME finalization must not issue certificates for identifiers beyond those authorized on the
    /// order, or beyond the domain restrictions on the account's roles.
    /// </summary>
    [TestClass]
    public class ManagedAcmeCsrScopeTests
    {
        /// <summary>
        /// Build a CSR encoded as an ACME client sends it (base64url of the DER).
        /// </summary>
        private static string BuildCsr(string commonName, params string[] subjectAltNames)
        {
            using var rsa = RSA.Create(2048);

            var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var san in subjectAltNames)
            {
                sanBuilder.AddDnsName(san);
            }

            request.CertificateExtensions.Add(sanBuilder.Build());

            return Convert.ToBase64String(request.CreateSigningRequest())
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Decode a finalization CSR the same way the ACME finalize endpoint does.
        /// </summary>
        private static List<string> DecodeCsrIdentifiers(string csr)
        {
            return CSRUtils.DecodeCsrSubjects(Certify.Management.Util.FromUrlSafeBase64String(csr))
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? FindIdentifierNotOnOrder(IEnumerable<string> csrIdentifiers, params string[] orderIdentifiers)
        {
            var onOrder = orderIdentifiers
                .Select(v => v.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return csrIdentifiers.FirstOrDefault(i => !onOrder.Contains(i));
        }

        [TestMethod]
        [Description("A CSR requesting exactly the order's identifiers is accepted")]
        public void CsrMatchingOrderIsAccepted()
        {
            var identifiers = DecodeCsrIdentifiers(BuildCsr("app.finance.example.com", "app.finance.example.com", "api.finance.example.com"));

            CollectionAssert.AreEquivalent(
                new[] { "app.finance.example.com", "api.finance.example.com" },
                identifiers,
                "the CN and SANs should both be extracted");

            Assert.IsNull(FindIdentifierNotOnOrder(identifiers, "app.finance.example.com", "api.finance.example.com"));
        }

        [TestMethod]
        [Description("A CSR smuggling an identifier absent from the order is rejected")]
        public void CsrWithIdentifierNotOnOrderIsRejected()
        {
            var identifiers = DecodeCsrIdentifiers(BuildCsr("app.finance.example.com", "app.finance.example.com", "www.evil.com"));

            Assert.AreEqual(
                "www.evil.com",
                FindIdentifierNotOnOrder(identifiers, "app.finance.example.com"),
                "an identifier absent from the order must be detected");
        }

        [TestMethod]
        [Description("A CSR requesting a subset of the order's identifiers is accepted")]
        public void CsrRequestingSubsetOfOrderIsAccepted()
        {
            var identifiers = DecodeCsrIdentifiers(BuildCsr("a.example.com", "a.example.com"));

            Assert.IsNull(FindIdentifierNotOnOrder(identifiers, "a.example.com", "b.example.com"));
        }

        [TestMethod]
        [Description("Wildcard identifiers survive CSR decoding intact")]
        public void WildcardIdentifierIsPreserved()
        {
            var identifiers = DecodeCsrIdentifiers(BuildCsr("*.example.com", "*.example.com"));

            Assert.IsTrue(identifiers.Contains("*.example.com"));
            Assert.IsNull(FindIdentifierNotOnOrder(identifiers, "*.example.com"));
        }

        [TestMethod]
        [Description("CSR identifiers are re-checked against the account's domain restrictions")]
        public void CsrIdentifiersAreCheckedAgainstDomainRestrictions()
        {
            var role = new AssignedRole
            {
                RoleId = StandardRoles.ManagedAcmeConsumer.Id,
                SecurityPrincipalId = "acme_principal",
                IncludedResources = [new Resource { ResourceType = ResourceTypes.Domain, Identifier = "*.finance.example.com" }]
            };

            var rules = ResourceAccess.GetDomainRestrictionRules([role]);

            var permitted = DecodeCsrIdentifiers(BuildCsr("app.finance.example.com", "app.finance.example.com"));
            Assert.IsTrue(permitted.All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(rules, i)));

            // an order placed before the restriction was narrowed must not still finalize out of scope
            var outOfScope = DecodeCsrIdentifiers(BuildCsr("app.finance.example.com", "app.finance.example.com", "app.sales.example.com"));
            Assert.IsFalse(outOfScope.All(i => ResourceAccess.IsIdentifierPermittedByDomainRules(rules, i)));
        }

        [TestMethod]
        [Description("A malformed CSR is rejected rather than silently accepted")]
        public void MalformedCsrIsRejected()
        {
            Assert.ThrowsExactly<ArgumentException>(() => DecodeCsrIdentifiers("bm90LWEtY3Ny"));
        }
    }
}
