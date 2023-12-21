using Certify.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Provides operations related to identifier validation challenges (proof of domain control etc)
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class ValidationController : ControllerBase
    {

        private readonly ILogger<ValidationController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public ValidationController(ILogger<ValidationController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// get current challenge info for a given type/key
        /// </summary>
        /// <param name="type"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("{type}/{key?}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.SimpleAuthorizationChallengeItem>))]
        public async Task<IActionResult> GetValidationChallenges(string type, string key)
        {
            if (type == null)
            {
                type = "http-01";
            }

            var list = await _client.GetCurrentChallenges(type, key);
            return new OkObjectResult(list);
        }
    }
}
