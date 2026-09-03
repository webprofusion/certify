using System;
using System.Security.Cryptography;
using System.Text;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the decision a certificate subscription makes about what its source returned: whether it is a
    /// certificate the item does not already hold, and which version marker identifies it. Reporting an update which
    /// is really the certificate already installed redeploys it and re-runs every deployment task on each check;
    /// missing a real one leaves the target on an expiring certificate
    /// </summary>
    [TestClass]
    public class SubscriptionFetchDecisionTests
    {
        private static byte[] Payload(string content = "certificate-payload") => Encoding.UTF8.GetBytes(content);

        private static string DigestOf(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        [TestMethod, Description("A certificate the item does not hold is reported as an update")]
        public void NewVersionIsAnUpdate()
        {
            var result = CertifyManager.ResolveFetchedCertificate(Payload(), "etag-v2", lastSourceVersion: "etag-v1", ignoreCurrentVersion: false);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual("etag-v2", result.SourceVersion, "The source's own version is what gets recorded once it deploys");
            Assert.IsNotNull(result.CertificateData, "The payload is carried through, it is what gets stored and deployed");
        }

        [TestMethod, Description("The certificate the item already holds is not reported as an update")]
        public void VersionAlreadyHeldIsNotAnUpdate()
        {
            var result = CertifyManager.ResolveFetchedCertificate(Payload(), "etag-v1", lastSourceVersion: "etag-v1", ignoreCurrentVersion: false);

            Assert.IsTrue(result.IsSuccess, "The source answered, which is a successful check");
            Assert.IsFalse(result.HasUpdate, "Redeploying the certificate already installed would re-run every deployment task on each check");
        }

        [TestMethod, Description("A version matching the one held is recognised regardless of case")]
        public void VersionComparisonIgnoresCase()
        {
            // hub versions are ETags, which are hex digests whose case is not significant
            var result = CertifyManager.ResolveFetchedCertificate(Payload(), "ABC123", lastSourceVersion: "abc123", ignoreCurrentVersion: false);

            Assert.IsFalse(result.HasUpdate);
        }

        [TestMethod, Description("The first fetch for an item which holds no version is an update")]
        public void FirstFetchIsAnUpdate()
        {
            Assert.IsTrue(CertifyManager.ResolveFetchedCertificate(Payload(), "etag-v1", lastSourceVersion: null, ignoreCurrentVersion: false).HasUpdate);
            Assert.IsTrue(CertifyManager.ResolveFetchedCertificate(Payload(), "etag-v1", lastSourceVersion: "   ", ignoreCurrentVersion: false).HasUpdate,
                "A blank stored version identifies nothing, so there is nothing to match against");
        }

        [TestMethod, Description("A manual request deploys what the source has even when it is the version already held")]
        public void IgnoringTheCurrentVersionAlwaysReportsAnUpdate()
        {
            // a manual request and the subscription access test both fetch regardless: the user is asking for the
            // certificate to be pulled and deployed now
            var result = CertifyManager.ResolveFetchedCertificate(Payload(), "etag-v1", lastSourceVersion: "etag-v1", ignoreCurrentVersion: true);

            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual("etag-v1", result.SourceVersion);
        }

        [TestMethod, Description("A source which declares no version is identified by a digest of what it returned")]
        public void SourceWithoutAVersionIsIdentifiedByItsPayloadDigest()
        {
            var payload = Payload();

            var result = CertifyManager.ResolveFetchedCertificate(payload, sourceETag: null, lastSourceVersion: null, ignoreCurrentVersion: false);

            Assert.AreEqual(DigestOf(payload), result.SourceVersion, "Without a declared version the payload itself has to identify the certificate");
            Assert.IsTrue(result.HasUpdate);
        }

        [TestMethod, Description("A source without versions reports no update while it keeps returning the same certificate")]
        public void UnversionedSourceReturningTheSameCertificateIsNotAnUpdate()
        {
            var payload = Payload();
            var firstFetch = CertifyManager.ResolveFetchedCertificate(payload, sourceETag: null, lastSourceVersion: null, ignoreCurrentVersion: false);

            // the version recorded by the first fetch, as it would have been stored after deployment
            var secondFetch = CertifyManager.ResolveFetchedCertificate(payload, sourceETag: null, lastSourceVersion: firstFetch.SourceVersion, ignoreCurrentVersion: false);

            Assert.IsFalse(secondFetch.HasUpdate, "The digest is stable, which is what stops an unversioned source redeploying on every check");
        }

        [TestMethod, Description("A source without versions reports an update once the certificate itself changes")]
        public void UnversionedSourceReturningADifferentCertificateIsAnUpdate()
        {
            var firstFetch = CertifyManager.ResolveFetchedCertificate(Payload("first-certificate"), sourceETag: null, lastSourceVersion: null, ignoreCurrentVersion: false);

            var secondFetch = CertifyManager.ResolveFetchedCertificate(Payload("renewed-certificate"), sourceETag: null, lastSourceVersion: firstFetch.SourceVersion, ignoreCurrentVersion: false);

            Assert.IsTrue(secondFetch.HasUpdate);
            Assert.AreNotEqual(firstFetch.SourceVersion, secondFetch.SourceVersion);
        }

        [TestMethod, Description("A source which declares a blank version is treated as declaring none")]
        [DataRow("")]
        [DataRow("   ")]
        public void BlankDeclaredVersionFallsBackToTheDigest(string sourceETag)
        {
            var payload = Payload();

            var result = CertifyManager.ResolveFetchedCertificate(payload, sourceETag, lastSourceVersion: null, ignoreCurrentVersion: false);

            // recording a blank marker would leave the next check comparing against nothing, so it would refetch and
            // redeploy the same certificate every time
            Assert.AreEqual(DigestOf(payload), result.SourceVersion);
        }

        [TestMethod, Description("A source which returns nothing is a failed fetch, not an empty update")]
        public void EmptyPayloadIsAFailure()
        {
            var result = CertifyManager.ResolveFetchedCertificate(Array.Empty<byte>(), "etag-v2", lastSourceVersion: "etag-v1", ignoreCurrentVersion: false);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate, "Nothing usable came back, so nothing may be presented as a certificate to deploy");
            Assert.Contains("empty certificate payload", result.Message);
        }

        [TestMethod, Description("A missing payload is a failed fetch rather than an unhandled error")]
        public void MissingPayloadIsAFailure()
        {
            var result = CertifyManager.ResolveFetchedCertificate(null, "etag-v2", lastSourceVersion: null, ignoreCurrentVersion: false);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.HasUpdate);
        }

        [TestMethod, Description("A version held but no version declared is judged on the payload, not assumed unchanged")]
        public void HeldVersionAgainstAnUndeclaredVersionIsJudgedOnThePayload()
        {
            var payload = Payload();

            // the source stopped declaring versions: the digest decides, and it does not match the ETag last stored
            var result = CertifyManager.ResolveFetchedCertificate(payload, sourceETag: null, lastSourceVersion: "etag-v1", ignoreCurrentVersion: false);

            Assert.IsTrue(result.HasUpdate);
            Assert.AreEqual(DigestOf(payload), result.SourceVersion);
        }
    }
}
