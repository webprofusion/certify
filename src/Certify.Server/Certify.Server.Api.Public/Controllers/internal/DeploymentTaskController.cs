using Certify.Client;
using Certify.Server.Api.Public.Services;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Internal API for extended certificate management. Not intended for general use.
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public partial class DeploymentTaskController : ApiControllerBase
    {

        private readonly ILogger<DeploymentTaskController> _logger;

        private readonly ICertifyInternalApiClient _client;
        private readonly ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public DeploymentTaskController(ILogger<DeploymentTaskController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _client = client;
            _mgmtAPI = mgmtAPI;
        }
    }
}
