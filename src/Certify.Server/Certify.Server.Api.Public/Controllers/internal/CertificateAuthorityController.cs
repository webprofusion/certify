using Certify.Client;
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
    public partial class CertificateAuthorityController : ApiControllerBase
    {

        private readonly ILogger<CertificateAuthorityController> _logger;

        private readonly ICertifyInternalApiClient _client;
        private readonly ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public CertificateAuthorityController(ILogger<CertificateAuthorityController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtApi)
        {
            _logger = logger;
            _client = client;
            _mgmtAPI = mgmtApi;
        }

        /// <summary>
        /// Get list of known certificate authorities
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.CertificateAuthority>))]
        public async Task<IActionResult> GetCertificateAuthorities()
        {
            var list = await _client.GetCertificateAuthorities();
            return new OkObjectResult(list);
        }
    }
}
