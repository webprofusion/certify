using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Server.Api.Public.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthController : ControllerBase
    {

        private readonly ILogger<AuthController> _logger;

        private readonly ICertifyInternalApiClient _client;
        private IConfiguration _config;
        public AuthController(ILogger<AuthController> logger, ICertifyInternalApiClient client, IConfiguration config)
        {
            _logger = logger;
            _client = client;
            _config = config;
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet]
        [Route("status")]
        public async Task<IActionResult> Get()
        {
            return await Task.FromResult(new OkResult());
        }

        [HttpPost]
        [Route("login")]
        public string Login(LoginModel login)
        {
            // TODO: check users login, if valid issue new JWT token based on their user id

            var jwt = new Api.Public.Services.JwtService(_config);
            var token = jwt.GenerateSecurityToken(login.Username);
            return token;
        }
    }
}
