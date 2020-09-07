using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class SystemController : ControllerBase
    {

        private readonly ILogger<SystemController> _logger;

        private readonly ICertifyInternalApiClient _client;

        public SystemController(ILogger<SystemController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        [HttpGet]
        [Route("version")]
        public async Task<IActionResult> Get()
        {
            var versionInfo= await _client.GetAppVersion();

            return new OkObjectResult(versionInfo);
        }
    }
}
