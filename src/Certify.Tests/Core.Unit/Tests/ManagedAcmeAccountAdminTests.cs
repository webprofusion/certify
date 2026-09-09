using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Providers;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Models.Acme;
using Certify.Server.Hub.Api.Services.Acme;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using ActionResultConfig = Certify.Models.Config.ActionResult;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// Administration of the ACME accounts which ACME clients have registered with the hub Managed ACME service.
    ///
    /// Accounts are stored keyed by their account url, which is what the client signs with, but an operator acts
    /// on the id within that url. The account itself only records the ids of the principal and access token it was
    /// registered against, so the listing has to resolve those to names for them to mean anything.
    /// </summary>
    [TestClass]
    public class ManagedAcmeAccountAdminTests
    {
        private const string OperatorPrincipalId = "sp-operator";
        private const string OwnerPrincipalId = "sp-build-agent";
        private const string AccessTokenId = "eab-key-id";

        [TestMethod]
        [Description("The account listing resolves the identity each account was registered against")]
        public async Task GetAccounts_ResolvesTheOwningIdentity()
        {
            var fixture = new Fixture();
            var accountId = await fixture.StoreAccount(lastUsed: DateTimeOffset.UtcNow.AddMinutes(-5));

            var result = await fixture.CreateController().GetManagedAcmeAccounts();

            var accounts = AssertOk<List<ManagedAcmeAccountSummary>>(result);
            var account = accounts.Single(a => a.AccountId == accountId);

            Assert.AreEqual("build-agent", account.SecurityPrincipalTitle, "the owning principal is reported by name");
            Assert.AreEqual(SecurityPrincipalType.User, account.PrincipalType);
            Assert.AreEqual("Build agent token", account.AccessTokenTitle, "the token whose EAB key registered the account is reported by name");
            Assert.AreEqual(AccessTokenId, account.AccessTokenId);
            Assert.IsNotNull(account.DateLastUsed);
            Assert.IsNotNull(account.DateCreated);
        }

        /// <summary>
        /// An account registered before ownership was tracked, or one whose principal has since been removed, is
        /// still the account an operator needs to see in order to remove it.
        /// </summary>
        [TestMethod]
        [Description("An account whose owner cannot be resolved is still listed")]
        public async Task GetAccounts_UnresolvableOwner_IsStillListed()
        {
            var fixture = new Fixture();
            var accountId = await fixture.StoreAccount(securityPrincipalId: "sp-since-deleted");

            var result = await fixture.CreateController().GetManagedAcmeAccounts();

            var account = AssertOk<List<ManagedAcmeAccountSummary>>(result).Single(a => a.AccountId == accountId);

            Assert.AreEqual("sp-since-deleted", account.SecurityPrincipalId);
            Assert.IsNull(account.SecurityPrincipalTitle);
        }

        [TestMethod]
        [Description("Listing accounts is refused without the managed ACME list action")]
        public async Task GetAccounts_WithoutTheListAction_IsForbidden()
        {
            var fixture = new Fixture { Authorized = false };
            await fixture.StoreAccount();

            var result = await fixture.CreateController().GetManagedAcmeAccounts();

            Assert.IsInstanceOfType<ForbidResult>(result);
            Assert.AreEqual(StandardResourceActions.ManagedAcmeAccountList, fixture.CheckedActions.LastOrDefault());
        }

        [TestMethod]
        [Description("Removing an account removes the account and the account key the client signs with")]
        public async Task RemoveAccount_RemovesTheAccountAndItsKey()
        {
            var fixture = new Fixture();
            var accountId = await fixture.StoreAccount();
            var accountUrl = fixture.AccountUrl(accountId);

            var result = await fixture.CreateController().RemoveManagedAcmeAccount(accountId);

            var actionResult = AssertOk<ActionResultConfig>(result);

            Assert.IsTrue(actionResult.IsSuccess, actionResult.Message);
            Assert.IsNull(await fixture.Config.GetAccount(accountUrl), "the account is removed");
            Assert.IsNull(await fixture.Config.GetAccountKey(accountUrl), "the account key is removed, so the client can no longer sign requests");
            Assert.AreEqual(StandardResourceActions.ManagedAcmeAccountDelete, fixture.CheckedActions.LastOrDefault());
        }

        /// <summary>
        /// Reporting success for an account which was not there would tell an operator a client had been revoked
        /// when it had not.
        /// </summary>
        [TestMethod]
        [Description("Removing an account which is not registered reports that it was not found")]
        public async Task RemoveAccount_UnknownAccount_ReportsNotFound()
        {
            var fixture = new Fixture();
            await fixture.StoreAccount();

            var result = await fixture.CreateController().RemoveManagedAcmeAccount("not-a-registered-account");

            var actionResult = AssertOk<ActionResultConfig>(result);

            Assert.IsFalse(actionResult.IsSuccess);
            StringAssert.Contains(actionResult.Message, "not found");
        }

        [TestMethod]
        [Description("Removing an account is refused without the managed ACME delete action")]
        public async Task RemoveAccount_WithoutTheDeleteAction_IsForbidden()
        {
            var fixture = new Fixture { Authorized = false };
            var accountId = await fixture.StoreAccount();
            var accountUrl = fixture.AccountUrl(accountId);

            var result = await fixture.CreateController().RemoveManagedAcmeAccount(accountId);

            Assert.IsInstanceOfType<ForbidResult>(result);
            Assert.IsNotNull(await fixture.Config.GetAccount(accountUrl), "a refused removal leaves the account in place");
        }

        #region Last used

        [TestMethod]
        [Description("Using an account records when it was last used")]
        public async Task RecordAccountUsed_RecordsTheTimeOfUse()
        {
            var fixture = new Fixture();
            var accountUrl = fixture.AccountUrl(await fixture.StoreAccount(lastUsed: null));

            await fixture.Config.RecordAccountUsed(accountUrl);

            var account = await fixture.Config.GetAccount(accountUrl);

            Assert.IsNotNull(account?.DateLastUsed);
            Assert.IsTrue(DateTimeOffset.UtcNow - account.DateLastUsed < TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// A client polling an order signs a request every few seconds, and each one resolves the same account.
        /// Last use is reported to an operator, so it is recorded at minute granularity rather than turning order
        /// polling into a database write per request.
        /// </summary>
        [TestMethod]
        [Description("Repeated use within the write interval does not rewrite the account")]
        public async Task RecordAccountUsed_RepeatedUse_IsNotWrittenEveryTime()
        {
            var fixture = new Fixture();
            var accountUrl = fixture.AccountUrl(await fixture.StoreAccount(lastUsed: null));

            await fixture.Config.RecordAccountUsed(accountUrl);
            var writesAfterFirstUse = fixture.Store.WriteCount;

            await fixture.Config.RecordAccountUsed(accountUrl);
            await fixture.Config.RecordAccountUsed(accountUrl);

            Assert.AreEqual(writesAfterFirstUse, fixture.Store.WriteCount, "the account is only rewritten once per write interval");
        }

        [TestMethod]
        [Description("Using an account which is not registered records nothing")]
        public async Task RecordAccountUsed_UnknownAccount_RecordsNothing()
        {
            var fixture = new Fixture();
            var writesBefore = fixture.Store.WriteCount;

            await fixture.Config.RecordAccountUsed(fixture.AccountUrl(Guid.NewGuid().ToString("N")));

            Assert.AreEqual(writesBefore, fixture.Store.WriteCount);
        }

        #endregion

        #region Fixture

        private static T AssertOk<T>(IActionResult result) where T : class
        {
            Assert.IsInstanceOfType<OkObjectResult>(result, result.GetType().Name);

            var value = ((OkObjectResult)result).Value;

            Assert.IsInstanceOfType<T>(value, value?.GetType().Name ?? "null");

            return (T)value;
        }

        private sealed class Fixture
        {
            /// <summary>Whether the caller's roles grant the action the endpoint asks about.</summary>
            public bool Authorized { get; init; } = true;

            /// <summary>The resource actions the endpoint asked about, in order.</summary>
            public List<string> CheckedActions { get; } = [];

            public FakeConfigurationStore Store { get; } = new();

            public AcmeServerConfig Config { get; }

            public Fixture() => Config = new AcmeServerConfig(Store, "acme-tests");

            public string AccountUrl(string accountId) => $"https://hub.example.com/acme/account/{accountId}";

            /// <summary>
            /// Register an account as the ACME endpoint would, returning the account id an operator acts on.
            /// Ids are unique per account so tests do not share the last-used write state, which is process wide.
            /// </summary>
            public async Task<string> StoreAccount(
                string securityPrincipalId = OwnerPrincipalId,
                DateTimeOffset? lastUsed = null)
            {
                var accountId = Guid.NewGuid().ToString("N");
                var accountUrl = AccountUrl(accountId);

                await Config.StoreAcmeAccount(accountUrl, new AcmeAccount
                {
                    internalId = AccessTokenId,
                    SecurityPrincipalId = securityPrincipalId,
                    Status = AccountStatus.Valid,
                    Contact = ["mailto:ops@example.com"],
                    TermsOfServiceAgreed = true,
                    Orders = $"{accountUrl}/orders",
                    DateCreated = DateTimeOffset.UtcNow.AddDays(-1),
                    DateLastUsed = lastUsed
                });

                await Config.StoreAcmeAccountKey(accountUrl, new JsonWebKey { Kty = "EC", Crv = "P-256" });

                return accountId;
            }

            public ManagedAcmeController CreateController()
            {
                var client = new Mock<ICertifyInternalApiClient>();

                client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ReturnsAsync((AccessCheck check, AuthContext _) =>
                    {
                        CheckedActions.Add(check.ResourceActionId);
                        return Authorized;
                    });

                client.Setup(c => c.GetSecurityPrincipals(It.IsAny<AuthContext>()))
                    .ReturnsAsync(() =>
                    [
                        new SecurityPrincipal
                        {
                            Id = OwnerPrincipalId,
                            Username = "build-agent",
                            PrincipalType = SecurityPrincipalType.User
                        }
                    ]);

                client.Setup(c => c.GetAssignedAccessTokens(It.IsAny<AuthContext>()))
                    .ReturnsAsync(() =>
                    [
                        new AssignedAccessToken
                        {
                            SecurityPrincipalId = OwnerPrincipalId,
                            Title = "Build agent token",
                            AccessTokens = [new AccessToken { Id = AccessTokenId, ClientId = "client-id" }]
                        }
                    ]);

                var claims = new List<Claim> { new(ClaimTypes.Sid, OperatorPrincipalId) };

                return new ManagedAcmeController(NullLogger<ManagedAcmeController>.Instance, client.Object, Config)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext
                        {
                            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                        }
                    }
                };
            }
        }

        /// <summary>
        /// In memory stand in for the hub's own configuration store, round tripping items through json the way the
        /// real store does so that the stored form is exercised rather than the in memory object.
        /// </summary>
        private sealed class FakeConfigurationStore : IConfigurationStore
        {
            private readonly Dictionary<(string ItemType, string Id), string> _items = [];

            /// <summary>Writes performed, so a caller which is meant to write sparingly can be held to it.</summary>
            public int WriteCount { get; private set; }

            public Task<T> Get<T>(string itemType, string id)
            {
                return Task.FromResult(_items.TryGetValue((Normalize<T>(itemType), id), out var json)
                    ? JsonConvert.DeserializeObject<T>(json)
                    : default);
            }

            public Task Add<T>(string itemType, T item) => Update(itemType, item);

            public Task Update<T>(string itemType, T item)
            {
                WriteCount++;
                _items[(Normalize<T>(itemType), GetId(item))] = JsonConvert.SerializeObject(item);
                return Task.CompletedTask;
            }

            public Task<bool> Delete<T>(string itemType, string id) => Task.FromResult(_items.Remove((Normalize<T>(itemType), id)));

            public Task<List<T>> GetItems<T>(string itemType)
            {
                var normalized = Normalize<T>(itemType);

                return Task.FromResult(_items
                    .Where(i => i.Key.ItemType == normalized)
                    .Select(i => JsonConvert.DeserializeObject<T>(i.Value))
                    .ToList());
            }

            public Task<bool> IsInitialised() => Task.FromResult(true);

            public Task<List<SerializedConfigurationItem>> GetAllSerializedItems() => Task.FromResult(new List<SerializedConfigurationItem>());

            public Task UpsertSerializedItem(SerializedConfigurationItem item) => Task.CompletedTask;

            private static string Normalize<T>(string itemType)
                => string.IsNullOrEmpty(itemType) ? typeof(T).Name.ToLowerInvariant() : itemType.ToLowerInvariant();

            private static string GetId<T>(T item)
            {
                return item switch
                {
                    SerializedConfigurationItem configItem => configItem.Id,
                    IIdentifiable identifiable => identifiable.Id,
                    _ => throw new ArgumentException($"Item of type {typeof(T).Name} has no id")
                };
            }
        }

        #endregion
    }
}
