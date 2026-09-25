using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
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

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The endpoints acting on a single managed item, driven through the controllers.
    ///
    /// Holding a managed item action is checked before the item is known, so on its own it lets a caller whose role
    /// is scoped to some tags or domains read or change any item they can name by id - including ones the item
    /// listing never shows them. Each of these endpoints narrows the action to the item, and an item outside the
    /// caller's scope is reported as not found without the request reaching its instance.
    /// </summary>
    [TestClass]
    public class ManagedItemScopeEndpointTests
    {
        private const string CallerId = "sp-scoped";
        private const string InstanceId = "instance-1";

        /// <summary>
        /// A second instance, holding only an item outside the tag scope the tests use
        /// </summary>
        private const string OtherInstanceId = "instance-2";

        private const string WithinId = "item-within";
        private const string OutsideId = "item-outside";

        private static TagScope Production => new() { CategoryKey = "environment", Value = "production" };

        #region Reading an item

        [TestMethod]
        [Description("A domain restricted caller reads an item whose identifiers are all within their domains")]
        public async Task GetManagedCertificateDetails_WithinDomainScope_ReturnsItem()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            var result = await harness.Certificates().GetManagedCertificateDetails(InstanceId, WithinId);

            Assert.AreEqual(WithinId, ((ManagedCertificate)((OkObjectResult)result).Value!).Id);
        }

        [TestMethod]
        [Description("A domain restricted caller cannot read an item outside their domains by its id")]
        public async Task GetManagedCertificateDetails_OutsideDomainScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound(await harness.Certificates().GetManagedCertificateDetails(InstanceId, OutsideId));
        }

        [TestMethod]
        [Description("A tag scoped caller reads an item carrying their tag")]
        public async Task GetManagedCertificateDetails_WithinTagScope_ReturnsItem()
        {
            var harness = new Harness(tagScopes: [Production]);

            var result = await harness.Certificates().GetManagedCertificateDetails(InstanceId, WithinId);

            Assert.AreEqual(WithinId, ((ManagedCertificate)((OkObjectResult)result).Value!).Id);
        }

        [TestMethod]
        [Description("A tag scoped caller cannot read an item without their tag by its id")]
        public async Task GetManagedCertificateDetails_OutsideTagScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            AssertNotFound(await harness.Certificates().GetManagedCertificateDetails(InstanceId, OutsideId));
        }

        [TestMethod]
        [Description("A caller with no tag or domain restrictions reads any item, as before")]
        public async Task GetManagedCertificateDetails_UnrestrictedCaller_ReturnsAnyItem()
        {
            var harness = new Harness();

            var result = await harness.Certificates().GetManagedCertificateDetails(InstanceId, OutsideId);

            Assert.AreEqual(OutsideId, ((ManagedCertificate)((OkObjectResult)result).Value!).Id);
        }

        [TestMethod]
        [Description("An item the hub has not cached yet is checked against its instance's copy")]
        public async Task DownloadLog_ItemNotCachedByTheHub_IsCheckedAgainstTheInstancesCopy()
        {
            var harness = new Harness(domainRules: ["*.example.com"], cacheItems: false);

            AssertNotFound(await harness.Certificates().DownloadLog(InstanceId, OutsideId));
            Assert.IsInstanceOfType<OkObjectResult>(await harness.Certificates().DownloadLog(InstanceId, WithinId));
        }

        [TestMethod]
        [Description("An item's log is not read for a caller the item is outside the scope of")]
        public async Task DownloadLog_OutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound(await harness.Certificates().DownloadLog(InstanceId, OutsideId));
            AssertNotSent(harness, ManagementHubCommands.GetManagedItemLog);
        }

        [TestMethod]
        [Description("An item's log text download is not read for a caller the item is outside the scope of")]
        public async Task DownloadLogText_OutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            AssertNotFound(await harness.Certificates().DownloadLogText(InstanceId, OutsideId));
            AssertNotSent(harness, ManagementHubCommands.GetManagedItemLog);
        }

        [TestMethod]
        [Description("An item's certificate is not exported for decoding for a caller the item is outside the scope of")]
        public async Task GetDecodedCertificate_OutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound((IActionResult)await harness.Certificates().GetDecodedCertificate(InstanceId, OutsideId, false));
            AssertNotSent(harness, ManagementHubCommands.ExportCertificate);
        }

        [TestMethod]
        [Description("When the caller's role assignments cannot be read the item is refused rather than shown")]
        public async Task GetManagedCertificateDetails_ScopeCannotBeRead_IsRefused()
        {
            var harness = new Harness(tagScopes: [Production]);
            harness.Client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ThrowsAsync(new InvalidOperationException("store unavailable"));

            var result = await harness.Certificates().GetManagedCertificateDetails(InstanceId, WithinId);

            Assert.AreEqual(StatusCodes.Status401Unauthorized, ((ObjectResult)result).StatusCode);
        }

        #endregion

        #region Acting on an item

        [TestMethod]
        [Description("An item outside the caller's scope is not reset")]
        public async Task ResetStatus_OutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound(await harness.Certificates().ResetStatus(InstanceId, OutsideId));
            AssertNotSent(harness, ManagementHubCommands.ResetManagedItemStatus);
        }

        [TestMethod]
        [Description("An item within the caller's scope is reset")]
        public async Task ResetStatus_WithinScope_IsSentToTheInstance()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Certificates().ResetStatus(InstanceId, WithinId));
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.ResetManagedItemStatus);
        }

        [TestMethod]
        [Description("No certificate order is begun for an item outside the caller's scope")]
        public async Task BeginOrder_OutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            AssertNotFound(await harness.Certificates().BeginOrder(InstanceId, OutsideId));
            AssertNotSent(harness, ManagementHubCommands.PerformManagedItemRequest);
        }

        [TestMethod]
        [Description("An item outside the caller's scope is not removed, through the generated endpoint")]
        public async Task RemoveManagedCertificate_OutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound(await harness.Certificates().RemoveManagedCertificate(InstanceId, OutsideId));
            AssertNotSent(harness, ManagementHubCommands.RemoveManagedItem);
        }

        [TestMethod]
        [Description("An item within the caller's scope is removed, through the generated endpoint")]
        public async Task RemoveManagedCertificate_WithinScope_IsSentToTheInstance()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Certificates().RemoveManagedCertificate(InstanceId, WithinId));
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.RemoveManagedItem);
        }

        [TestMethod]
        [Description("No deployment task is run for an item outside the caller's scope, through the generated endpoint")]
        public async Task ExecuteDeploymentTask_OutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound(await harness.DeploymentTasks().ExecuteDeploymentTask(InstanceId, OutsideId, "task-1"));
            AssertNotSent(harness, ManagementHubCommands.ExecuteDeploymentTask);
        }

        #endregion

        #region Submitting an item configuration

        [TestMethod]
        [Description("A save naming an existing item outside the caller's scope does not replace it, whatever the submitted identifiers")]
        public async Task UpdateManagedCertificateDetails_ExistingItemOutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            var submitted = Item(OutsideId, "www.example.com");

            AssertNotFound(await harness.Certificates().UpdateManagedCertificateDetails(InstanceId, submitted));
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        /// <summary>
        /// A principal holding an update role scoped to one tag and a viewing role scoped to another can view items
        /// carrying either tag, but only update those carrying the update role's tag. The viewing role's scope must
        /// not widen what the update action reaches.
        /// </summary>
        [TestMethod]
        [Description("Tag scopes on a role which does not grant the action do not widen what the action reaches")]
        public async Task UpdateManagedCertificateDetails_TagsFromARoleNotGrantingUpdate_DoNotWidenIt()
        {
            var harness = new Harness(tagScopes: [Production]);

            var updater = new AssignedRole { Id = "ar-scoped", RoleId = StandardRoles.CertificateManager.Id, SecurityPrincipalId = CallerId, ScopedTags = [Production] };
            var viewer = new AssignedRole { Id = "ar-viewer", RoleId = StandardRoles.HubViewer.Id, SecurityPrincipalId = CallerId, ScopedTags = [new TagScope { CategoryKey = "environment", Value = "development" }] };

            harness.Client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((AccessCheck check, AuthContext _) => new ResourceAccessScope
                {
                    HasAccess = true,
                    AuthorizingRoles = check.ResourceActionId == StandardResourceActions.ManagedItemList ? [updater, viewer] : [updater]
                });

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Certificates().GetManagedCertificateDetails(InstanceId, OutsideId));

            AssertNotFound(await harness.Certificates().UpdateManagedCertificateDetails(InstanceId, Item(OutsideId, "www.example.org")));
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("An item within the caller's scope cannot be saved with identifiers outside their domains")]
        public async Task UpdateManagedCertificateDetails_IdentifiersOutsideDomainScope_IsRefused()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            var submitted = Item(WithinId, "www.example.com", "www.example.org");

            var result = await harness.Certificates().UpdateManagedCertificateDetails(InstanceId, submitted);

            Assert.AreEqual(StatusCodes.Status401Unauthorized, ((ObjectResult)result).StatusCode);
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("A new item within the caller's domains is saved. It has no tags until saved, so a tag scope does not stop it.")]
        public async Task UpdateManagedCertificateDetails_NewItemWithinScope_IsSaved()
        {
            var harness = new Harness(domainRules: ["*.example.com"], tagScopes: [Production]);

            var submitted = Item("item-new", "new.example.com");

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Certificates().UpdateManagedCertificateDetails(InstanceId, submitted));
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("A configuration test naming an existing item outside the caller's scope is not run")]
        public async Task PerformConfigurationTest_ExistingItemOutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            AssertNotFound(await harness.Certificates().PerformConfigurationTest(InstanceId, Item(OutsideId, "www.example.org")));
            AssertNotSent(harness, ManagementHubCommands.TestManagedItemConfiguration);
        }

        [TestMethod]
        [Description("A caller who is not an administrator cannot add a task which runs a program, even with no tag or domain restrictions")]
        public async Task UpdateManagedCertificateDetails_NonAdminAddingProgramTask_IsForbidden()
        {
            var harness = new Harness();

            var result = await harness.Certificates().UpdateManagedCertificateDetails(InstanceId, WithProgramTask(Item(WithinId, "www.example.com")));

            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("An administrator can add a task which runs a program")]
        public async Task UpdateManagedCertificateDetails_AdminAddingProgramTask_IsSaved()
        {
            var harness = new Harness();
            harness.Client.Setup(c => c.GetSecurityPrincipalAssignedRoles(CallerId, It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<AssignedRole> { new() { Id = "ar-admin", RoleId = StandardRoles.Administrator.Id, SecurityPrincipalId = CallerId } });

            Assert.IsInstanceOfType<OkObjectResult>(await harness.Certificates().UpdateManagedCertificateDetails(InstanceId, WithProgramTask(Item(WithinId, "www.example.com"))));
            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("A caller who is not an administrator cannot test a configuration using the custom script DNS provider")]
        public async Task PerformConfigurationTest_NonAdminUsingScriptDnsProvider_IsForbidden()
        {
            var harness = new Harness();

            var submitted = Item(WithinId, "www.example.com");
            submitted.RequestConfig.Challenges = [new CertRequestChallengeConfig { ChallengeType = "dns-01", ChallengeProvider = ProgramExecutionSettings.ScriptDnsProviderId }];

            var result = await harness.Certificates().PerformConfigurationTest(InstanceId, submitted);

            Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
            AssertNotSent(harness, ManagementHubCommands.TestManagedItemConfiguration);
        }

        private static ManagedCertificate WithProgramTask(ManagedCertificate item)
        {
            item.PostRequestTasks =
            [
                new Certify.Config.DeploymentTaskConfig
                {
                    Id = "task-1",
                    TaskTypeId = ProgramExecutionSettings.ProgramTaskTypeId,
                    Parameters = [new Certify.Models.Config.ProviderParameterSetting("path", "run.cmd")]
                }
            ];

            return item;
        }

        [TestMethod]
        [Description("A preview naming an existing item outside the caller's scope is not produced")]
        public async Task GetPreview_ExistingItemOutsideScope_IsNotFound()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            AssertNotFound(await harness.Previews().GetPreview(Item(OutsideId, "www.example.org")));
            AssertNotFound(await harness.Previews().GetPreviewAsMarkdown(Item(OutsideId, "www.example.org")));
            AssertNotSent(harness, ManagementHubCommands.GetManagedItemRenewalPreview);
        }

        [TestMethod]
        [Description("A new item cannot be added for identifiers outside the caller's domains")]
        public async Task AddManagedCertificate_IdentifiersOutsideDomainScope_IsRefused()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            var result = await harness.Certificates().AddManagedCertificate(new ManagedCertificateAddRequest
            {
                InstanceId = InstanceId,
                Identifiers = [new IdentifierItem("www.example.org")]
            });

            Assert.AreEqual(StatusCodes.Status401Unauthorized, ((ObjectResult)result).StatusCode);
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("A new item is not saved to an instance holding nothing within the caller's scope")]
        public async Task UpdateManagedCertificateDetails_NewItemOnInstanceOutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            var submitted = Item("item-new", "new.example.com");
            submitted.InstanceId = OtherInstanceId;

            AssertNotFound(await harness.Certificates().UpdateManagedCertificateDetails(OtherInstanceId, submitted));
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        /// <summary>
        /// Tags are held against an item id, so a new item reusing the id of an item on another instance would be read
        /// as carrying that item's tags - letting a scoped caller place an item within their scope on any instance.
        /// </summary>
        [TestMethod]
        [Description("A new item may not reuse the id of an item on another instance, whoever the caller is")]
        public async Task UpdateManagedCertificateDetails_NewItemReusingAnotherInstancesItemId_IsConflict()
        {
            var harness = new Harness();

            var submitted = Item(WithinId, "www.example.com");
            submitted.InstanceId = OtherInstanceId;

            var result = await harness.Certificates().UpdateManagedCertificateDetails(OtherInstanceId, submitted);

            Assert.AreEqual(StatusCodes.Status409Conflict, ((ObjectResult)result).StatusCode);
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        [TestMethod]
        [Description("A new item cannot be added to an instance holding nothing within the caller's scope")]
        public async Task AddManagedCertificate_InstanceOutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            var result = await harness.Certificates().AddManagedCertificate(new ManagedCertificateAddRequest
            {
                InstanceId = OtherInstanceId,
                Identifiers = [new IdentifierItem("new.example.com")]
            });

            AssertNotFound(result);
            AssertNotSent(harness, ManagementHubCommands.UpdateManagedItem);
        }

        #endregion

        #region Acting on an instance

        [TestMethod]
        [Description("An instance level endpoint serves an instance holding an item within the caller's tag scope")]
        public async Task GetCertificateAuthorities_InstanceHoldingAnItemInScope_IsServed()
        {
            var harness = new Harness(tagScopes: [Production]);

            await harness.CertificateAuthorities().GetCertificateAuthorities(InstanceId);

            CollectionAssert.Contains(harness.SentCommands, ManagementHubCommands.GetCertificateAuthorities);
        }

        [TestMethod]
        [Description("An instance level endpoint does not serve an instance holding nothing within the caller's tag scope")]
        public async Task GetCertificateAuthorities_InstanceOutsideScope_IsNotFound()
        {
            var harness = new Harness(tagScopes: [Production]);

            AssertNotFound(await harness.CertificateAuthorities().GetCertificateAuthorities(OtherInstanceId));
            AssertNotSent(harness, ManagementHubCommands.GetCertificateAuthorities);
        }

        [TestMethod]
        [Description("An instance carrying the caller's tag is within their scope although it holds no item of theirs")]
        public async Task GetCertificateAuthorities_InstanceCarryingTheCallersTag_IsServed()
        {
            var harness = new Harness(tagScopes: [Production]);
            harness.InstanceTags.Add(new ItemTag(OtherInstanceId, TaggedItemTypes.ManagedInstance, "environment", "production", OtherInstanceId));

            // only the hub's own instance is served in-process, so this asserts the request was not refused
            var result = await harness.CertificateAuthorities().GetCertificateAuthorities(OtherInstanceId);

            Assert.IsFalse(result is ObjectResult { StatusCode: StatusCodes.Status404NotFound }, "an instance carrying the caller's tag is within their scope");
        }

        #endregion

        #region Pending challenges

        [TestMethod]
        [Description("Pending challenges are not attributed to an item, so a caller restricted to some items is shown none")]
        public async Task GetValidationChallenges_RestrictedCaller_IsShownNone()
        {
            var harness = new Harness(domainRules: ["*.example.com"]);

            var result = await harness.Validation().GetValidationChallenges("http-01") as OkObjectResult;

            Assert.IsEmpty((ICollection<SimpleAuthorizationChallengeItem>)result!.Value!);
        }

        [TestMethod]
        [Description("A caller who can see every managed item is shown the pending challenges")]
        public async Task GetValidationChallenges_UnrestrictedCaller_IsShownChallenges()
        {
            var harness = new Harness();

            var result = await harness.Validation().GetValidationChallenges("http-01") as OkObjectResult;

            Assert.HasCount(1, (ICollection<SimpleAuthorizationChallengeItem>)result!.Value!);
        }

        #endregion

        #region Fixtures

        private static void AssertNotFound(IActionResult result)
        {
            Assert.IsInstanceOfType<ObjectResult>(result, result.GetType().Name);
            Assert.AreEqual(StatusCodes.Status404NotFound, ((ObjectResult)result).StatusCode);
        }

        private static void AssertNotSent(Harness harness, string commandType)
        {
            CollectionAssert.DoesNotContain(harness.SentCommands, commandType, $"{commandType} reached the instance");
        }

        /// <summary>
        /// Two items: one on example.com tagged production, and one on example.org tagged development.
        /// </summary>
        private static List<ManagedCertificate> InstanceItems() =>
        [
            Item(WithinId, "www.example.com"),
            Item(OutsideId, "www.example.org")
        ];

        private static readonly Dictionary<string, List<TagSummary>> ItemTags = new()
        {
            [WithinId] = [new TagSummary { CategoryKey = "environment", Value = "production", InstanceId = InstanceId }],
            [OutsideId] = [new TagSummary { CategoryKey = "environment", Value = "development", InstanceId = InstanceId }],
            [OtherInstanceItemId] = [new TagSummary { CategoryKey = "environment", Value = "development", InstanceId = OtherInstanceId }]
        };

        private const string OtherInstanceItemId = "item-on-other-instance";

        /// <summary>
        /// The item tags as the tag store lists them, each recorded against its instance
        /// </summary>
        private static List<ItemTag> AllItemTags() => ItemTags
            .SelectMany(i => i.Value.Select(t => new ItemTag(i.Key, TaggedItemTypes.ManagedCertificate, t.CategoryKey, t.Value, t.InstanceId)))
            .ToList();

        private static ManagedCertificate Item(string id, params string[] domains)
        {
            return new ManagedCertificate
            {
                Id = id,
                InstanceId = InstanceId,
                Name = id,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = domains.First(),
                    SubjectAlternativeNames = domains
                }
            };
        }

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Certify.Shared.JsonOptions.DefaultJsonSerializerOptions);

        /// <summary>
        /// A caller holding every managed item action through one role assignment, which may be scoped to tags and
        /// restricted to domains, and a hub whose instance is served in-process so the commands reaching it can be
        /// recorded.
        /// </summary>
        private sealed class Harness
        {
            public Mock<ICertifyInternalApiClient> Client { get; } = new();

            public List<string> SentCommands { get; } = [];

            /// <summary>
            /// Tags held against the instances themselves, none unless a test adds some
            /// </summary>
            public List<ItemTag> InstanceTags { get; } = [];

            private readonly ManagementAPI _mgmtApi;

            public Harness(List<string>? domainRules = null, List<TagScope>? tagScopes = null, bool cacheItems = true)
            {
                var role = new AssignedRole
                {
                    Id = "ar-scoped",
                    RoleId = StandardRoles.CertificateManager.Id,
                    SecurityPrincipalId = CallerId,
                    ScopedTags = tagScopes,
                    IncludedResources = (domainRules ?? []).Select(d => new Resource { ResourceType = ResourceTypes.Domain, Identifier = d }).ToList()
                };

                Client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(true);

                Client.Setup(c => c.GetSecurityPrincipalAssignedRoles(CallerId, It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<AssignedRole> { role });

                Client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(new ResourceAccessScope { HasAccess = true, IsUnrestricted = tagScopes == null, AuthorizingRoles = [role] });

                Client.Setup(c => c.GetHubItemTags(TaggedItemTypes.ManagedCertificate, It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((string _, string itemId, AuthContext _) => ItemTags.TryGetValue(itemId, out var tags) ? tags : []);

                Client.Setup(c => c.GetAllHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((string _, string _, string itemType, string _, AuthContext _) =>
                        itemType == TaggedItemTypes.ManagedInstance ? InstanceTags.ToList() : AllItemTags());

                Client.Setup(c => c.GetHubManagedInstance(It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((string id, AuthContext _) => new ManagedInstanceInfo { Id = id, InstanceId = id, Title = id });

                Client.Setup(c => c.GetCurrentChallenges(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync(new List<SimpleAuthorizationChallengeItem> { new() { ChallengeType = "http-01", Key = "token", Value = "response" } });

                var cache = new ConcurrentDictionary<string, ManagedInstanceItems>();

                if (cacheItems)
                {
                    cache[InstanceId] = new ManagedInstanceItems { InstanceId = InstanceId, Items = InstanceItems() };
                }

                var otherInstanceItem = Item(OtherInstanceItemId, "www.example.org");
                otherInstanceItem.InstanceId = OtherInstanceId;
                cache[OtherInstanceId] = new ManagedInstanceItems { InstanceId = OtherInstanceId, Items = [otherInstanceItem] };

                var stateProvider = new Mock<IInstanceManagementStateProvider>();
                stateProvider.Setup(s => s.GetManagementHubInstanceId()).Returns(InstanceId);
                stateProvider.Setup(s => s.GetManagedInstanceItems(It.IsAny<string?>())).Returns(cache);

                var certifyManager = new Mock<Certify.Management.ICertifyManager>();
                certifyManager.Setup(m => m.PerformHubCommandWithResult(It.IsAny<InstanceCommandRequest>()))
                    .ReturnsAsync((InstanceCommandRequest cmd) =>
                    {
                        SentCommands.Add(cmd.CommandType);

                        return new InstanceCommandResult
                        {
                            CommandId = cmd.CommandId,
                            CommandType = cmd.CommandType,
                            Value = Respond(cmd)
                        };
                    });

                _mgmtApi = new ManagementAPI(
                    stateProvider.Object,
                    new Mock<IHubContext<InstanceManagementHub, IInstanceManagementHub>>().Object,
                    certifyManager.Object,
                    NullLogger<ManagementAPI>.Instance);
            }

            /// <summary>
            /// The instance's answers. Looking an item up is not recorded as reaching the instance with a request,
            /// as the scope check itself may need to.
            /// </summary>
            private string? Respond(InstanceCommandRequest cmd)
            {
                var args = JsonSerializer.Deserialize<KeyValuePair<string, string>[]>(cmd.Value ?? "[]") ?? [];
                string? Arg(string key) => args.FirstOrDefault(a => a.Key == key).Value;

                switch (cmd.CommandType)
                {
                    case ManagementHubCommands.GetManagedItem:
                        SentCommands.Remove(cmd.CommandType);
                        var item = Arg("instanceId") == InstanceId ? InstanceItems().FirstOrDefault(i => i.Id == Arg("managedCertId")) : null;
                        return item == null ? null : Serialize(item);

                    case ManagementHubCommands.GetManagedItemLog:
                        return Serialize(new[] { new LogItem { Message = "log entry" } });

                    case ManagementHubCommands.ResetManagedItemStatus:
                        return Serialize(InstanceItems().First(i => i.Id == Arg("managedCertId")));

                    case ManagementHubCommands.RemoveManagedItem:
                        return Serialize(new Certify.Models.Config.ActionResult("OK", true));

                    case ManagementHubCommands.UpdateManagedItem:
                        return Arg("managedCert");

                    case ManagementHubCommands.TestManagedItemConfiguration:
                        return Serialize(new List<StatusMessage>());

                    case ManagementHubCommands.GetManagedItemRenewalPreview:
                    case ManagementHubCommands.ExecuteDeploymentTask:
                        return Serialize(new List<ActionStep>());

                    default:
                        return null;
                }
            }

            public CertificateController Certificates()
                => WithCaller(new CertificateController(NullLogger<CertificateController>.Instance, Client.Object, _mgmtApi));

            public PreviewController Previews()
                => WithCaller(new PreviewController(NullLogger<PreviewController>.Instance, Client.Object, _mgmtApi));

            public DeploymentTaskController DeploymentTasks()
                => WithCaller(new DeploymentTaskController(NullLogger<DeploymentTaskController>.Instance, Client.Object, _mgmtApi));

            public CertificateAuthorityController CertificateAuthorities()
                => WithCaller(new CertificateAuthorityController(NullLogger<CertificateAuthorityController>.Instance, Client.Object, _mgmtApi));

            public ValidationController Validation()
                => WithCaller(new ValidationController(NullLogger<ValidationController>.Instance, Client.Object));

            private static T WithCaller<T>(T controller) where T : ControllerBase
            {
                var context = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Sid, CallerId)],
                        ApiKeyAuthenticationDefaults.AuthenticationScheme))
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
