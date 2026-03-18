using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    [TestClass]
    public class ManagedInstanceRequestAuthTests
    {
        [TestMethod]
        public void ComputeSignatureFromSecret_MatchesStoredSecretHashSignature()
        {
            var secret = ManagedInstanceRequestAuth.GenerateSecret();
            var secretHash = ManagedInstanceRequestAuth.DeriveSecretHash(secret);
            var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash(Encoding.UTF8.GetBytes("{\"value\":1}"));

            var signatureFromSecret = ManagedInstanceRequestAuth.ComputeSignatureFromSecret(
                secret,
                "instance-1",
                timestamp,
                "POST",
                "/api/v1/managedchallenge/request",
                bodyHash);

            var signatureFromHash = ManagedInstanceRequestAuth.ComputeSignatureFromSecretHash(
                secretHash,
                "instance-1",
                timestamp,
                "POST",
                "/api/v1/managedchallenge/request",
                bodyHash);

            Assert.AreEqual(signatureFromSecret, signatureFromHash);
            Assert.IsTrue(ManagedInstanceRequestAuth.FixedTimeEquals(signatureFromSecret, signatureFromHash));
        }

        [TestMethod]
        public async Task ValidateAsync_AcceptsValidSignedRequest()
        {
            var secret = ManagedInstanceRequestAuth.GenerateSecret();
            var secretHash = ManagedInstanceRequestAuth.DeriveSecretHash(secret);
            var requestBody = "{\"value\":1}";
            var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
            var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash(bodyBytes);
            var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var path = "/api/v1/managedchallenge/request";
            var signature = ManagedInstanceRequestAuth.ComputeSignatureFromSecret(secret, "instance-1", timestamp, "POST", path, bodyHash);

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetHubManagedInstance("instance-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new ManagedInstanceInfo
                {
                    Id = "instance-1",
                    InstanceId = "instance-1",
                    RequestAuthSecretHash = secretHash,
                    SecurityPrincipalId = "sp-1"
                });

            var validator = new ManagedInstanceRequestAuthValidator(client.Object, NullLogger<ManagedInstanceRequestAuthValidator>.Instance);
            var context = CreateRequestContext(path, requestBody, timestamp, signature, bodyHash);

            var result = await validator.ValidateAsync(context.Request);

            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.IsNotNull(result.ManagedInstance);
            Assert.AreEqual("instance-1", result.ManagedInstance.InstanceId);
        }

        [TestMethod]
        public async Task ValidateAsync_RejectsStaleTimestamp()
        {
            var secret = ManagedInstanceRequestAuth.GenerateSecret();
            var secretHash = ManagedInstanceRequestAuth.DeriveSecretHash(secret);
            var requestBody = "{\"value\":1}";
            var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash(Encoding.UTF8.GetBytes(requestBody));
            var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O", CultureInfo.InvariantCulture);
            var signature = ManagedInstanceRequestAuth.ComputeSignatureFromSecret(
                secret,
                "instance-1",
                timestamp,
                "POST",
                "/api/v1/managedchallenge/request",
                bodyHash);

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetHubManagedInstance("instance-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new ManagedInstanceInfo
                {
                    Id = "instance-1",
                    InstanceId = "instance-1",
                    RequestAuthSecretHash = secretHash,
                    SecurityPrincipalId = "sp-1"
                });

            var validator = new ManagedInstanceRequestAuthValidator(client.Object, NullLogger<ManagedInstanceRequestAuthValidator>.Instance);
            var context = CreateRequestContext("/api/v1/managedchallenge/request", requestBody, timestamp, signature, bodyHash);

            var result = await validator.ValidateAsync(context.Request);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains(result.Message, "clock skew");
        }

        private static DefaultHttpContext CreateRequestContext(string path, string requestBody, string timestamp, string signature, string bodyHash)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = path;
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
            context.Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName] = "instance-1";
            context.Request.Headers[ManagedInstanceRequestAuth.TimestampHeaderName] = timestamp;
            context.Request.Headers[ManagedInstanceRequestAuth.SignatureHeaderName] = signature;
            context.Items[ManagedInstanceRequestAuth.CachedBodyHashItemKey] = bodyHash;
            return context;
        }
    }
}
