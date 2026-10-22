using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Core.Tests.Unit;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// A secret field of a stored credential can take its value from an API Key or Token stored credential. Saving a
    /// link through the hub must not let a caller use a credential they could not see in the credential listing, so
    /// each linked credential is checked against the caller's scope as well as the credential being saved.
    /// </summary>
    [TestClass]
    public class StoredCredentialLinkScopeTests
    {
        private const string CallerId = "sp-scoped";
        private const string InstanceId = "instance-1";

        private const string EditedId = "azure-a";
        private const string TokenWithinId = "token-a";
        private const string TokenOutsideId = "token-b";
        private const string PasswordWithinId = "password-a";

        private static TagScope TeamA => new() { CategoryKey = "team", Value = "a" };

        [TestMethod]
        [Description("A scoped caller can link to an API Key or Token credential within their scope")]
        public async Task Update_LinkWithinScope_IsSaved()
        {
            var harness = new Harness(tagScopes: [TeamA]);

            var result = await harness.Controller().UpdateStoredCredential(InstanceId, Edited(TokenWithinId));

            Assert.IsInstanceOfType<OkObjectResult>(result);
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.UpdateStoredCredential);
        }

        [TestMethod]
        [Description("A scoped caller cannot link to a credential they could not see, even knowing its storage key")]
        public async Task Update_LinkOutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [TeamA]);

            AssertNotFound(await harness.Controller().UpdateStoredCredential(InstanceId, Edited(TokenOutsideId)));
            AssertNotSaved(harness);
        }

        [TestMethod]
        [Description("A credential without links is saved as before")]
        public async Task Update_NoLinks_IsSaved()
        {
            var harness = new Harness(tagScopes: [TeamA]);

            var item = Edited(null);

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Controller().UpdateStoredCredential(InstanceId, item));
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.UpdateStoredCredential);
        }

        [TestMethod]
        [Description("A link to a credential which does not exist on the instance is refused, whoever the caller is")]
        public async Task Update_LinkToMissingCredential_IsNotFound()
        {
            var harness = new Harness();

            AssertNotFound(await harness.Controller().UpdateStoredCredential(InstanceId, Edited("missing")));
            AssertNotSaved(harness);
        }

        [TestMethod]
        [Description("A secret can only take its value from an API Key or Token credential")]
        public async Task Update_LinkToOtherCredentialType_IsRefused()
        {
            var harness = new Harness();

            var result = await harness.Controller().UpdateStoredCredential(InstanceId, Edited(PasswordWithinId));

            Assert.AreEqual(StatusCodes.Status400BadRequest, ((ObjectResult)result).StatusCode);
            AssertNotSaved(harness);
        }

        [TestMethod]
        [Description("A scoped caller can unlock a credential whose linked credentials are all within their scope")]
        public async Task Unlock_LinksWithinScope_IsSent()
        {
            var harness = new Harness(tagScopes: [TeamA]);
            harness.EditedLinks.Add(TokenWithinId);

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Controller().UnlockStoredCredential(InstanceId, EditedId));
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.UnlockStoredCredential);
        }

        [TestMethod]
        [Description("Unlocking releases linked values too, so a credential linked to one outside the caller's scope is not unlocked")]
        public async Task Unlock_LinkOutsideScope_IsForbidden()
        {
            var harness = new Harness(tagScopes: [TeamA]);
            harness.EditedLinks.Add(TokenOutsideId);

            var result = await harness.Controller().UnlockStoredCredential(InstanceId, EditedId);

            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
            StringAssert.DoesNotMatch(((ProblemDetails)((ObjectResult)result).Value!).Detail!, new System.Text.RegularExpressions.Regex("Team B"), "the out of scope credential should not be named");
            CollectionAssert.DoesNotContain(harness.SentCommands, ManagementHubCommands.UnlockStoredCredential, "the credential reached the instance");
        }

        [TestMethod]
        [Description("An unrestricted caller's unlock does not need the credential's links")]
        public async Task Unlock_UnrestrictedCaller_DoesNotCheckLinks()
        {
            var harness = new Harness();
            harness.EditedLinks.Add(TokenOutsideId);

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Controller().UnlockStoredCredential(InstanceId, EditedId));
            CollectionAssert.DoesNotContain(harness.SentCommands, ManagementHubCommands.GetStoredCredentialLinks);
        }

        #region Fixtures

        /// <summary>
        /// The edited Azure credential, its secret linked to the given credential, or holding its own value when null
        /// </summary>
        private static StoredCredential Edited(string? linkedStorageKey)
        {
            var values = new Dictionary<string, string> { ["tenantid"] = "tenant-1" };

            if (linkedStorageKey != null)
            {
                values[StoredCredentialLinks.GetLinkKey("secret")] = linkedStorageKey;
            }
            else
            {
                values["secret"] = "typed-secret";
            }

            return new StoredCredential { StorageKey = EditedId, ProviderType = "ExternalAuth.Azure.ClientSecret", Title = "Azure", Secret = Newtonsoft.Json.JsonConvert.SerializeObject(values) };
        }

        private static readonly List<StoredCredential> InstanceCredentials =
        [
            new() { StorageKey = EditedId, Title = "Azure", ProviderType = "ExternalAuth.Azure.ClientSecret" },
            new() { StorageKey = TokenWithinId, Title = "Team A Token", ProviderType = StandardAuthTypes.STANDARD_AUTH_API_TOKEN },
            new() { StorageKey = TokenOutsideId, Title = "Team B Token", ProviderType = StandardAuthTypes.STANDARD_AUTH_API_TOKEN },
            new() { StorageKey = PasswordWithinId, Title = "Team A Password", ProviderType = StandardAuthTypes.STANDARD_AUTH_PASSWORD }
        ];

        private static readonly Dictionary<string, string> CredentialTeams = new()
        {
            [EditedId] = "a",
            [TokenWithinId] = "a",
            [TokenOutsideId] = "b",
            [PasswordWithinId] = "a"
        };

        private static void AssertNotFound(IActionResult result)
        {
            Assert.IsInstanceOfType<ObjectResult>(result, result.GetType().Name);
            Assert.AreEqual(StatusCodes.Status404NotFound, ((ObjectResult)result).StatusCode);
        }

        private static void AssertNotSaved(Harness harness)
            => CollectionAssert.DoesNotContain(harness.SentCommands, ManagementHubCommands.UpdateStoredCredential, "the credential reached the instance");

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);

        /// <summary>
        /// A caller holding every stored credential action through one role assignment, which may be scoped to tags, and
        /// a hub whose instance is served in-process so the commands reaching it can be recorded
        /// </summary>
        private sealed class Harness
        {
            public Mock<ICertifyInternalApiClient> Client { get; } = new();

            public List<string> SentCommands { get; } = [];

            /// <summary>
            /// The credentials the edited credential links to, as its instance reports them
            /// </summary>
            public List<string> EditedLinks { get; } = [];

            private readonly ManagementAPI _mgmtApi;

            public Harness(List<TagScope>? tagScopes = null)
            {
                var role = new AssignedRole { Id = "ar-scoped", RoleId = StandardRoles.CertificateManager.Id, SecurityPrincipalId = CallerId, ScopedTags = tagScopes };

                Client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(true);

                Client.Setup(c => c.GetSecurityPrincipalAssignedRoles(CallerId, It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<AssignedRole> { role });

                Client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(new ResourceAccessScope { HasAccess = true, IsUnrestricted = tagScopes == null, AuthorizingRoles = [role] });

                Client.Setup(c => c.GetHubItemTags(TaggedItemTypes.StoredCredential, It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((string _, string itemId, AuthContext _) => CredentialTeams.TryGetValue(itemId, out var team)
                        ? [new TagSummary { CategoryKey = "team", Value = team, InstanceId = InstanceId }]
                        : []);

                var stateProvider = new Mock<IInstanceManagementStateProvider>();
                stateProvider.Setup(s => s.GetManagementHubInstanceId()).Returns(InstanceId);
                stateProvider.Setup(s => s.GetManagedInstanceItems(It.IsAny<string?>())).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, ManagedInstanceItems>());

                var certifyManager = new Mock<Certify.Management.ICertifyManager>();
                certifyManager.Setup(m => m.PerformHubCommandWithResult(It.IsAny<InstanceCommandRequest>()))
                    .ReturnsAsync((InstanceCommandRequest cmd) =>
                    {
                        SentCommands.Add(cmd.CommandType);

                        return new InstanceCommandResult
                        {
                            CommandId = cmd.CommandId,
                            CommandType = cmd.CommandType,
                            Value = cmd.CommandType switch
                            {
                                ManagementHubCommands.GetStoredCredentials => Serialize(InstanceCredentials),
                                ManagementHubCommands.UpdateStoredCredential => Serialize(new Certify.Models.Config.ActionResult("Updated", true)),
                                ManagementHubCommands.GetStoredCredentialLinks => Serialize(EditedLinks),
                                ManagementHubCommands.UnlockStoredCredential => Serialize(new StoredCredentialUnlockResult { IsSuccess = true }),
                                _ => null
                            }
                        };
                    });

                _mgmtApi = new ManagementAPI(
                    stateProvider.Object,
                    new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                    certifyManager.Object,
                    NullLogger<ManagementAPI>.Instance);
            }

            public StoredCredentialController Controller()
            {
                var controller = new StoredCredentialController(NullLogger<StoredCredentialController>.Instance, Client.Object, _mgmtApi);

                var context = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Sid, CallerId)], ApiKeyAuthenticationDefaults.AuthenticationScheme))
                };

                var services = new ServiceCollection();
                services.AddProblemDetailsFactory();
                context.RequestServices = services.BuildServiceProvider();

                controller.ControllerContext = new ControllerContext { HttpContext = context };
                return controller;
            }
        }

        #endregion
    }
}
