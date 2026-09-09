using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
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

        /// <summary>
        /// MapHub copies the [Authorize] attributes from a hub class onto its endpoint, where they are combined with the
        /// policy the hub is mapped with, so the two have to be satisfiable together. This is what the policies actually
        /// resolve to for a connection - testing each one alone says nothing about the pair.
        /// </summary>
        private static async Task<AuthorizationPolicy> CombinedEndpointPolicy(Type hubType, string mappedPolicy)
        {
            var provider = new ServiceCollection()
                .AddLogging()
                .AddHubAuthorization()
                .BuildServiceProvider()
                .GetRequiredService<IAuthorizationPolicyProvider>();

            var authorizeData = new List<IAuthorizeData>(hubType.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>())
            {
                mappedPolicy == null ? new AuthorizeAttribute() : new AuthorizeAttribute(mappedPolicy)
            };

            return await AuthorizationPolicy.CombineAsync(provider, authorizeData);
        }

        /// <summary>
        /// A bare [Authorize] on the management hub resolves to the default policy, which refuses joining tokens. Combined
        /// with the joining policy the hub is mapped with, that made requirements no token could satisfy at once and every
        /// negotiate was a 403 - the one connection this hub exists for could not be established at all.
        /// </summary>
        [TestMethod]
        public async Task ManagementHubEndpoint_AcceptsOnlyAJoiningToken()
        {
            var authorization = CreateAuthorizationService();
            var policy = await CombinedEndpointPolicy(typeof(InstanceManagementHub), HubTokenPurposes.ManagementHubJoinPolicy);

            var joining = await authorization.AuthorizeAsync(JoiningTokenPrincipal(), resource: null, policy);
            Assert.IsTrue(joining.Succeeded, "an instance's joining token must authorize the management hub connection");

            var user = await authorization.AuthorizeAsync(UserPrincipal(), resource: null, policy);
            Assert.IsFalse(user.Succeeded, "a user's token is not a joining token");
        }

        /// <summary>
        /// The status hub carries managed certificate state for every connected instance, so it takes the default policy
        /// both on the class and where it is mapped.
        /// </summary>
        [TestMethod]
        public async Task StatusHubEndpoint_AcceptsOnlyAUserToken()
        {
            var authorization = CreateAuthorizationService();
            var policy = await CombinedEndpointPolicy(typeof(UserInterfaceStatusHub), mappedPolicy: null);

            var user = await authorization.AuthorizeAsync(UserPrincipal(), resource: null, policy);
            Assert.IsTrue(user.Succeeded, "a signed in user must be able to subscribe to status updates");

            var joining = await authorization.AuthorizeAsync(JoiningTokenPrincipal(), resource: null, policy);
            Assert.IsFalse(joining.Succeeded, "a joining token must not subscribe to the status feed");
        }
    }
}
