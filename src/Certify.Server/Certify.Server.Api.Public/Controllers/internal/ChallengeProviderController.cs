using Certify.Client;
using Certify.Models.Providers;
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
    public partial class ChallengeProviderController : ControllerBase
    {

        private readonly ILogger<ChallengeProviderController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public ChallengeProviderController(ILogger<ChallengeProviderController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get list of supported challenge providers
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.Config.ChallengeProviderDefinition>))]

        public async Task<IActionResult> GetChallengeProviders()
        {
            var list = await _client.GetChallengeAPIList();
            return new OkObjectResult(list);
        }

        /// <summary>
        /// Fetch list of DNS zones for a given DNS provider and credential
        /// </summary>
        /// <param name="providerTypeId"></param>
        /// <param name="credentialsId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("dnszones")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DnsZone>))]
        public async Task<List<DnsZone>> GetDnsZones(string providerTypeId, string credentialsId)
        {
            return await _client.GetDnsProviderZones(providerTypeId, credentialsId);
        }
    }
}
