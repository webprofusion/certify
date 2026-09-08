using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Certify.Core.Management.Access;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Models.Hub;
using Certify.Models.Providers;
using Certify.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Cover the standard access control config and the startup migration which applies it.
    ///
    /// The failure this is guarding against is quiet: authorization resolves role -> policy -> action from the
    /// store, and a reference which does not resolve contributes nothing rather than raising an error, so a role
    /// which migrated without all of its actions is indistinguishable from one which was meant to be restrictive.
    /// </summary>
    [TestClass]
    public class StandardAccessConfigTests
    {
        private const string AdminId = AccessControlConfig.AdminSecurityPrincipalId;

        private AccessControl _access = default!;

        [TestInitialize]
        public void TestInitialize()
        {
            var log = new Loggy(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<StandardAccessConfigTests>());
            _access = new AccessControl(log, new MemoryObjectStore());
        }

        #region standard config integrity

        [TestMethod]
        public void StandardConfigReferencesAllResolve()
        {
            var problems = AccessControlConfig.GetStandardConfigIntegrityProblems();

            Assert.IsEmpty(problems, "The standard access config is not internally consistent:\r\n" + string.Join("\r\n", problems));
        }

        [TestMethod]
        public void EveryDeclaredStandardRoleIsIncludedInGetStandardRoles()
        {
            // a role which is declared but never registered is invisible to the migration, so any principal
            // assigned to it keeps whatever definition happened to be stored when it was last registered
            var declared = typeof(StandardRoles)
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(Role))
                .Select(p => (Role)p.GetValue(null)!)
                .ToList();

            var registeredIds = Policies.GetStandardRoles().Select(r => r.Id).ToList();

            var missing = declared.Where(r => !registeredIds.Contains(r.Id)).Select(r => r.Id).ToList();

            Assert.IsEmpty(missing, $"Roles declared on StandardRoles but not returned by GetStandardRoles(): {string.Join(", ", missing)}");
        }

        [TestMethod]
        public void EveryStandardPolicyConstantIsDefined()
        {
            var declared = GetStringConstants(typeof(StandardPolicies));
            var defined = Policies.GetStandardPolicies().Select(p => p.Id).ToList();

            var missing = declared.Where(id => !defined.Contains(id)).ToList();

            Assert.IsEmpty(missing, $"Policy ids declared on StandardPolicies but not defined by GetStandardPolicies(): {string.Join(", ", missing)}");
        }

        [TestMethod]
        public void EveryStandardResourceActionConstantIsRegistered()
        {
            var declared = GetStringConstants(typeof(StandardResourceActions));
            var registered = Policies.GetStandardResourceActions().Select(a => a.Id).ToList();

            var missing = declared.Where(id => !registered.Contains(id)).ToList();

            Assert.IsEmpty(missing, $"Resource action ids declared on StandardResourceActions but not registered by GetStandardResourceActions(): {string.Join(", ", missing)}");
        }

        [TestMethod]
        public void StandardRoleIdsAreStable()
        {
            // Role ids are permanent identifiers: stored role assignments reference them, every admin gate checks
            // StandardRoles.Administrator.Id, and there is no remapping step. Renaming one silently strips every
            // existing assignment of that role, including the one which grants administration.
            var expected = new Dictionary<string, string>
            {
                [nameof(StandardRoles.Administrator)] = "sysadmin_role",
                [nameof(StandardRoles.CertificateManager)] = "cert_manager_role",
                [nameof(StandardRoles.HubViewer)] = "hub_viewer_role",
                [nameof(StandardRoles.CertificateConsumer)] = "cert_consumer_role",
                [nameof(StandardRoles.StoredCredentialConsumer)] = "storedcredential_consumer_role",
                [nameof(StandardRoles.ManagedChallengeAdmin)] = "managedchallenge_admin_role",
                [nameof(StandardRoles.ManagedChallengeConsumer)] = "managedchallenge_consumer_role",
                [nameof(StandardRoles.ManagedInstance)] = "managedinstance_role",
                [nameof(StandardRoles.ManagedAcmeConsumer)] = "managedacme_consumer_role",
            };

            foreach (var (name, id) in expected)
            {
                var role = Policies.GetStandardRoles().FirstOrDefault(r => r.Id == id);
                Assert.IsNotNull(role, $"Standard role {name} should still have the id '{id}'. Changing it invalidates every existing role assignment and scoped access token.");
            }
        }

        private static List<string> GetStringConstants(Type type)
        {
            return type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();
        }

        #endregion

        #region applying the standard config

        [TestMethod]
        public async Task UpdateStandardAccessConfigWritesTheWholeStandardConfig()
        {
            var result = await AccessControlConfig.UpdateStandardAccessConfig(_access);

            AssertNoProblems(result);

            var storedRoles = await _access.GetRoles(AdminId);
            var storedPolicies = await _access.GetResourcePolicies(AdminId);
            var storedActions = await _access.GetResourceActions(AdminId);

            foreach (var role in Policies.GetStandardRoles())
            {
                var stored = storedRoles.FirstOrDefault(r => r.Id == role.Id);
                Assert.IsNotNull(stored, $"Role {role.Id} should be in the store");
                CollectionAssert.AreEqual(role.Policies, stored.Policies, $"Role {role.Id} should hold its standard policies");
            }

            foreach (var policy in Policies.GetStandardPolicies())
            {
                var stored = storedPolicies.FirstOrDefault(p => p.Id == policy.Id);
                Assert.IsNotNull(stored, $"Policy {policy.Id} should be in the store");
                CollectionAssert.AreEqual(policy.ResourceActions, stored.ResourceActions, $"Policy {policy.Id} should grant its standard actions");
            }

            Assert.AreEqual(Policies.GetStandardResourceActions().Count, storedActions.Count, "every standard resource action should be in the store");
        }

        [TestMethod]
        public async Task UpdateStandardAccessConfigIsIdempotent()
        {
            await AccessControlConfig.UpdateStandardAccessConfig(_access);

            var secondRun = await AccessControlConfig.UpdateStandardAccessConfig(_access);

            AssertNoProblems(secondRun);
            Assert.AreEqual(0, secondRun.ResourceActionsUpdated, "an unchanged config should not rewrite resource actions");
            Assert.AreEqual(0, secondRun.ResourcePoliciesUpdated, "an unchanged config should not rewrite policies");
            Assert.AreEqual(0, secondRun.RolesUpdated, "an unchanged config should not rewrite roles");
        }

        [TestMethod]
        public async Task UpdateStandardAccessConfigRepairsARoleWhichIsMissingAPolicy()
        {
            var standard = Policies.GetStandardRoles().First(r => r.Id == StandardRoles.CertificateManager.Id);

            // as stored by a previous version, before this policy was part of the role
            var previousVersion = new Role(standard.Id, standard.Title, standard.Description,
                standard.Policies.Where(p => p != StandardPolicies.StoredCredentialAdmin).ToList());

            await _access.AddRole(AdminId, previousVersion, bypassIntegrityCheck: true);

            var result = await AccessControlConfig.UpdateStandardAccessConfig(_access);

            AssertNoProblems(result);

            var stored = (await _access.GetRoles(AdminId)).First(r => r.Id == standard.Id);
            CollectionAssert.AreEqual(standard.Policies, stored.Policies, "the stored role should be brought up to the standard definition");
        }

        [TestMethod]
        public async Task UpdateStandardAccessConfigRepairsAPolicyWhichIsMissingAnAction()
        {
            await AccessControlConfig.UpdateStandardAccessConfig(_access);

            // roll one stored policy back to how a previous version defined it
            var standard = Policies.GetStandardPolicies().First(p => p.Id == StandardPolicies.ManagedChallengeConsumer);

            await _access.AddResourcePolicy(AdminId, PolicyWithoutAction(standard, StandardResourceActions.ManagedChallengeCleanup), bypassIntegrityCheck: true);

            var result = await AccessControlConfig.UpdateStandardAccessConfig(_access);

            AssertNoProblems(result);
            Assert.AreEqual(1, result.ResourcePoliciesUpdated, "only the stale policy should have been rewritten");

            var stored = (await _access.GetResourcePolicies(AdminId)).First(p => p.Id == standard.Id);
            CollectionAssert.AreEqual(standard.ResourceActions, stored.ResourceActions, "the stored policy should grant the full standard action set");
        }

        [TestMethod]
        public async Task UpdateStandardAccessConfigReportsAStoredRoleWhosePolicyIsMissing()
        {
            // a role left behind by an older version, still assigned to principals, pointing at a policy which is
            // no longer defined. Authorization treats this as "grants nothing" without complaining, so the update
            // has to be the thing which says so.
            await _access.AddRole(AdminId, new Role("legacy_role", "Legacy Role", "Left over from a previous version", ["policy_which_no_longer_exists"]), bypassIntegrityCheck: true);

            var result = await AccessControlConfig.UpdateStandardAccessConfig(_access);

            Assert.IsEmpty(result.Failures, "the standard config itself should still apply cleanly");
            Assert.IsTrue(
                result.IntegrityProblems.Any(p => p.Contains("legacy_role") && p.Contains("policy_which_no_longer_exists")),
                "the unresolved policy reference should be reported: " + string.Join("; ", result.IntegrityProblems));
        }

        /// <summary>
        /// The upgrade case this whole area exists for: a role gains an action in a new build, and the principals
        /// and API access tokens already assigned to it pick it up without being reassigned or reissued.
        /// </summary>
        [TestMethod]
        public async Task AnAssignedRoleAndItsScopedAccessTokenGainActionsAddedByAnUpgrade()
        {
            await AccessControlConfig.UpdateStandardAccessConfig(_access);

            var admin = await SetupAdmin();

            // roll the stored policy back to how a previous version defined it
            var standardPolicy = Policies.GetStandardPolicies().First(p => p.Id == StandardPolicies.ManagedChallengeConsumer);
            await _access.AddResourcePolicy(AdminId, PolicyWithoutAction(standardPolicy, StandardResourceActions.ManagedChallengeCleanup), bypassIntegrityCheck: true);

            // a principal assigned the role under that previous version, with an API token scoped to the assignment
            var consumer = new SecurityPrincipal
            {
                Id = "challenge_consumer_01",
                Username = "challenge_consumer",
                PrincipalType = SecurityPrincipalType.Application,
                Provider = StandardIdentityProviders.INTERNAL
            };

            Assert.IsTrue(await _access.AddSecurityPrincipal(AdminId, consumer, bypassIntegrityCheck: true));

            var assignedRole = new AssignedRole
            {
                Id = Guid.NewGuid().ToString(),
                RoleId = StandardRoles.ManagedChallengeConsumer.Id,
                SecurityPrincipalId = consumer.Id
            };

            Assert.IsTrue(await _access.AddAssignedRole(AdminId, assignedRole, bypassIntegrityCheck: true));

            var token = new AccessToken
            {
                ClientId = consumer.Id,
                Secret = Guid.NewGuid().ToString(),
                TokenType = AccessTokenTypes.Simple,
                DateCreated = DateTimeOffset.UtcNow
            };

            Assert.IsTrue(await _access.AddAssignedAccessToken(admin.Id, new AssignedAccessToken
            {
                Id = Guid.NewGuid().ToString(),
                SecurityPrincipalId = consumer.Id,
                Title = "Challenge consumer integration",
                AccessTokens = [token],
                ScopedAssignedRoles = [assignedRole.Id]
            }));

            var cleanupCheck = new AccessCheck(consumer.Id, ResourceTypes.ManagedChallenge, StandardResourceActions.ManagedChallengeCleanup);

            Assert.IsFalse(
                (await _access.IsAccessTokenAuthorised(AdminId, token, cleanupCheck)).IsSuccess,
                "before the upgrade the previous policy definition should not grant the cleanup action");

            // the upgrade
            var result = await AccessControlConfig.UpdateStandardAccessConfig(_access);
            AssertNoProblems(result);

            Assert.IsTrue(
                (await _access.IsAccessTokenAuthorised(AdminId, token, cleanupCheck)).IsSuccess,
                "the same access token should grant the action the upgrade added to the role's policy");

            // the assignment id is what the token is scoped by, so it has to survive the upgrade untouched
            var assignmentsAfter = await _access.GetAssignedRoles(AdminId, consumer.Id);
            Assert.AreEqual(assignedRole.Id, assignmentsAfter.Single().Id, "the upgrade must not replace an existing role assignment");
        }

        #endregion

        #region standard users and service principals

        [TestMethod]
        public async Task ConfigureStandardUsersAndRolesSetsUpTheHubOnAFirstRun()
        {
            var creds = new TestCredentialsManager();

            var result = await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            AssertNoProblems(result);

            var principals = await _access.GetSecurityPrincipals(AdminId);
            Assert.IsTrue(principals.Any(p => p.Id == AdminId), "the default admin should be created");
            Assert.IsTrue(principals.Any(p => p.Id == AccessControlConfig.ManagedInstanceSecurityPrincipalId), "the managed instance service principal should be created");

            Assert.IsTrue(await _access.IsPrincipalInRole(AdminId, AdminId, StandardRoles.Administrator.Id), "the default admin should hold the administrator role");

            var managedInstanceAssignment = (await _access.GetAssignedRoles(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId)).Single();
            Assert.AreEqual(StandardRoles.ManagedInstance.Id, managedInstanceAssignment.RoleId);

            var joiningToken = (await _access.GetAssignedAccessTokens(AdminId)).Single(t => t.Title == AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            CollectionAssert.AreEqual(new[] { managedInstanceAssignment.Id }, joiningToken.ScopedAssignedRoles, "the joining token should be scoped to the managed instance role assignment");

            Assert.IsNotNull(await creds.GetUnlockedCredential(HubSharedConstants.MgmtHubJoiningCredId), "the joining key credential should be stored");
        }

        [TestMethod]
        public async Task ConfigureStandardUsersAndRolesIsIdempotent()
        {
            var creds = new TestCredentialsManager();

            await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            var tokenBefore = (await _access.GetAssignedAccessTokens(AdminId)).Single(t => t.Title == AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            var assignmentsBefore = (await _access.GetAssignedRoles(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId)).Single();

            var secondRun = await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            AssertNoProblems(secondRun);

            var tokenAfter = (await _access.GetAssignedAccessTokens(AdminId)).Single(t => t.Title == AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            var assignmentsAfter = (await _access.GetAssignedRoles(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId)).Single();

            Assert.AreEqual(tokenBefore.AccessTokens.Single().Secret, tokenAfter.AccessTokens.Single().Secret, "an existing joining token must be kept, managed instances already hold its secret");
            Assert.AreEqual(assignmentsBefore.Id, assignmentsAfter.Id, "an existing role assignment must be kept, the joining token is scoped by its id");
        }

        [TestMethod]
        public async Task ConfigureStandardUsersAndRolesReissuesAMissingJoiningToken()
        {
            var creds = new TestCredentialsManager();

            await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            var original = (await _access.GetAssignedAccessTokens(AdminId)).Single(t => t.Title == AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            Assert.IsTrue(await _access.DeleteAssignedAccessToken(AdminId, original.Id));

            // the stored credential is still present and now points at a token which is gone
            var result = await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            AssertNoProblems(result);

            var reissued = (await _access.GetAssignedAccessTokens(AdminId)).Single(t => t.Title == AccessControlConfig.ManagedInstanceJoiningTokenTitle);
            Assert.AreNotEqual(original.AccessTokens.Single().Secret, reissued.AccessTokens.Single().Secret, "a new joining token should be issued");

            var storedSecret = await creds.GetUnlockedCredential(HubSharedConstants.MgmtHubJoiningCredId);
            Assert.IsTrue(storedSecret!.Contains(reissued.AccessTokens.Single().Secret), "the stored joining credential should be updated to the reissued token");
        }

        [TestMethod]
        public async Task ConfigureStandardUsersAndRolesRepairsAMissingManagedInstanceRoleAssignment()
        {
            var creds = new TestCredentialsManager();

            await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            var assignment = (await _access.GetAssignedRoles(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId)).Single();

            await _access.UpdateAssignedRoles(AdminId, new SecurityPrincipalAssignedRoleUpdate
            {
                SecurityPrincipalId = AccessControlConfig.ManagedInstanceSecurityPrincipalId,
                RemovedAssignedRoles = [assignment]
            });

            Assert.IsFalse(await _access.IsPrincipalInRole(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId, StandardRoles.ManagedInstance.Id));

            var result = await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            AssertNoProblems(result);
            Assert.IsTrue(
                await _access.IsPrincipalInRole(AdminId, AccessControlConfig.ManagedInstanceSecurityPrincipalId, StandardRoles.ManagedInstance.Id),
                "the managed instance role assignment should be restored on startup rather than only when the principal is first created");
        }

        [TestMethod]
        public async Task ConfigureStandardUsersAndRolesDoesNotRecreateADeletedBuiltInAdmin()
        {
            var creds = new TestCredentialsManager();

            // an established deployment which runs its own administrator and has removed the built-in one
            var ownAdmin = new SecurityPrincipal
            {
                Id = "ops_admin_01",
                Username = "ops_admin",
                Password = "a-long-enough-password",
                PrincipalType = SecurityPrincipalType.User,
                Provider = StandardIdentityProviders.INTERNAL
            };

            Assert.IsTrue(await _access.AddSecurityPrincipal(ownAdmin.Id, ownAdmin, bypassIntegrityCheck: true));

            var result = await AccessControlConfig.ConfigureStandardUsersAndRoles(_access, creds);

            var principals = await _access.GetSecurityPrincipals(ownAdmin.Id);

            Assert.IsFalse(
                principals.Any(p => p.Id == AdminId),
                "a deliberately removed built-in admin must not be recreated with the default password on the next restart");

            Assert.IsNotEmpty(result.Failures, "skipping standard principal setup should be reported");

            // the roles and policies themselves still have to be applied, they are what existing assignments resolve against
            Assert.AreEqual(Policies.GetStandardRoles().Count, (await _access.GetRoles(ownAdmin.Id)).Count, "standard roles should still be applied");
        }

        #endregion

        private static void AssertNoProblems(StandardAccessConfigResult result)
        {
            Assert.IsEmpty(result.Failures, "unexpected failures: " + string.Join("; ", result.Failures));
            Assert.IsEmpty(result.IntegrityProblems, "unexpected integrity problems: " + string.Join("; ", result.IntegrityProblems));
        }

        private static ResourcePolicy PolicyWithoutAction(ResourcePolicy standard, string actionId)
        {
            return new ResourcePolicy
            {
                Id = standard.Id,
                Title = standard.Title,
                Description = standard.Description,
                SecurityPermissionType = standard.SecurityPermissionType,
                IsResourceSpecific = standard.IsResourceSpecific,
                ResourceActions = standard.ResourceActions.Where(a => a != actionId).ToList()
            };
        }

        /// <summary>
        /// An admin principal holding the administrator role, so operations which require one can be exercised.
        /// </summary>
        private async Task<SecurityPrincipal> SetupAdmin()
        {
            var admin = new SecurityPrincipal
            {
                Id = AdminId,
                Username = "admin",
                Password = "a-long-enough-password",
                PrincipalType = SecurityPrincipalType.User,
                Provider = StandardIdentityProviders.INTERNAL,
                IsBuiltIn = true
            };

            Assert.IsTrue(await _access.AddSecurityPrincipal(admin.Id, admin, bypassIntegrityCheck: true));

            Assert.IsTrue(await _access.AddAssignedRole(admin.Id, new AssignedRole
            {
                Id = Guid.NewGuid().ToString(),
                RoleId = StandardRoles.Administrator.Id,
                SecurityPrincipalId = admin.Id
            }, bypassIntegrityCheck: true));

            return admin;
        }

        /// <summary>
        /// Minimal credentials manager which behaves like the real one for the joining key: what is written by
        /// Update is what a later GetUnlockedCredential returns.
        /// </summary>
        private sealed class TestCredentialsManager : ICredentialsManager
        {
            private readonly Dictionary<string, StoredCredential> _credentials = new();

            public bool Init(string connectionString, ILog log, string instanceId = null) => true;
            public Task<bool> IsInitialised() => Task.FromResult(true);
            public Task<ActionResult> Delete(IManagedItemStore itemStore, string storageKey) { _credentials.Remove(storageKey); return Task.FromResult(new ActionResult("OK", true)); }
            public Task<List<StoredCredential>> GetCredentials(string type = null, string storageKey = null)
                => Task.FromResult(_credentials.Values.Where(c => storageKey == null || c.StorageKey == storageKey).ToList());
            public Task<StoredCredential> GetCredential(string storageKey) { _credentials.TryGetValue(storageKey, out var c); return Task.FromResult(c); }
            public Task<string> GetUnlockedCredential(string storageKey) { _credentials.TryGetValue(storageKey, out var c); return Task.FromResult(c?.Secret); }
            public Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey) => Task.FromResult<Dictionary<string, string>>(null);
            public Task<StoredCredential> Update(StoredCredential credentialInfo) { _credentials[credentialInfo.StorageKey] = credentialInfo; return Task.FromResult(credentialInfo); }
        }
    }
}
