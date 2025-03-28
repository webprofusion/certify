using System.Net;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Provides general system level information (version etc)
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class SystemController : ApiControllerBase
    {

        private readonly ILogger<SystemController> _logger;

        private readonly ICertifyInternalApiClient _client;

        private ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="mgmtApi"></param>
        public SystemController(ILogger<SystemController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtApi)
        {
            _logger = logger;
            _client = client;
            _mgmtAPI = mgmtApi;

        }

        /// <summary>
        /// Get the server software version
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("version")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(VersionInfo))]
        public async Task<IActionResult> GetSystemVersion()
        {
            var versionInfo = await _client.GetAppVersion();
            var result = new Models.Hub.VersionInfo { Version = versionInfo, Product = "Certify Management Hub" };
            return new OkObjectResult(result);
        }

        /// <summary>
        /// Check API is configured, responding and can connect to background service
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("health")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubHealth))]
        public async Task<IActionResult> GetHealth()
        {
            var serviceAvailable = false;
            var versionInfo = "Not available. Cannot connect to core service.";
            try
            {
                versionInfo = await _client.GetAppVersion();
                serviceAvailable = true;
            }
            catch { }

#if DEBUG
            var health = new HubHealth { Status = "OK", Version = versionInfo, ServiceAvailable = serviceAvailable, env = Environment.GetEnvironmentVariables() };
#else
            var health = new HubHealth { Status = "OK", Version = versionInfo, ServiceAvailable = serviceAvailable };
#endif

            return new OkObjectResult(health);
        }

        /// <summary>
        /// Checks if a client can join a hub based on provided credentials and parameters.
        /// </summary>
        /// <returns>Returns an IActionResult indicating the success or failure of the access check.</returns>
        [HttpGet]
        [Route("/api/v1/hub/joincheck")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubInfo))]
        public async Task<IActionResult> CheckJoining()
        {

            // auth based on client id and client secret
            // check token and access control before allowing download
            var clientId = Request.Headers["X-Client-ID"];
            var secret = Request.Headers["X-Client-Secret"];

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            {
                return Problem(detail: "X-Client-ID or X-Client-Secret HTTP header missing in request", statusCode: (int)HttpStatusCode.Unauthorized);
            }

            var accessPermittedResult = await IsAccessTokenAuthorized(_client, new AccessToken { ClientId = clientId, Secret = secret }, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));

            if (accessPermittedResult.IsSuccess)
            {
                var hubInfo = new HubInfo();

                var hubprefs = await _client.GetPreferences();

                hubInfo.InstanceId = hubprefs.InstanceId;

                var versionInfo = await _client.GetAppVersion();

                hubInfo.Version = new Models.Hub.VersionInfo
                {
                    Version = versionInfo,
                    Product = "Certify Management Hub",
                };

                hubInfo.HubEndpoint = "api/internal/managementhub";
                hubInfo.Message = "Joining OK";

                var _config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var jwtService = new Hub.Api.Services.JwtService(_config);

                hubInfo.JoiningToken = jwtService.GenerateSecurityToken($"clientId:{clientId}");

                return new OkObjectResult(hubInfo);
            }
            else
            {
                return Problem(detail: accessPermittedResult.Message, statusCode: (int)HttpStatusCode.Unauthorized);
            }
        }
    }
}
