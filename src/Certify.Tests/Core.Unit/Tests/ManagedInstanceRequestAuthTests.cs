using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;

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

        [TestMethod]
        public async Task PerformManagedChallenge_AuthorizesManagedInstanceAndFulfillsAsItsOwnPrincipal()
        {
            // A managed instance signs with the hub joining credentials, which belong to the shared managed
            // instance service principal and grant hub joining only. Instance authorization therefore has to
            // win over the failing access token check, and fulfillment has to run as the instance's own
            // principal - scoping the forwarded request to the joining token instead makes the hub re-check
            // managed challenge access against a principal which can never hold it, refusing every challenge
            // with "Security principal is not authorised to use managed challenges".
            var secret = ManagedInstanceRequestAuth.GenerateSecret();
            var secretHash = ManagedInstanceRequestAuth.DeriveSecretHash(secret);
            var request = new ManagedChallengeRequest
            {
                ChallengeType = "dns-01",
                Identifier = "test.exmaple.com",
                ResponseKey = "_acme-challenge.test.exmaple.com",
                ResponseValue = "txt-value",
                AuthKey = AccessControlConfig.ManagedInstanceSecurityPrincipalId,
                AuthSecret = "join-secret"
            };

            var requestBody = JsonConvert.SerializeObject(request);
            var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash(Encoding.UTF8.GetBytes(requestBody));
            var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            var path = "/api/v1/managedchallenge/request";
            var signature = ManagedInstanceRequestAuth.ComputeSignatureFromSecret(secret, "instance-1", timestamp, "POST", path, bodyHash);

            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.CheckApiTokenHasAccess(
                    It.Is<AccessToken>(t => t.ClientId == request.AuthKey && t.Secret == request.AuthSecret),
                    It.Is<AccessCheck>(a => a.ResourceType == ResourceTypes.ManagedInstance && a.ResourceActionId == StandardResourceActions.ManagementHubInstanceJoin),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(new Certify.Models.Config.ActionResult("Managed instance join allowed", true));

            client.Setup(c => c.CheckApiTokenHasAccess(
                    It.Is<AccessToken>(t => t.ClientId == request.AuthKey && t.Secret == request.AuthSecret),
                    It.Is<AccessCheck>(a => a.ResourceType == ResourceTypes.ManagedChallenge && a.ResourceActionId == StandardResourceActions.ManagedChallengeRequest),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(new Certify.Models.Config.ActionResult("Access token unknown, expired or revoked.", false));

            client.Setup(c => c.GetHubManagedInstance("instance-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new ManagedInstanceInfo
                {
                    Id = "instance-1",
                    InstanceId = "instance-1",
                    RequestAuthSecretHash = secretHash,
                    SecurityPrincipalId = "sp-1"
                });

            client.Setup(c => c.GetManagedChallenges(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ManagedChallenge>
                {
                    new ManagedChallenge
                    {
                        Id = "challenge-1",
                        ChallengeConfig = new CertRequestChallengeConfig
                        {
                            DomainMatch = request.Identifier
                        }
                    }
                });

            client.Setup(c => c.GetHubItemTags(TaggedItemTypes.ManagedChallenge, "challenge-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<TagSummary>());

            client.Setup(c => c.GetHubSettings(It.IsAny<AuthContext>()))
                .ReturnsAsync(new HubSettings());

            client.Setup(c => c.EvaluateAccessScope(
                    It.Is<AccessCheck>(a => a.SecurityPrincipalId == "sp-1" && a.ResourceActionId == StandardResourceActions.ManagedChallengeRequest),
                    It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope { HasAccess = true, IsUnrestricted = true });

            // Deliberately unused while the code is correct: it is what the joining credentials would resolve
            // to if fulfillment went back to deriving identity from the access token, so a regression fails
            // naming the wrong principal rather than a null one.
            client.Setup(c => c.GetAssignedAccessTokens(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<AssignedAccessToken>
                {
                    new AssignedAccessToken
                    {
                        SecurityPrincipalId = AccessControlConfig.ManagedInstanceSecurityPrincipalId,
                        ScopedAssignedRoles = ["managedinstance-assignment-1"],
                        AccessTokens = [new AccessToken { ClientId = request.AuthKey, Secret = request.AuthSecret }]
                    }
                });

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(
                    It.Is<AccessCheck>(a =>
                        a.SecurityPrincipalId == "sp-1"
                        && a.ResourceType == ResourceTypes.ManagedChallenge
                        && a.ResourceActionId == StandardResourceActions.ManagedChallengeRequest
                        && a.Identifier == "challenge-1"),
                    It.Is<AuthContext>(a => a.UserId == "sp-1")))
                .ReturnsAsync(true);

            ManagedChallengeRequest? forwardedRequest = null;
            client.Setup(c => c.PerformManagedChallenge(
                    It.Is<ManagedChallengeRequest>(r => r.Identifier == request.Identifier && r.ResponseKey == request.ResponseKey),
                    It.IsAny<AuthContext>()))
                .Callback<ManagedChallengeRequest, AuthContext>((r, _) => forwardedRequest = r)
                .ReturnsAsync(new Certify.Models.Config.ActionResult("Managed challenge completed", true));

            var services = new ServiceCollection();
            services.AddSingleton(new ManagedInstanceRequestAuthValidator(client.Object, NullLogger<ManagedInstanceRequestAuthValidator>.Instance));

            var context = CreateRequestContext(path, requestBody, timestamp, signature, bodyHash);
            context.RequestServices = services.BuildServiceProvider();

            var scopeService = new ManagedChallengeScopeService(client.Object, NullLogger<ManagedChallengeScopeService>.Instance);

            var controller = new ManagedChallengeController(NullLogger<ManagedChallengeController>.Instance, client.Object, scopeService)
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };

            var result = await controller.PerformManagedChallenge(request);

            Assert.IsInstanceOfType<OkObjectResult>(result);
            var okResult = (OkObjectResult)result;
            Assert.IsInstanceOfType<Certify.Models.Config.ActionResult>(okResult.Value);
            Assert.IsTrue(((Certify.Models.Config.ActionResult)okResult.Value!).IsSuccess);

            Assert.IsNotNull(forwardedRequest, "the challenge request should have been forwarded for fulfillment");
            Assert.AreEqual("sp-1", forwardedRequest!.SecurityPrincipalId, "fulfillment must run as the managed instance's own security principal");
            Assert.IsNull(forwardedRequest.ScopedAssignedRoles, "the instance's own role assignments apply, not the joining token's scope");
        }

        [TestMethod]
        public async Task ValidateAsync_RejectsUnsignedRequestWhenInstanceHasNoSecret()
        {
            // An instance registered without a request auth secret must not be able to authenticate on the strength
            // of its instance id alone, otherwise anyone who learns that id can impersonate the instance.
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetHubManagedInstance("instance-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new ManagedInstanceInfo
                {
                    Id = "instance-1",
                    InstanceId = "instance-1",
                    RequestAuthSecretHash = string.Empty,
                    SecurityPrincipalId = "sp-1"
                });

            var validator = new ManagedInstanceRequestAuthValidator(client.Object, NullLogger<ManagedInstanceRequestAuthValidator>.Instance);

            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/v1/managedchallenge/request";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
            context.Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName] = "instance-1";

            var result = await validator.ValidateAsync(context.Request);

            Assert.IsFalse(result.IsSuccess, "An instance with no request auth secret must not be authenticated.");
            StringAssert.Contains(result.Message, "no request auth secret");
        }

        [TestMethod]
        public async Task ValidateAsync_AllowsUnsignedRequestOnlyWhenLegacyFallbackIsExplicitlyEnabled()
        {
            var client = new Mock<ICertifyInternalApiClient>(MockBehavior.Strict);
            client.Setup(c => c.GetHubManagedInstance("instance-1", It.IsAny<AuthContext>()))
                .ReturnsAsync(new ManagedInstanceInfo
                {
                    Id = "instance-1",
                    InstanceId = "instance-1",
                    RequestAuthSecretHash = string.Empty,
                    SecurityPrincipalId = "sp-1"
                });

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ManagedInstanceRequestAuthValidator.AllowLegacyUnsignedRequestsConfigKey] = "true"
                })
                .Build();

            var validator = new ManagedInstanceRequestAuthValidator(client.Object, NullLogger<ManagedInstanceRequestAuthValidator>.Instance, configuration);

            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/v1/managedchallenge/request";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
            context.Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName] = "instance-1";

            var result = await validator.ValidateAsync(context.Request);

            Assert.IsTrue(result.IsSuccess, result.Message);
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
