using System;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    /// <summary>
    /// Provides general system level information (version etc)
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class SystemController : ControllerBase
    {

        private readonly ILogger<SystemController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public SystemController(ILogger<SystemController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get the server software version
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("version")]
        public async Task<IActionResult> GetSystemVersion()
        {

            var versionInfo = await _client.GetAppVersion();

            return new OkObjectResult(versionInfo);
        }

        /// <summary>
        /// Check API is responding and can connect to background service
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("health")]
        public async Task<IActionResult> GetHealth()
        {
            var serviceAvailable = false;
            var versionInfo = "Not available. Cannot connect to service worker.";
            try
            {
                versionInfo = await _client.GetAppVersion();
                serviceAvailable = true;
            }
            catch { }

            var env = Environment.GetEnvironmentVariables();

            var health = new { API = "OK", Service = versionInfo, ServiceAvailable = serviceAvailable, env = env };

            return new OkObjectResult(health);
        }
    }
}
