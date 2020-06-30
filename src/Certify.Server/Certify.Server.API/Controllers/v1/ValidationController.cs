using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Server.Api.Public.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class ValidationController : ControllerBase
    {

        private readonly ILogger<ValidationController> _logger;

        private readonly ICertifyInternalApiClient _client;

        public ValidationController(ILogger<ValidationController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        [HttpGet]
        [Route("{type}/{key?}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Get(string type, string key)
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
