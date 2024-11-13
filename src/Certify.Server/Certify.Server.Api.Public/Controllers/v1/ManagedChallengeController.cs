using Certify.Client;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Provides managed challenges such as DNS challenges on behalf of other ACME clients
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class ManagedChallengeController : ApiControllerBase
    {

        private readonly ILogger<ManagedChallengeController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public ManagedChallengeController(ILogger<ManagedChallengeController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Request a challenge response
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("request")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> PerformManagedChallenge(ManagedChallengeRequest request)
        {
            var result = await _client.PerformManagedChallenge(request, null);
            return new OkObjectResult(result);
        }

        /// <summary>
        /// Perform optional cleanup of a previously requested challenge response
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Route("cleanup")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> CleanupManagedChallenge(ManagedChallengeRequest request)
        {
            var result = await _client.CleanupManagedChallenge(request, null);
            return new OkObjectResult(result);
        }
    }
}
