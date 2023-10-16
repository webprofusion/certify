using Certify.Client;
using Certify.Server.API.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Certify.Models.Config.AccessControl;

namespace Certify.Server.Api.Public.Controllers
{
    /// <summary>
    /// Internal API controller for access related admin
    /// </summary>
    [Route("internal/v1/[controller]")]
    [ApiController]
    
    public class AccessController : ControllerBase
    {
        private readonly ILogger<CertificateAuthorityController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public AccessController(ILogger<CertificateAuthorityController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get list of Security Principles
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SecurityPrinciple>))]
        public async Task<IActionResult> GetSecurityPrinciples()
        {
            var list = await _client.GetAccessSecurityPrinciples();
            return new OkObjectResult(list);
        }
    }
}
