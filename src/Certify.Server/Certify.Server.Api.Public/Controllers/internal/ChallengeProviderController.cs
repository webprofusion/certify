using Certify.Client;
using Certify.Models.Providers;
using Certify.Server.Api.Public.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Internal API for extended certificate management. Not intended for general use.
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public partial class ChallengeProviderController : ApiControllerBase
    {

        private readonly ILogger<ChallengeProviderController> _logger;

        private readonly ICertifyInternalApiClient _client;
        private readonly ManagementAPI _mgmtAPI;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public ChallengeProviderController(ILogger<ChallengeProviderController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _client = client;
            _mgmtAPI = mgmtAPI;
        }

        /// <summary>
        /// Get list of supported challenge providers
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.Config.ChallengeProviderDefinition>))]

        public async Task<IActionResult> GetChallengeProviders()
        {
            var list = await _client.GetChallengeAPIList();
            return new OkObjectResult(list);
        }
    }
}
