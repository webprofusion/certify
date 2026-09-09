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

        private static ICollection<TagSummary> Tags(IActionResult result)
        {
            Assert.IsInstanceOfType<OkObjectResult>(result, result.GetType().Name);

            return (ICollection<TagSummary>)((OkObjectResult)result).Value!;
        }

        private static InternalManagedChallengeController CreateController(bool authorized)
        {
            var client = new Mock<ICertifyInternalApiClient>();

            client.Setup(c => c.CheckSecurityPrincipalHasAccess(It.IsAny<AccessCheck>(), It.IsAny<AuthContext>()))
                .ReturnsAsync(authorized);

            // the store filters on both, so the endpoint has to address the lookup the right way round to find anything
            client.Setup(c => c.GetHubItemTags(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<AuthContext>()))
                .ReturnsAsync((string itemType, string itemId, AuthContext _) => StoredTags()
                    .Where(t => t.TaggedItemType == itemType && t.TaggedItemId == itemId)
                    .Select(t => new TagSummary { CategoryKey = t.CategoryKey, Value = t.Value })
                    .ToList());

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
