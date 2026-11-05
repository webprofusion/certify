using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models;
using Certify.Models.Hub;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// A joining key is what a non-administrator is allowed to issue, so what matters here is that the key can do
    /// nothing but join, and that holding the role grants no route to a broader key.
    /// </summary>
    [TestClass]
    public class ManagedInstanceJoiningKeyTests
    {
        private const string AdminId = "joiningkeytests_admin";
        private const string TechnicianId = "joiningkeytests_technician";
        private const string ViewerId = "joiningkeytests_viewer";

        private AccessControl _access = default!;

        [TestInitialize]
        public async Task TestInitialize()
        {
            var log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<ManagedInstanceJoiningKeyTests>());
            _access = new AccessControl(log, new MemoryObjectStore());

            await AccessControlConfig.UpdateStandardAccessConfig(_access);

            await AddUser(AdminId, StandardRoles.Administrator);
            await AddUser(TechnicianId, StandardRoles.InstanceOnboarding);
            await AddUser(ViewerId, StandardRoles.HubViewer);
        }

        private async Task<AssignedRole> AddUser(string id, Role role)
        {
            await _access.AddSecurityPrincipal(StandardSecurityPrincipals.System, new SecurityPrincipal { Id = id, Username = id, PrincipalType = SecurityPrincipalType.User }, bypassIntegrityCheck: true);

            var assignment = new AssignedRole { Id = Guid.NewGuid().ToString(), RoleId = role.Id, SecurityPrincipalId = id };
            await _access.AddAssignedRole(StandardSecurityPrincipals.System, assignment, bypassIntegrityCheck: true);

            return assignment;
        }

        private async Task<AccessToken> AddToken(string principalId, string title, params string[] scopedAssignedRoles)
        {
            var token = new AccessToken
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = principalId,
                Secret = Guid.NewGuid().ToString(),
                TokenType = AccessTokenTypes.Simple,
                DateCreated = DateTimeOffset.UtcNow
            };

            await _access.AddAssignedAccessToken(StandardSecurityPrincipals.System, new AssignedAccessToken
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                SecurityPrincipalId = principalId,
                ScopedAssignedRoles = [.. scopedAssignedRoles],
                AccessTokens = [token]
            }, bypassIntegrityCheck: true);

            return token;
        }

        private async Task<AssignedAccessToken> AddKey(string title, string? contextUserId = null)
        {
            var result = await _access.AddManagedInstanceJoiningKey(contextUserId ?? TechnicianId, new ManagedInstanceJoiningKeyRequest { Title = title });

            Assert.IsTrue(result.IsSuccess, result.Message);

            return result.Result!;
        }

        private Task<bool> CanDo(AccessToken token, string resourceType, string action)
            => _access.IsAccessTokenAuthorised(token, new AccessCheck(token.ClientId, resourceType, action)).ContinueWith(r => r.Result.IsSuccess);

        #region issuing a key

        [TestMethod]
        [Description("The issued key is on a principal of its own, whose only role is the managed instance role")]
        public async Task JoiningKey_IsOnItsOwnPrincipal_ScopedToTheManagedInstanceRole()
        {
            var key = await AddKey("Contoso Production");

            Assert.AreEqual("Contoso Production", key.Title);

            var assignments = await _access.GetAssignedRoles(AdminId, key.SecurityPrincipalId);
            Assert.HasCount(1, assignments);
            Assert.AreEqual(StandardRoles.ManagedInstance.Id, assignments[0].RoleId);
            CollectionAssert.AreEqual(new[] { assignments[0].Id }, key.ScopedAssignedRoles);

            var principal = await _access.GetSecurityPrincipal(AdminId, key.SecurityPrincipalId);
            Assert.AreEqual(SecurityPrincipalType.Application, principal.PrincipalType);
            Assert.AreEqual("Contoso Production", principal.Title);
        }

        [TestMethod]
        [Description("The key can join the hub and do nothing else its issuer could do")]
        public async Task JoiningKey_CanJoin_ButDoesNotCarryTheIssuersAccess()
        {
            var token = (await AddKey("Contoso Production")).AccessTokens.Single();

            Assert.IsFalse(string.IsNullOrWhiteSpace(token.Secret), "the secret is returned to the issuer");
            Assert.IsTrue(await CanDo(token, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
            Assert.IsFalse(await CanDo(token, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList));
            Assert.IsFalse(await CanDo(token, ResourceTypes.AccessToken, StandardResourceActions.ManagedInstanceJoiningKeyAdd));
        }

        [TestMethod]
        [Description("Each key is on its own principal, so removing one leaves the others working")]
        public async Task JoiningKeys_AreIndependentlyRemovable()
        {
            var first = await AddKey("Contoso Production");
            var second = await AddKey("Fabrikam Production");

            Assert.AreNotEqual(first.SecurityPrincipalId, second.SecurityPrincipalId);

            Assert.IsTrue(await _access.DeleteAssignedAccessToken(AdminId, first.Id));

            Assert.IsFalse(await CanDo(first.AccessTokens.Single(), ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
            Assert.IsTrue(await CanDo(second.AccessTokens.Single(), ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
        }

        [TestMethod]
        public async Task JoiningKey_CanBeIssuedByAnAdministrator()
        {
            var key = await AddKey("Contoso Production", AdminId);

            Assert.IsNotNull(key);
        }

        [TestMethod]
        [Description("Holding a role which does not grant the action is refused, and nothing is left behind")]
        public async Task JoiningKey_WithoutTheAction_IsRefused()
        {
            var principalsBefore = (await _access.GetSecurityPrincipals(AdminId)).Count;

            var result = await _access.AddManagedInstanceJoiningKey(ViewerId, new ManagedInstanceJoiningKeyRequest { Title = "Contoso Production" });

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.Result);
            Assert.HasCount(principalsBefore, await _access.GetSecurityPrincipals(AdminId));
            Assert.IsEmpty(await _access.GetAssignedAccessTokens(AdminId));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(AccessControlConfig.ManagedInstanceJoiningTokenTitle)]
        [DataRow(" managed instance hub joining key ")]
        [Description("A key needs a name, and may not take the name the shared key is found by")]
        public async Task JoiningKey_WithBlankOrReservedTitle_IsRefused(string title)
        {
            var result = await _access.AddManagedInstanceJoiningKey(TechnicianId, new ManagedInstanceJoiningKeyRequest { Title = title });

            Assert.IsFalse(result.IsSuccess);
            Assert.IsEmpty(await _access.GetAssignedAccessTokens(AdminId));
        }

        [TestMethod]
        [Description("The onboarding role must not be a route to the general API key operations, which choose the principal and return the secret")]
        public async Task OnboardingRole_CannotUseTheGeneralAccessTokenOperations()
        {
            var adminToken = new AssignedAccessToken
            {
                Id = Guid.NewGuid().ToString(),
                SecurityPrincipalId = AdminId,
                Title = "Mine now",
                AccessTokens = [new AccessToken { Id = Guid.NewGuid().ToString(), ClientId = AdminId, Secret = Guid.NewGuid().ToString(), TokenType = AccessTokenTypes.Simple }]
            };

            Assert.IsFalse(await _access.AddAssignedAccessToken(TechnicianId, adminToken));
            Assert.IsNull(await _access.GetAssignedAccessTokens(TechnicianId));

            foreach (var action in new[] { StandardResourceActions.AccessTokenAdd, StandardResourceActions.AccessTokenList, StandardResourceActions.AccessTokenReadSecret })
            {
                Assert.IsFalse(await _access.IsSecurityPrincipalAuthorised(new AccessCheck(TechnicianId, ResourceTypes.AccessToken, action)), action);
            }
        }

        [TestMethod]
        [Description("The instances page lists instances and loads the tags it filters them by, so the role needs both for a technician to use it")]
        public async Task OnboardingRole_CanUseTheInstancesPage()
        {
            Assert.IsTrue(await _access.IsSecurityPrincipalAuthorised(new AccessCheck(TechnicianId, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList)));
            Assert.IsTrue(await _access.IsSecurityPrincipalAuthorised(new AccessCheck(TechnicianId, ResourceTypes.Tag, StandardResourceActions.TagList)));
        }

        [TestMethod]
        [Description("Add Joining Key stays hidden until an administrator enables it, including for hub settings saved before the option existed")]
        public void ShowAddJoiningKey_IsOffByDefault()
        {
            Assert.IsFalse(new HubSettings().InstanceConnections.ShowAddJoiningKey);

            var saved = System.Text.Json.JsonSerializer.Deserialize<HubSettings>("""{"InstanceConnections":{"EnforceSignedJoiningChecks":true}}""");
            Assert.IsFalse(saved!.InstanceConnections.ShowAddJoiningKey);
        }

        #endregion

        #region key used for a rejoin

        [TestMethod]
        [Description("An instance with no joining principal recorded rejoins with the shared key, whichever other keys exist")]
        public async Task LatestKey_WithNoJoiningPrincipal_IsTheSharedKey()
        {
            var shared = await AddToken(AccessControlConfig.ManagedInstanceSecurityPrincipalId, AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            await AddKey("Contoso Production");

            Assert.AreEqual(shared.Id, (await _access.GetLatestManagedInstanceJoiningKey(AdminId, null))?.Id);
            Assert.AreEqual(shared.Id, (await _access.GetLatestManagedInstanceJoiningKey(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId))?.Id);
        }

        [TestMethod]
        public async Task LatestKey_ForAJoiningPrincipal_IsThatPrincipalsKey()
        {
            await AddToken(AccessControlConfig.ManagedInstanceSecurityPrincipalId, AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            var key = await AddKey("Contoso Production");
            await AddKey("Fabrikam Production");

            var latest = await _access.GetLatestManagedInstanceJoiningKey(AdminId, key.SecurityPrincipalId);

            Assert.AreEqual(key.AccessTokens.Single().Id, latest?.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(latest?.Secret), "the rejoin command needs the secret");
        }

        [TestMethod]
        [Description("Falling back to the shared key would hand an instance a working key just after its own was removed")]
        public async Task LatestKey_AfterThePrincipalsKeyIsRemoved_IsNotTheSharedKey()
        {
            await AddToken(AccessControlConfig.ManagedInstanceSecurityPrincipalId, AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            var key = await AddKey("Contoso Production");

            await _access.DeleteAssignedAccessToken(AdminId, key.Id);

            Assert.IsNull(await _access.GetLatestManagedInstanceJoiningKey(AdminId, key.SecurityPrincipalId));
        }

        [TestMethod]
        [Description("An instance can be joined with any key allowed to join, and a rejoin must not send it that principal's other keys")]
        public async Task LatestKey_DoesNotSelectAKeyWhichCanDoMoreThanJoin()
        {
            var joiningAssignment = new AssignedRole { Id = Guid.NewGuid().ToString(), RoleId = StandardRoles.ManagedInstance.Id, SecurityPrincipalId = AdminId };
            await _access.AddAssignedRole(StandardSecurityPrincipals.System, joiningAssignment, bypassIntegrityCheck: true);

            var adminAssignment = (await _access.GetAssignedRoles(AdminId, AdminId)).Single(a => a.RoleId == StandardRoles.Administrator.Id);

            var joinOnly = await AddToken(AdminId, "Join only", joiningAssignment.Id);
            await AddToken(AdminId, "Unscoped");
            await AddToken(AdminId, "Admin scoped", adminAssignment.Id);
            await AddToken(AdminId, "Both", joiningAssignment.Id, adminAssignment.Id);

            Assert.AreEqual(joinOnly.Id, (await _access.GetLatestManagedInstanceJoiningKey(AdminId, AdminId))?.Id);
        }

        [TestMethod]
        public async Task LatestKey_IsNotAvailableToANonAdministrator()
        {
            var key = await AddKey("Contoso Production");

            Assert.IsNull(await _access.GetLatestManagedInstanceJoiningKey(TechnicianId, key.SecurityPrincipalId));
        }

        #endregion
    }
}
