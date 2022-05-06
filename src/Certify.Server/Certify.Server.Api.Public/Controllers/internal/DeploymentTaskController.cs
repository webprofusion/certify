using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    /// <summary>
    /// Internal API for extended certificate management. Not intended for general use.
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public class DeploymentTaskController : ControllerBase
    {

        private readonly ILogger<DeploymentTaskController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public DeploymentTaskController(ILogger<DeploymentTaskController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get List of supported deployment tasks
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]

        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.Config.DeploymentProviderDefinition>))]
        [Route("providers")]
        public async Task<IActionResult> GetDeploymentProviders()
        {
            var list = await _client.GetDeploymentProviderList();
            return new OkObjectResult(list);
        }
    }
}
