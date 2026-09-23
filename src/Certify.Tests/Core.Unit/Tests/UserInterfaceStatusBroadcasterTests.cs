using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Providers;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The UI status hub used to broadcast every item's progress and full configuration to every connected client,
    /// so a user whose roles were limited by tag scopes or domain restrictions received updates for items the item
    /// listing would never show them. Updates now go only to the clients whose caller may see the item.
    /// </summary>
    [TestClass]
    public class UserInterfaceStatusBroadcasterTests
    {
        private const string InstanceId = "instance-1";

        [TestMethod]
        public async Task ManagedItemUpdate_IsSentOnlyToCallersWhoMaySeeTheItem()
        {
            var harness = new Harness()
                .WithPrincipal("sp-viewer", ViewerRole("ar-viewer"))
                .WithPrincipal("sp-production", ViewerRole("ar-production", scopedTags: [new TagScope { CategoryKey = "environment", Value = "production" }]))
                .WithPrincipal("sp-consumer", new AssignedRole { Id = "ar-consumer", RoleId = StandardRoles.CertificateConsumer.Id })
                .WithItemTags("item-staging", new TagSummary { CategoryKey = "environment", Value = "staging" });

            harness.Connect("conn-viewer", "sp-viewer");
            harness.Connect("conn-production", "sp-production");
            harness.Connect("conn-consumer", "sp-consumer");

            await harness.Broadcaster.SendManagedItemUpdated(Item("item-staging", "secret.example.com"));

            var send = harness.Sends.Single();
            CollectionAssert.AreEqual(new[] { "conn-viewer" }, send.ConnectionIds.ToArray());
            Assert.AreEqual(StatusHubMessages.SendMsg, send.Method);
            Assert.AreEqual(ManagementHubCommands.NotificationUpdatedManagedItem, send.Args[0]);

            // only the item's ids are sent, a client fetches the item itself if it needs it
            var payload = (string)send.Args[1]!;
            var notification = JsonSerializer.Deserialize<ManagedItemChangeNotification>(payload)!;
            Assert.AreEqual(InstanceId, notification.InstanceId);
            Assert.AreEqual("item-staging", notification.ManagedItemId);
            Assert.AreEqual("updated", notification.Action);
            Assert.IsFalse(payload.Contains("secret.example.com"), "the item's configuration must not be sent");
        }

        [TestMethod]
        public async Task ManagedItemUpdate_IsSentToATagScopedCallerForAnItemWithinTheirScope()
        {
            var harness = new Harness()
                .WithPrincipal("sp-production", ViewerRole("ar-production", scopedTags: [new TagScope { CategoryKey = "environment", Value = "production" }]))
                .WithItemTags("item-production", new TagSummary { CategoryKey = "Environment", Value = "Production" });

            harness.Connect("conn-production", "sp-production");

            await harness.Broadcaster.SendManagedItemUpdated(Item("item-production", "www.example.com"));

            CollectionAssert.AreEqual(new[] { "conn-production" }, harness.Sends.Single().ConnectionIds.ToArray());
        }

        [TestMethod]
        public async Task ManagedItemUpdate_DomainRestrictedCallerIsSentOnlyItemsEntirelyWithinTheirDomains()
        {
            var harness = new Harness()
                .WithPrincipal("sp-domain", ViewerRole("ar-domain", domainRules: ["*.example.com"]));

            harness.Connect("conn-domain", "sp-domain");

            await harness.Broadcaster.SendManagedItemUpdated(Item("item-within", "www.example.com"));
            await harness.Broadcaster.SendManagedItemUpdated(Item("item-partly-outside", "www.example.com", "www.example.org"));
            await harness.Broadcaster.SendManagedItemUpdated(Item("item-no-identifiers"));

            var notification = JsonSerializer.Deserialize<ManagedItemChangeNotification>((string)harness.Sends.Single().Args[1]!)!;
            Assert.AreEqual("item-within", notification.ManagedItemId);
        }

        [TestMethod]
        public async Task RequestProgress_IsFilteredByTheCachedItemAndOmitsTheRequestResult()
        {
            var item = Item("item-1", "www.example.com");

            var harness = new Harness()
                .WithPrincipal("sp-example", ViewerRole("ar-example", domainRules: ["*.example.com"]))
                .WithPrincipal("sp-other", ViewerRole("ar-other", domainRules: ["*.example.org"]))
                .WithCachedItem(item);

            harness.Connect("conn-example", "sp-example");
            harness.Connect("conn-other", "sp-other");

            // an error state carries the request result, which holds the whole managed item
            var state = new RequestProgressState(RequestState.Error, "Request failed", item)
            {
                Result = new CertificateRequestResult(item, false, "Request failed")
            };

            await harness.Broadcaster.SendRequestProgress(state);

            var send = harness.Sends.Single();
            CollectionAssert.AreEqual(new[] { "conn-example" }, send.ConnectionIds.ToArray());
            Assert.AreEqual(StatusHubMessages.SendProgressStateMsg, send.Method);

            var sent = (RequestProgressState)send.Args[0]!;
            Assert.AreEqual("item-1", sent.ManagedCertificate?.Id);
            Assert.AreEqual(RequestState.Error, sent.CurrentState);
            Assert.AreEqual("Request failed", sent.Message);
            Assert.IsNull(sent.Result, "the request result carries the full managed item");
        }

        [TestMethod]
        public async Task ManagedItemRemoved_IsFilteredByTheCachedItem()
        {
            var harness = new Harness()
                .WithPrincipal("sp-example", ViewerRole("ar-example", domainRules: ["*.example.com"]))
                .WithPrincipal("sp-other", ViewerRole("ar-other", domainRules: ["*.example.org"]))
                .WithCachedItem(Item("item-1", "www.example.com"));

            harness.Connect("conn-example", "sp-example");
            harness.Connect("conn-other", "sp-other");

            await harness.Broadcaster.SendManagedItemRemoved(InstanceId, "item-1");

            var send = harness.Sends.Single();
            CollectionAssert.AreEqual(new[] { "conn-example" }, send.ConnectionIds.ToArray());
            Assert.AreEqual(ManagementHubCommands.NotificationRemovedManagedItem, send.Args[0]);
            Assert.AreEqual("deleted", JsonSerializer.Deserialize<ManagedItemChangeNotification>((string)send.Args[1]!)!.Action);
        }

        /// <summary>
        /// A caller connected with an API token scoped to specific role assignments is evaluated with that scope,
        /// not as the principal's full role set, and separately from the same principal connected without one.
        /// </summary>
        [TestMethod]
        public async Task ManagedItemUpdate_ScopedTokenConnectionIsEvaluatedWithItsScope()
        {
            var harness = new Harness()
                .WithPrincipal("sp-app",
                    ViewerRole("ar-viewer"),
                    new AssignedRole { Id = "ar-consumer", RoleId = StandardRoles.CertificateConsumer.Id });

            harness.Connect("conn-session", "sp-app");
            harness.Connect("conn-token", "sp-app", scopedAssignedRoles: ["ar-consumer"]);

            await harness.Broadcaster.SendManagedItemUpdated(Item("item-1", "www.example.com"));

            CollectionAssert.AreEqual(new[] { "conn-session" }, harness.Sends.Single().ConnectionIds.ToArray());
        }

        [TestMethod]
        public async Task ManagedItemUpdate_IsNotSentWhenACallersRolesCannotBeRead()
        {
            var harness = new Harness()
                .WithPrincipal("sp-viewer", ViewerRole("ar-viewer"));

            harness.Client
                .Setup(c => c.GetSecurityPrincipalAssignedRoles("sp-viewer", It.IsAny<AuthContext>()))
                .ThrowsAsync(new System.Exception("data store unavailable"));

            harness.Connect("conn-viewer", "sp-viewer");

            await harness.Broadcaster.SendManagedItemUpdated(Item("item-1", "www.example.com"));

            Assert.IsEmpty(harness.Sends, "an unreadable role assignment must not read as unrestricted");
        }

        [TestMethod]
        public async Task DiagnosticActionRequired_IsSentOnlyToAdministrators()
        {
            var harness = new Harness()
                .WithPrincipal("sp-admin", new AssignedRole { Id = "ar-admin", RoleId = StandardRoles.Administrator.Id })
                .WithPrincipal("sp-viewer", ViewerRole("ar-viewer"));

            harness.Connect("conn-admin", "sp-admin");
            harness.Connect("conn-viewer", "sp-viewer");

            await harness.Broadcaster.SendDiagnosticActionRequired(new DiagnosticActionRequired { Key = "datastore", Title = "Data store unreachable" });

            var send = harness.Sends.Single();
            CollectionAssert.AreEqual(new[] { "conn-admin" }, send.ConnectionIds.ToArray());
            Assert.AreEqual(StatusHubMessages.NotificationActionRequired, send.Args[0]);
        }

        [TestMethod]
        public async Task DisconnectedClient_IsNoLongerSentUpdates()
        {
            var harness = new Harness()
                .WithPrincipal("sp-viewer", ViewerRole("ar-viewer"));

            harness.Connect("conn-viewer", "sp-viewer");
            harness.Broadcaster.RemoveConnection("conn-viewer");

            await harness.Broadcaster.SendManagedItemUpdated(Item("item-1", "www.example.com"));

            Assert.IsEmpty(harness.Sends);
        }

        private static AssignedRole ViewerRole(string id, List<TagScope>? scopedTags = null, List<string>? domainRules = null)
        {
            return new AssignedRole
            {
                Id = id,
                RoleId = StandardRoles.HubViewer.Id,
                ScopedTags = scopedTags,
                IncludedResources = domainRules?.Select(d => new Resource { ResourceType = ResourceTypes.Domain, Identifier = d }).ToList() ?? []
            };
        }

        private static ManagedCertificate Item(string id, params string[] domains)
        {
            return new ManagedCertificate
            {
                Id = id,
                InstanceId = InstanceId,
                Name = domains.FirstOrDefault() ?? id,
                RequestConfig = new CertRequestConfig
                {
                    PrimaryDomain = domains.FirstOrDefault(),
                    SubjectAlternativeNames = domains
                }
            };
        }

        /// <summary>
        /// A broadcaster over a stand-in access control store, in which the Hub Viewer and Administrator roles grant
        /// the managed item list action and no other role does.
        /// </summary>
        private sealed class Harness
        {
            private static readonly string[] ListingRoleIds = [StandardRoles.HubViewer.Id, StandardRoles.Administrator.Id];

            private readonly Dictionary<string, List<AssignedRole>> _assignedRoles = [];
            private readonly Dictionary<string, List<TagSummary>> _itemTags = [];
            private readonly ConcurrentDictionary<string, ManagedInstanceItems> _cachedItems = new();

            public Mock<ICertifyInternalApiClient> Client { get; } = new();

            public List<(IReadOnlyList<string> ConnectionIds, string Method, object?[] Args)> Sends { get; } = [];

            public UserInterfaceStatusBroadcaster Broadcaster { get; }

            public Harness()
            {
                Client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AccessCheck check, AuthContext _) =>
                        check.ResourceActionId == StandardResourceActions.ManagedItemList
                        && GetListingRoles(check.SecurityPrincipalId, check.ScopedAssignedRoles).Any());

                Client.Setup(c => c.GetSecurityPrincipalAssignedRoles(It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((string id, AuthContext _) => GetRoles(id));

                Client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AccessCheck check, AuthContext _) =>
                    {
                        var authorizingRoles = GetListingRoles(check.SecurityPrincipalId, check.ScopedAssignedRoles).ToList();
                        return new ResourceAccessScope { HasAccess = authorizingRoles.Count > 0, AuthorizingRoles = authorizingRoles };
                    });

                Client.Setup(c => c.GetHubItemTags(TaggedItemTypes.ManagedCertificate, It.IsAny<string>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((string _, string itemId, AuthContext _) =>
                        _itemTags.TryGetValue(itemId, out var tags) ? tags : new List<TagSummary>());

                var hubClients = new Mock<IHubClients>();
                hubClients.Setup(c => c.Clients(It.IsAny<IReadOnlyList<string>>()))
                    .Returns((IReadOnlyList<string> connectionIds) =>
                    {
                        var proxy = new Mock<IClientProxy>();
                        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                            .Callback((string method, object?[] args, CancellationToken _) => Sends.Add((connectionIds.ToList(), method, args)))
                            .Returns(Task.CompletedTask);
                        return proxy.Object;
                    });

                var hubContext = new Mock<IHubContext<UserInterfaceStatusHub>>();
                hubContext.Setup(h => h.Clients).Returns(hubClients.Object);

                var stateProvider = new Mock<IInstanceManagementStateProvider>();
                stateProvider.Setup(s => s.GetManagedInstanceItems(It.IsAny<string?>())).Returns(_cachedItems);

                Broadcaster = new UserInterfaceStatusBroadcaster(
                    hubContext.Object,
                    Client.Object,
                    stateProvider.Object,
                    new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<UserInterfaceStatusBroadcaster>.Instance);
            }

            public Harness WithPrincipal(string securityPrincipalId, params AssignedRole[] assignedRoles)
            {
                foreach (var role in assignedRoles)
                {
                    role.SecurityPrincipalId = securityPrincipalId;
                }

                _assignedRoles[securityPrincipalId] = [.. assignedRoles];
                return this;
            }

            public Harness WithItemTags(string itemId, params TagSummary[] tags)
            {
                _itemTags[itemId] = [.. tags];
                return this;
            }

            public Harness WithCachedItem(ManagedCertificate item)
            {
                _cachedItems.GetOrAdd(InstanceId, _ => new ManagedInstanceItems { InstanceId = InstanceId }).Items.Add(item);
                return this;
            }

            public void Connect(string connectionId, string securityPrincipalId, List<string>? scopedAssignedRoles = null)
            {
                Broadcaster.AddConnection(connectionId, new AuthContext { UserId = securityPrincipalId, ScopedAssignedRoles = scopedAssignedRoles });
            }

            private List<AssignedRole> GetRoles(string? securityPrincipalId)
            {
                return securityPrincipalId != null && _assignedRoles.TryGetValue(securityPrincipalId, out var roles) ? roles : [];
            }

            private IEnumerable<AssignedRole> GetListingRoles(string? securityPrincipalId, ICollection<string>? scopedAssignedRoles)
            {
                return ResourceAccess.FilterToScopedAssignments(GetRoles(securityPrincipalId), scopedAssignedRoles)
                    .Where(r => ListingRoleIds.Contains(r.RoleId));
            }
        }
    }
}
