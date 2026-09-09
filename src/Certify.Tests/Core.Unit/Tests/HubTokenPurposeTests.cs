using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// A joining token and a signed in user's token are both bearer tokens signed with the same key, so nothing but
    /// the purpose claim distinguishes them. Before it existed, a managed instance's joining token was accepted
    /// anywhere a user's token was - including the user interface status hub, which carries managed certificate
    /// state for every connected instance.
    /// </summary>
    [TestClass]
    public class HubTokenPurposeTests
    {
        private static IAuthorizationService CreateAuthorizationService()
        {
            return new ServiceCollection()
                .AddLogging()
                .AddHubAuthorization()
                .BuildServiceProvider()
                .GetRequiredService<IAuthorizationService>();
        }

        private static AuthorizationPolicy GetPolicy(string name = null)
        {
            var provider = new ServiceCollection()
                .AddLogging()
                .AddHubAuthorization()
                .BuildServiceProvider()
                .GetRequiredService<IAuthorizationPolicyProvider>();

            return name == null
                ? provider.GetDefaultPolicyAsync().Result
                : provider.GetPolicyAsync(name).Result;
        }

        private static ClaimsPrincipal UserPrincipal(string principalId = "sp-user")
        {
            return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Sid, principalId)], "Bearer"));
        }

        private static ClaimsPrincipal JoiningTokenPrincipal(string principalId = "managedinstance_sp_01")
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Sid, principalId),
                    new Claim("hub-assigned-id", "instance-1"),
                    new Claim(HubTokenPurposes.ClaimType, HubTokenPurposes.ManagementHubJoin)
                ],
                "Bearer"));
        }

        [TestMethod]
        public void IsManagementHubJoinToken_RecognisesOnlyAJoiningToken()
        {
            Assert.IsTrue(HubTokenPurposes.IsManagementHubJoinToken(JoiningTokenPrincipal()));
            Assert.IsFalse(HubTokenPurposes.IsManagementHubJoinToken(UserPrincipal()));
            Assert.IsFalse(HubTokenPurposes.IsManagementHubJoinToken(null));
        }

        /// <summary>
        /// The default policy is what every [AuthorizedApi] endpoint and RequireAuthorization() call resolves to, so
        /// excluding joining tokens there covers the whole API and the status hub at once.
        /// </summary>
        [TestMethod]
        public async Task DefaultPolicy_RefusesAJoiningTokenAndAcceptsAUserToken()
        {
            var authorization = CreateAuthorizationService();
            var policy = GetPolicy();

            var user = await authorization.AuthorizeAsync(UserPrincipal(), resource: null, policy);
            Assert.IsTrue(user.Succeeded, "a signed in user's token must still authorize");

            var joining = await authorization.AuthorizeAsync(JoiningTokenPrincipal(), resource: null, policy);
            Assert.IsFalse(joining.Succeeded, "a joining token must not authorize an ordinary authenticated endpoint");
        }

        /// <summary>
        /// The management hub connection is the one place a joining token is what is expected, so it opts back in.
        /// </summary>
        [TestMethod]
        public async Task ManagementHubJoinPolicy_AcceptsOnlyAJoiningToken()
        {
            var authorization = CreateAuthorizationService();
            var policy = GetPolicy(HubTokenPurposes.ManagementHubJoinPolicy);

            Assert.IsNotNull(policy, "the management hub joining policy must be registered");

            var joining = await authorization.AuthorizeAsync(JoiningTokenPrincipal(), resource: null, policy);
            Assert.IsTrue(joining.Succeeded, "an instance must be able to open its management hub connection");

            var user = await authorization.AuthorizeAsync(UserPrincipal(), resource: null, policy);
            Assert.IsFalse(user.Succeeded, "a user's token is not a joining token");
        }

        /// <summary>
        /// An unauthenticated caller is refused by both, so neither policy is doing the authentication check alone.
        /// </summary>
        [TestMethod]
        public async Task BothPolicies_RefuseAnUnauthenticatedCaller()
        {
            var authorization = CreateAuthorizationService();
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

            Assert.IsFalse((await authorization.AuthorizeAsync(anonymous, resource: null, GetPolicy())).Succeeded);
            Assert.IsFalse((await authorization.AuthorizeAsync(anonymous, resource: null, GetPolicy(HubTokenPurposes.ManagementHubJoinPolicy))).Succeeded);
        }
    }
}
