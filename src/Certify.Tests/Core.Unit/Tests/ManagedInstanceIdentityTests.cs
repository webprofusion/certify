using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Which managed instance a caller is. The joining credentials are shared by every instance, so they do not identify
    /// one: an instance holding a request auth secret proves it by signing its joincheck, a management hub connection
    /// acts only as the instance its joining token names, and the security principal an instance acts as is decided by
    /// the hub alone.
    /// </summary>
    [TestClass]
    public class ManagedInstanceIdentityTests
    {
        private const string InstanceId = "5d0b3a8e-6a3f-4f69-9a55-2f3e1c7d9b10";
        private const string OtherInstanceId = "0f6c2b1d-3e4a-4b5c-8d7e-9f0a1b2c3d4e";
        private const string JoiningPrincipalId = AccessControlConfig.ManagedInstanceSecurityPrincipalId;

        #region joincheck

        [TestMethod]
        [Description("An instance holding a secret is refused a joining token for an unsigned joincheck")]
        public async Task CheckJoining_InstanceWithSecret_Unsigned_IsRefused()
        {
            var harness = new JoinHarness(hasSecret: true);

            var result = await harness.Controller().CheckJoining();

            var problem = (ObjectResult)result;
            Assert.AreEqual(StatusCodes.Status401Unauthorized, problem.StatusCode);
            StringAssert.EndsWith(((ProblemDetails)problem.Value!).Type, "/hub-joincheck-signature-required");
            Assert.IsNull(harness.StoredSecretHash, "no secret is issued");
        }

        [TestMethod]
        [Description("A joincheck signed with the instance's secret is issued a joining token for that instance")]
        public async Task CheckJoining_InstanceWithSecret_Signed_IsIssuedAJoiningToken()
        {
            var harness = new JoinHarness(hasSecret: true);

            var joining = Joined(await harness.Controller(signed: true).CheckJoining());

            Assert.IsFalse(string.IsNullOrWhiteSpace(joining.JoiningToken));
            Assert.AreEqual(InstanceId, joining.HubAssignedInstanceId);
            Assert.IsTrue(string.IsNullOrEmpty(joining.RequestAuthSecret), "the existing secret is kept unless a reissue is asked for");
        }

        [TestMethod]
        [Description("A joincheck signed with the instance's secret may have the secret replaced")]
        public async Task CheckJoining_InstanceWithSecret_SignedReissue_IssuesANewSecret()
        {
            var harness = new JoinHarness(hasSecret: true);

            var joining = Joined(await harness.Controller(signed: true, reissue: true).CheckJoining(reissueRequestAuthSecret: true));

            Assert.IsFalse(string.IsNullOrWhiteSpace(joining.RequestAuthSecret));
            Assert.AreEqual(ManagedInstanceRequestAuth.DeriveSecretHash(joining.RequestAuthSecret), harness.StoredSecretHash);
        }

        [TestMethod]
        [Description("With the legacy setting on, an unsigned joincheck is issued a joining token, but the secret is never replaced")]
        public async Task CheckJoining_InstanceWithSecret_UnsignedWithLegacySetting_IsIssuedATokenButNoSecret()
        {
            var harness = new JoinHarness(hasSecret: true, allowLegacyUnsignedJoinCheck: true);

            var joining = Joined(await harness.Controller(reissue: true).CheckJoining(reissueRequestAuthSecret: true));

            Assert.IsFalse(string.IsNullOrWhiteSpace(joining.JoiningToken));
            Assert.IsTrue(string.IsNullOrEmpty(joining.RequestAuthSecret));
            Assert.IsNull(harness.StoredSecretHash);
        }

        [TestMethod]
        [Description("An instance with no secret, such as one an administrator has had rejoin, is issued one by an unsigned joincheck")]
        public async Task CheckJoining_InstanceWithoutSecret_IsIssuedASecret()
        {
            var harness = new JoinHarness(hasSecret: false);

            var joining = Joined(await harness.Controller().CheckJoining());

            Assert.IsFalse(string.IsNullOrWhiteSpace(joining.RequestAuthSecret));
            Assert.AreEqual(ManagedInstanceRequestAuth.DeriveSecretHash(joining.RequestAuthSecret), harness.StoredSecretHash);
        }

        private static HubJoiningInfo Joined(IActionResult result)
        {
            Assert.IsInstanceOfType<OkObjectResult>(result, (result as ObjectResult)?.Value?.ToString());
            return (HubJoiningInfo)((OkObjectResult)result).Value!;
        }

        private sealed class JoinHarness
        {
            private readonly Mock<ICertifyInternalApiClient> _client = new();
            private readonly ManagementAPI _mgmtApi;
            private readonly IConfiguration _configuration;
            private readonly string _secret = ManagedInstanceRequestAuth.GenerateSecret();

            public string? StoredSecretHash { get; private set; }

            public JoinHarness(bool hasSecret, bool allowLegacyUnsignedJoinCheck = false)
            {
                _client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(true);

                _client.Setup(c => c.GetHubManagedInstance(InstanceId, It.IsAny<AuthContext>()))
                    .ReturnsAsync(() => new ManagedInstanceInfo
                    {
                        Id = InstanceId,
                        InstanceId = InstanceId,
                        RequestAuthSecretHash = hasSecret ? ManagedInstanceRequestAuth.DeriveSecretHash(_secret) : string.Empty
                    });

                var certifyManager = new Mock<ICertifyManager>();
                certifyManager.Setup(m => m.SetHubManagedInstanceRequestAuthSecretHash(InstanceId, It.IsAny<string?>()))
                    .Callback((string _, string? hash) => StoredSecretHash = hash)
                    .ReturnsAsync(new Certify.Models.Config.ActionResult("Updated", true));

                _mgmtApi = new ManagementAPI(
                    new Mock<IInstanceManagementStateProvider>().Object,
                    new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                    certifyManager.Object,
                    NullLogger<ManagementAPI>.Instance);

                var settings = new Dictionary<string, string?>
                {
                    ["JwtSettings:secret"] = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                    ["JwtSettings:issuer"] = "Certify.Server.Hub.Api.Tests",
                    ["JwtSettings:authTokenExpirationInMinutes"] = "5"
                };

                if (allowLegacyUnsignedJoinCheck)
                {
                    settings[ManagedInstanceRequestAuthValidator.AllowLegacyUnsignedJoinCheckConfigKey] = "true";
                }

                _configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            }

            public SystemController Controller(bool signed = false, bool reissue = false)
            {
                var services = new ServiceCollection();
                services.AddProblemDetailsFactory();
                services.AddLogging();
                services.AddSingleton(_configuration);
                services.AddSingleton(_client.Object);

                var context = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Sid, JoiningPrincipalId)],
                        ApiKeyAuthenticationDefaults.AuthenticationScheme)),
                    RequestServices = services.BuildServiceProvider()
                };

                context.Request.Method = HttpMethods.Get;
                context.Request.Path = "/api/v1/hub/joincheck/";
                context.Request.QueryString = new QueryString(reissue ? "?reissueRequestAuthSecret=true" : "?reissueRequestAuthSecret=false");
                context.Request.Headers[ManagedInstanceRequestAuth.HubAssignedIdHeaderName] = InstanceId;

                if (signed)
                {
                    var bodyHash = ManagedInstanceRequestAuth.ComputeBodyHash([]);
                    var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    var pathAndQuery = context.Request.Path.Value + context.Request.QueryString.Value;

                    context.Request.Headers[ManagedInstanceRequestAuth.TimestampHeaderName] = timestamp;
                    context.Request.Headers[ManagedInstanceRequestAuth.SignatureHeaderName] =
                        ManagedInstanceRequestAuth.ComputeSignatureFromSecret(_secret, InstanceId, timestamp, HttpMethods.Get, pathAndQuery, bodyHash);
                    context.Items[ManagedInstanceRequestAuth.CachedBodyHashItemKey] = bodyHash;
                }

                return new SystemController(NullLogger<SystemController>.Instance, _client.Object, _mgmtApi)
                {
                    ControllerContext = new ControllerContext { HttpContext = context }
                };
            }
        }

        #endregion

        #region management hub connection

        [TestMethod]
        [Description("A connection reporting an instance other than the one its joining token names is refused")]
        public async Task InstanceInfo_ReportingAnotherInstance_IsRefused()
        {
            var harness = new HubHarness();

            await harness.Hub.ReceiveCommandResult(InstanceInfoResult(new ManagedInstanceInfo { Id = OtherInstanceId, InstanceId = OtherInstanceId, Title = "other" }));

            harness.Context.Verify(c => c.Abort(), Times.Once);
            harness.StateProvider.Verify(s => s.UpdateInstanceConnectionInfo(It.IsAny<string>(), It.IsAny<ManagedInstanceInfo>()), Times.Never);
            harness.Backend.Verify(c => c.AddHubManagedInstance(It.IsAny<ManagedInstanceInfo>(), It.IsAny<AuthContext>()), Times.Never);
            harness.Backend.Verify(c => c.UpdateHubManagedInstance(It.IsAny<ManagedInstanceInfo>(), It.IsAny<AuthContext>()), Times.Never);
        }

        [TestMethod]
        [Description("An instance cannot name the security principal it acts as")]
        public async Task InstanceInfo_NamingASecurityPrincipal_IsNotLinkedToIt()
        {
            var harness = new HubHarness();

            await harness.Hub.ReceiveCommandResult(InstanceInfoResult(new ManagedInstanceInfo
            {
                Id = InstanceId,
                InstanceId = InstanceId,
                Title = "instance",
                SecurityPrincipalId = AccessControlConfig.AdminSecurityPrincipalId
            }));

            harness.Context.Verify(c => c.Abort(), Times.Never);
            harness.Backend.Verify(c => c.AddHubManagedInstance(
                It.Is<ManagedInstanceInfo>(i => i.InstanceId == InstanceId && string.IsNullOrEmpty(i.SecurityPrincipalId)),
                It.IsAny<AuthContext>()), Times.Once);
            harness.StateProvider.Verify(s => s.UpdateInstanceConnectionInfo(
                "connection-1",
                It.Is<ManagedInstanceInfo>(i => i.InstanceId == InstanceId && string.IsNullOrEmpty(i.SecurityPrincipalId))), Times.Once);
        }

        [TestMethod]
        [Description("A known instance keeps the security principal the hub linked it to, whatever it reports")]
        public async Task InstanceInfo_KnownInstance_KeepsItsStoredSecurityPrincipal()
        {
            var harness = new HubHarness(storedInstance: new ManagedInstanceInfo { Id = InstanceId, InstanceId = InstanceId, SecurityPrincipalId = "instance-principal" });

            await harness.Hub.ReceiveCommandResult(InstanceInfoResult(new ManagedInstanceInfo
            {
                Id = InstanceId,
                InstanceId = InstanceId,
                Title = "instance",
                SecurityPrincipalId = AccessControlConfig.AdminSecurityPrincipalId
            }));

            harness.StateProvider.Verify(s => s.UpdateInstanceConnectionInfo(
                "connection-1",
                It.Is<ManagedInstanceInfo>(i => i.SecurityPrincipalId == "instance-principal")), Times.Once);
            harness.Backend.Verify(c => c.UpdateHubManagedInstance(
                It.Is<ManagedInstanceInfo>(i => i.SecurityPrincipalId == "instance-principal"),
                It.IsAny<AuthContext>()), Times.Once);
        }

        private static InstanceCommandResult InstanceInfoResult(ManagedInstanceInfo reported)
            => new()
            {
                CommandId = Guid.NewGuid(),
                CommandType = ManagementHubCommands.GetInstanceInfo,
                IsCommandResponse = false,
                Value = JsonSerializer.Serialize(reported, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions)
            };

        private sealed class HubHarness
        {
            public Mock<IInstanceManagementStateProvider> StateProvider { get; } = new();
            public Mock<ICertifyInternalApiClient> Backend { get; } = new();
            public Mock<HubCallerContext> Context { get; } = new();
            public InstanceManagementHub Hub { get; }

            public HubHarness(ManagedInstanceInfo? storedInstance = null)
            {
                StateProvider.Setup(s => s.GetInstanceIdForConnection("connection-1")).Returns(InstanceId);
                StateProvider.Setup(s => s.HasItemsForManagedInstance(It.IsAny<string>())).Returns(true);
                StateProvider.Setup(s => s.HasStatusSummaryForManagedInstance(It.IsAny<string>())).Returns(true);

                Backend.Setup(c => c.GetHubManagedInstance(InstanceId, It.IsAny<AuthContext>())).ReturnsAsync(storedInstance!);
                Backend.Setup(c => c.AddHubManagedInstance(It.IsAny<ManagedInstanceInfo>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((ManagedInstanceInfo i, AuthContext _) => new Certify.Models.Config.ActionResult<ManagedInstanceInfo>("Added", true, i));

                Context.Setup(c => c.ConnectionId).Returns("connection-1");
                Context.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Sid, JoiningPrincipalId),
                        new Claim(HubTokenPurposes.HubAssignedIdClaimType, InstanceId),
                        new Claim(HubTokenPurposes.ClaimType, HubTokenPurposes.ManagementHubJoin)
                    ],
                    "Bearer")));

                Hub = new InstanceManagementHub(StateProvider.Object, NullLogger<InstanceManagementHub>.Instance, null!, Backend.Object)
                {
                    Context = Context.Object
                };
            }
        }

        #endregion

        #region instance security principal

        [TestMethod]
        public void IsManagedInstanceOwnPrincipal()
        {
            SecurityPrincipal Principal(SecurityPrincipalType type, string? externalIdentifier, bool isBuiltIn = false)
                => new() { Id = "principal", PrincipalType = type, ExternalIdentifier = externalIdentifier, IsBuiltIn = isBuiltIn };

            Assert.IsTrue(CertifyManager.IsManagedInstanceOwnPrincipal(Principal(SecurityPrincipalType.ManagedInstance, InstanceId), InstanceId));
            Assert.IsTrue(CertifyManager.IsManagedInstanceOwnPrincipal(Principal(SecurityPrincipalType.ManagedInstance, null), InstanceId), "a principal created before the instance id was recorded on it");

            Assert.IsFalse(CertifyManager.IsManagedInstanceOwnPrincipal(Principal(SecurityPrincipalType.User, null), InstanceId), "a user is never adopted");
            Assert.IsFalse(CertifyManager.IsManagedInstanceOwnPrincipal(Principal(SecurityPrincipalType.ManagedInstance, OtherInstanceId), InstanceId), "another instance's principal is never adopted");
            Assert.IsFalse(CertifyManager.IsManagedInstanceOwnPrincipal(Principal(SecurityPrincipalType.ManagedInstance, null, isBuiltIn: true), InstanceId), "a built in principal is never adopted");

            var legacy = Principal(SecurityPrincipalType.Application, null);
            legacy.Id = InstanceId;
            Assert.IsTrue(CertifyManager.IsManagedInstanceOwnPrincipal(legacy, InstanceId), "the earlier scheme gave an instance's principal the instance id as its id");
        }

        #endregion
    }
}
