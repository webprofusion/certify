using System.Net;
using System.Security.Claims;
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
        /// Attempt to register as a new instance with the management hub
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/api/v1/hub/register")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubJoiningInfo))]

        public async Task<IActionResult> Register()
        {
            return await CheckJoining(register: true);
        }

        /// <summary>
        /// Checks if a client can join a hub based on provided credentials and parameters.
        /// </summary>
        /// <returns>Returns an IActionResult indicating the success or failure of the access check.</returns>
        [HttpGet]
        [Route("/api/v1/hub/joincheck/")]

        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubJoiningInfo))]
        public async Task<IActionResult> CheckJoining(bool? register = false)
        {

            // auth based on client id and client secret
            // check token and access control before allowing download
            var clientId = Request.Headers["X-Client-ID"];
            var secret = Request.Headers["X-Client-Secret"];
            var hubAssignedInstanceId = Request.Headers["X-Certify-HubAssignedId"];

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            {
                return Problem(detail: "X-Client-ID or X-Client-Secret HTTP header missing in request", statusCode: (int)HttpStatusCode.Unauthorized);
            }

            // if hub assigned instance id is provided we will either check the supplied hub assigned instance id or create a new one

            // check if we know this instance, if so, check the supplied hub assigned instance ID
            if (!string.IsNullOrEmpty(hubAssignedInstanceId))
            {
                var instanceInfo = await _client.GetHubManagedInstance(hubAssignedInstanceId, CurrentAuthContext);

                if (instanceInfo == null)
                {
                    return Problem(detail: "Invalid or unknown hub assigned instance id", statusCode: (int)HttpStatusCode.Unauthorized);
                }
            }
            else if (register == true)
            {
                // no assigned id provided, assign new one 
                var instanceInfo = new ManagedInstanceInfo
                {
                    DateRegistered = DateTime.UtcNow,
                    DateLastReported = DateTime.UtcNow,
                    ConnectionStatus = ConnectionStatus.Disconnected,
                    IsAuthenticated = false,
                };
                var r = await _client.AddHubManagedInstance(instanceInfo, CurrentAuthContext);
                hubAssignedInstanceId = r.Result!.InstanceId;
            }
            else
            {
                return Problem(detail: "X-Certify-HubAssignedId HTTP header missing in request", statusCode: (int)HttpStatusCode.Unauthorized);
            }

            var accessPermittedResult = await IsAccessTokenAuthorized(_client, new AccessToken { ClientId = clientId, Secret = secret }, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));

            if (accessPermittedResult.IsSuccess)
            {
                var joiningInfo = new HubJoiningInfo();

                var versionInfo = await _client.GetAppVersion();

                joiningInfo.Version = new Models.Hub.VersionInfo
                {
                    Version = versionInfo,
                    Product = "Certify Management Hub",
                };

                joiningInfo.HubEndpoint = "api/internal/managementhub";
                joiningInfo.Message = "Joining OK";

                var _config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var jwtService = new Hub.Api.Services.JwtService(_config);

                var additionalClaims = new List<Claim>
                {
                    new Claim("hub-assigned-id", Guid.NewGuid().ToString())
                };

                joiningInfo.JoiningToken = jwtService.GenerateSecurityToken($"{clientId}");
                joiningInfo.HubAssignedInstanceId = hubAssignedInstanceId!;

                return new OkObjectResult(joiningInfo);
            }
            else
            {
                return Problem(detail: accessPermittedResult.Message, statusCode: (int)HttpStatusCode.Unauthorized);
            }
        }
    }
}
