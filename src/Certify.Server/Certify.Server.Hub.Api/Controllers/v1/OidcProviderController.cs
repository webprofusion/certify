using Certify.Client;
using Certify.Models.Hub;
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
    [Route("internal/v1/[controller]")]
    public partial class OidcProviderController(ILogger<OidcProviderController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI, IConfiguration config) : ApiControllerBase
    {
        private readonly ILogger<OidcProviderController> _logger = logger;
        private readonly ICertifyInternalApiClient _client = client;
        private readonly ManagementAPI _mgmtAPI = mgmtAPI;
        private readonly IConfiguration _config = config;

        /// <summary>
        /// Get the sign in options this hub offers, so that an unauthenticated client only presents the methods
        /// which will actually be accepted.
        /// </summary>
        [HttpGet]
        [Route("providers")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(AuthProviderInfo), 200)]
        public async Task<IActionResult> GetSupportedProviders()
        {
            var list = await _client.GetOidcProviders(CurrentAuthContext);

            var result = new AuthProviderInfo
            {
                OidcProviders = list.ToDictionary(k => k.Id, v => v.Title),
                IsPasswordLoginEnabled = AuthSettings.IsPasswordLoginEnabled(_config)
            };

            return Ok(result);
        }
    }
}
