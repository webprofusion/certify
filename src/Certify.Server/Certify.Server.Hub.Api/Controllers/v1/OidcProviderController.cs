using Certify.Client;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Administer Oidc providers
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="logger"></param>
    /// <param name="client"></param>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class OidcProviderController(ILogger<OidcProviderController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI) : ApiControllerBase
    {
        private readonly ILogger<OidcProviderController> _logger = logger;
        private readonly ICertifyInternalApiClient _client = client;
        private readonly ManagementAPI _mgmtAPI = mgmtAPI;

        [HttpGet]
        [Route("providers")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(Dictionary<string, string>), 200)]
        public async Task<IActionResult> GetSupportedProviders()
        {
            var list = await _client.GetOidcProviders(CurrentAuthContext);
            var result = list.ToDictionary(k => k.Id, v => v.Title);
            return Ok(result);
        }
    }
}
