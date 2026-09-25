using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Controllers;
using Certify.Server.Hub.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Certify.Core.Tests.Unit
{
    /// <summary>
    /// The managed challenge tags endpoint, driven through the controller.
    ///
    /// A tag lookup is addressed by item type and item id, and the two are both strings. Passing them the wrong way
    /// round does not fail: it asks for an item of type "the challenge id" and gets an empty list, which reads as a
    /// challenge that simply has no tags. The endpoint returned exactly that for every managed challenge. So the
    /// client here filters the way the store does rather than answering any call, and the assertions are about the
    /// tags that come back rather than about the call being made.
    /// </summary>
    [TestClass]
    public class ManagedChallengeTagsEndpointTests
    {
        private const string ChallengeId = "challenge-01";
        private const string OtherChallengeId = "challenge-02";
        private const string CallerId = "sp-caller";

        /// <summary>
        /// Tags on two managed challenges, plus one on a managed certificate which happens to share the first
        /// challenge's id - so a lookup with the arguments swapped cannot match by accident.
        /// </summary>
        private static List<ItemTag> StoredTags() =>
        [
            new(ChallengeId, TaggedItemTypes.ManagedChallenge, "environment", "Development"),
            new(ChallengeId, TaggedItemTypes.ManagedChallenge, "criticality", "High"),
            new(OtherChallengeId, TaggedItemTypes.ManagedChallenge, "environment", "Production"),
            new(ChallengeId, TaggedItemTypes.ManagedCertificate, "environment", "Staging"),
        ];

        [TestMethod]
        [Description("The endpoint returns the tags assigned to that managed challenge")]
        public async Task GetManagedChallengeTags_ReturnsTheTagsAssignedToTheChallenge()
        {
            var controller = CreateController(authorized: true);

            var tags = Tags(await controller.GetManagedChallengeTags(ChallengeId));

            CollectionAssert.AreEquivalent(
                new[] { "environment:Development", "criticality:High" },
                tags.Select(t => $"{t.CategoryKey}:{t.Value}").ToArray(),
                "the challenge's own managed challenge tags, and only those");
        }

        [TestMethod]
        [Description("Tags on another managed challenge are not returned")]
        public async Task GetManagedChallengeTags_DoesNotReturnAnotherChallengesTags()
        {
            var controller = CreateController(authorized: true);

            var tags = Tags(await controller.GetManagedChallengeTags(OtherChallengeId));

            CollectionAssert.AreEquivalent(
                new[] { "environment:Production" },
                tags.Select(t => $"{t.CategoryKey}:{t.Value}").ToArray());
        }

        [TestMethod]
        [Description("A managed challenge with no tags returns an empty list")]
        public async Task GetManagedChallengeTags_UntaggedChallenge_ReturnsEmpty()
        {
            var controller = CreateController(authorized: true);

            Assert.IsEmpty(Tags(await controller.GetManagedChallengeTags("challenge-with-no-tags")));
        }

        [TestMethod]
        [Description("A caller without the managed challenge list action is refused")]
        public async Task GetManagedChallengeTags_UnauthorizedCaller_IsForbidden()
        {
            var controller = CreateController(authorized: false);

            var result = await controller.GetManagedChallengeTags(ChallengeId);

            Assert.IsInstanceOfType<ForbidResult>(result, result.GetType().Name);
        }

        [TestMethod]
        [Description("A caller whose role is scoped to a tag lists only the challenges carrying it")]
        public async Task GetManagedChallengeSummaries_TagScopedCaller_ListsOnlyMatchingChallenges()
        {
            var controller = CreateController(authorized: true, client => ScopeTo(client, ProductionScopedRole()));

            var result = await controller.GetManagedChallengeSummaries();

            CollectionAssert.AreEquivalent(new[] { OtherChallengeId }, Summaries(result).Select(s => s.Id).ToArray());
        }

        [TestMethod]
        [Description("A role requiring all of its tags lists only the challenges carrying every one of them")]
        public async Task GetManagedChallengeSummaries_RoleRequiringAllTags_ListsOnlyChallengesCarryingAllOfThem()
        {
            var role = ProductionScopedRole();
            role.ScopedTags = [new TagScope { CategoryKey = "environment", Value = "development" }, new TagScope { CategoryKey = "criticality", Value = "low" }];
            role.RequireAllScopedTags = true;

            var controller = CreateController(authorized: true, client => ScopeTo(client, role));

            var result = await controller.GetManagedChallengeSummaries();

            Assert.IsEmpty(Summaries(result), "the development challenge carries only one of the two required tags");
        }

        [TestMethod]
        [Description("When the caller's role assignments cannot be read no challenges are listed, rather than every one")]
        public async Task GetManagedChallengeSummaries_RolesCannotBeRead_ListsNothing()
        {
            var controller = CreateController(authorized: true, client =>
                client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                    .ThrowsAsync(new System.InvalidOperationException("store unavailable")));

            var result = await controller.GetManagedChallengeSummaries();

            Assert.IsEmpty(Summaries(result));
        }

        [TestMethod]
        [Description("A tag scoped caller cannot read the tags of a challenge outside their scope")]
        public async Task GetManagedChallengeTags_OutsideCallersScope_IsNotFound()
        {
            var controller = CreateController(authorized: true, client => ScopeTo(client, ProductionScopedRole()));

            AssertNotFound(await controller.GetManagedChallengeTags(ChallengeId));
            Assert.IsInstanceOfType<OkObjectResult>(await controller.GetManagedChallengeTags(OtherChallengeId));
        }

        [TestMethod]
        [Description("A tag scoped caller cannot tag a challenge outside their scope into it")]
        public async Task AddManagedChallengeTags_OutsideCallersScope_IsNotFound()
        {
            var added = false;

            var controller = CreateController(authorized: true, client =>
            {
                ScopeTo(client, ProductionScopedRole());
                client.Setup(c => c.AddHubItemTags(It.IsAny<ICollection<ItemTag>>(), It.IsAny<AuthContext>()))
                    .Callback(() => added = true)
                    .ReturnsAsync(new Certify.Models.Config.ActionResult("OK", true));
            });

            var result = await controller.AddManagedChallengeTags(ChallengeId, [new TagScope { CategoryKey = "environment", Value = "production" }]);

            AssertNotFound(result);
            Assert.IsFalse(added, "the tags reached the store");
        }

        private static AssignedRole ProductionScopedRole() => new()
        {
            Id = "ar-consumer",
            RoleId = StandardRoles.ManagedChallengeConsumer.Id,
            SecurityPrincipalId = CallerId,
            ScopedTags = [new TagScope { CategoryKey = "environment", Value = "production" }]
        };

        private static void ScopeTo(Mock<ICertifyInternalApiClient> client, AssignedRole role)
        {
            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope { HasAccess = true, AuthorizingRoles = [role] });
        }

        private static void AssertNotFound(IActionResult result)
        {
            Assert.IsInstanceOfType<ObjectResult>(result, result.GetType().Name);
            Assert.AreEqual(StatusCodes.Status404NotFound, ((ObjectResult)result).StatusCode);
        }

        private static ICollection<ManagedChallengeSummary> Summaries(IActionResult result)
        {
            Assert.IsInstanceOfType<OkObjectResult>(result, result.GetType().Name);

            return (ICollection<ManagedChallengeSummary>)((OkObjectResult)result).Value!;
        }

        private static ICollection<TagSummary> Tags(IActionResult result)
        {
            Assert.IsInstanceOfType<OkObjectResult>(result, result.GetType().Name);

            return (ICollection<TagSummary>)((OkObjectResult)result).Value!;
        }

        private static InternalManagedChallengeController CreateController(bool authorized, System.Action<Mock<ICertifyInternalApiClient>>? configure = null)
        {
            var client = new Mock<ICertifyInternalApiClient>();

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(authorized);

            // unless a test scopes the caller, their role grants the action without restriction
            client.Setup(c => c.EvaluateAccessScope(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(new ResourceAccessScope
                {
                    HasAccess = authorized,
                    AuthorizingRoles = authorized ? [new AssignedRole { Id = "ar-admin", RoleId = StandardRoles.ManagedChallengeAdmin.Id, SecurityPrincipalId = CallerId }] : []
                });

            // the store filters on both, so the endpoint has to address the lookup the right way round to find anything
            client.Setup(c => c.GetHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((string itemType, string itemId, AuthContext _) => StoredTags()
                    .Where(t => t.TaggedItemType == itemType && t.TaggedItemId == itemId)
                    .Select(t => new TagSummary { CategoryKey = t.CategoryKey, Value = t.Value })
                    .ToList());

            client.Setup(c => c.GetAllHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((string _, string _, string itemType, string _, AuthContext _) => StoredTags()
                    .Where(t => t.TaggedItemType == itemType)
                    .ToList());

            client.Setup(c => c.GetManagedChallenges(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<ManagedChallenge>
                {
                    new() { Id = ChallengeId, Title = "Development" },
                    new() { Id = OtherChallengeId, Title = "Production" }
                });

            client.Setup(c => c.GetTagCategories(It.IsAny<AuthContext>()))
                .ReturnsAsync(new List<TagCategory>());

            configure?.Invoke(client);

            return new InternalManagedChallengeController(NullLogger<InternalManagedChallengeController>.Instance, client.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = CreateContext() }
            };
        }

        private static DefaultHttpContext CreateContext()
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Sid, CallerId)],
                    ApiKeyAuthenticationDefaults.AuthenticationScheme))
            };

            context.Request.Method = HttpMethods.Get;
            context.Request.Path = $"/api/internal/v1/managedchallenges/{ChallengeId}/tags";

            var services = new ServiceCollection();
            services.AddProblemDetailsFactory();
            context.RequestServices = services.BuildServiceProvider();

            return context;
        }
    }
}
