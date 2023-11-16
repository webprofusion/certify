using Certify.Client;
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
    public partial class StoredCredentialController : ControllerBase
    {

        private readonly ILogger<StoredCredentialController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public StoredCredentialController(ILogger<StoredCredentialController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get List of stored credentials
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]

        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.Config.StoredCredential>))]

        public async Task<IActionResult> GetStoredCredentials()
        {
            var list = await _client.GetCredentials();
            return new OkObjectResult(list);
        }

        /// <summary>
        /// Add/Update a stored credential
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.Config.StoredCredential))]
        public async Task<IActionResult> UpdateStoredCredential(Models.Config.StoredCredential credential)
        {
            var update = await _client.UpdateCredentials(credential);
            if (update != null)
            {
                return new OkObjectResult(update);
            }
            else
            {
                return new BadRequestResult();
            }
        }
    }
}
