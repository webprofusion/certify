using System.Net;
using System.Security.Claims;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Models.Reporting;
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
            var result = new VersionInfo { Version = versionInfo, Product = "Certify Management Hub" };
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
            var isDataStoreAvailable = false;
            var versionInfo = "Not available. Cannot connect to core service.";
            var detail = string.Empty;

            try
            {
                versionInfo = await _client.GetAppVersion();
                serviceAvailable = true;
            }
            catch { }

            if (serviceAvailable)
            {
                try
                {
                    var dataStoreStatus = await _client.GetDataStoreStatus();
                    isDataStoreAvailable = !dataStoreStatus.IsDegradedMode;

                    if (dataStoreStatus.IsDegradedMode)
                    {
                        detail = dataStoreStatus.LastErrorMessage ?? "Data store is unavailable.";
                    }
                }
                catch { }
            }

            var status = serviceAvailable && isDataStoreAvailable ? "OK" : "Degraded";

#if DEBUG
            var health = new HubHealth { Status = status, Detail = detail, Version = versionInfo, ServiceAvailable = serviceAvailable, IsDataStoreAvailable = isDataStoreAvailable, env = Environment.GetEnvironmentVariables() };
#else
            var health = new HubHealth { Status = status, Detail = detail, Version = versionInfo, ServiceAvailable = serviceAvailable, IsDataStoreAvailable = isDataStoreAvailable };
#endif

            return new OkObjectResult(health);
        }

        /// <summary>
        /// Get the current data store connection status
        /// </summary>
        /// <returns>Data store status including connection state and any error information</returns>
        [HttpGet]
        [Route("datastore/status")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DataStoreStatus))]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Type = typeof(DataStoreStatus))]
        public async Task<IActionResult> GetDataStoreStatus()
        {
            var status = await _client.GetDataStoreStatus(CurrentAuthContext);

            if (status.IsDegradedMode)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, status);
            }

            return new OkObjectResult(status);
        }

        /// <summary>
        /// Attempt to reconnect to the data store after a failure
        /// </summary>
        /// <returns>Result of the reconnection attempt</returns>
        [HttpPost]
        [Route("datastore/reconnect")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Certify.Models.Config.ActionResult))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(Certify.Models.Config.ActionResult))]
        public async Task<IActionResult> AttemptDataStoreReconnection()
        {
            var result = await _client.AttemptDataStoreReconnection(CurrentAuthContext);

            if (!result.IsSuccess)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, result);
            }

            return new OkObjectResult(result);
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

            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)HttpStatusCode.Unauthorized);
            }

            var hubAssignedInstanceId = Request.Headers["X-Certify-HubAssignedId"].ToString(); ;
            var instanceTitle = Request.Headers["X-Certify-Trace-InstanceName"].ToString();
            var isKnownInstance = false;
            string? requestAuthSecret = null;
            string? requestAuthSecretHash = null;

            // if hub assigned instance id is provided we will either check the supplied hub assigned instance id or create a new one

            // check if we know this instance, if so, check the supplied hub assigned instance ID
            if (!string.IsNullOrEmpty(hubAssignedInstanceId))
            {
                var instanceInfo = await _client.GetHubManagedInstance(hubAssignedInstanceId, CurrentAuthContext);

                if (instanceInfo == null)
                {
                    if (!Guid.TryParse(hubAssignedInstanceId, out _))
                    {
                        return Problem(detail: "Invalid hub assigned instance id format", statusCode: (int)HttpStatusCode.Unauthorized, type: "https://api.certifytheweb.com/problemtype/hub-unknown-instance-id");
                    }

                    requestAuthSecret = ManagedInstanceRequestAuth.GenerateSecret();
                    requestAuthSecretHash = ManagedInstanceRequestAuth.DeriveSecretHash(requestAuthSecret);

                    var newInstance = new ManagedInstanceInfo
                    {
                        Id = hubAssignedInstanceId,
                        InstanceId = hubAssignedInstanceId,
                        DateRegistered = DateTimeOffset.UtcNow,
                        DateLastReported = DateTimeOffset.UtcNow,
                        ConnectionStatus = ConnectionStatus.Disconnected,
                        IsAuthenticated = false,
                        Title = instanceTitle,
                        RequestAuthSecretHash = requestAuthSecretHash
                    };

                    var addResult = await _client.AddHubManagedInstance(newInstance, CurrentAuthContext);
                    if (!addResult.IsSuccess || addResult.Result == null)
                    {
                        return Problem(detail: addResult.Message ?? "Could not register presented hub assigned instance id", statusCode: (int)HttpStatusCode.BadRequest);
                    }

                    hubAssignedInstanceId = addResult.Result.InstanceId;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(instanceInfo.RequestAuthSecretHash))
                    {
                        requestAuthSecret = ManagedInstanceRequestAuth.GenerateSecret();
                        requestAuthSecretHash = ManagedInstanceRequestAuth.DeriveSecretHash(requestAuthSecret);
                        instanceInfo.RequestAuthSecretHash = requestAuthSecretHash;
                        await _client.UpdateHubManagedInstance(instanceInfo, SystemAuthContext);
                    }

                    isKnownInstance = true;
                }
            }
            else if (register == true)
            {
                // no assigned id provided, assign new one 
                requestAuthSecret = ManagedInstanceRequestAuth.GenerateSecret();
                requestAuthSecretHash = ManagedInstanceRequestAuth.DeriveSecretHash(requestAuthSecret);

                var instanceInfo = new ManagedInstanceInfo
                {
                    DateRegistered = DateTimeOffset.UtcNow,
                    DateLastReported = DateTimeOffset.UtcNow,
                    ConnectionStatus = ConnectionStatus.Disconnected,
                    IsAuthenticated = false,
                    RequestAuthSecretHash = requestAuthSecretHash
                };
                var r = await _client.AddHubManagedInstance(instanceInfo, CurrentAuthContext);
                hubAssignedInstanceId = r.Result!.InstanceId;
            }
            else
            {
                return Problem(detail: "X-Certify-HubAssignedId HTTP header missing in request", statusCode: (int)HttpStatusCode.Unauthorized);
            }

            var joiningInfo = new HubJoiningInfo();

            var versionInfo = Management.Util.GetAppVersion().ToString();

            joiningInfo.Version = new VersionInfo
            {
                Version = versionInfo,
                Product = "Certify Management Hub",
            };

            joiningInfo.HubEndpoint = "api/internal/managementhub";
            joiningInfo.IsKnownInstance = isKnownInstance;
            joiningInfo.Message = isKnownInstance ? "Joining OK. Existing instance registration reused." : "Joining OK. New instance registration created.";

            var _config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var jwtService = new Hub.Api.Services.JwtService(_config);

            var additionalClaims = new List<Claim>
                {
                    new Claim("hub-assigned-id", hubAssignedInstanceId),
                    new Claim(ClaimTypes.Name, instanceTitle??""),
                };

            joiningInfo.JoiningToken = jwtService.GenerateSecurityToken($"{Request.Headers["X-Client-ID"]}", additionalClaims: additionalClaims);
            joiningInfo.HubAssignedInstanceId = hubAssignedInstanceId!;
            joiningInfo.RequestAuthSecret = requestAuthSecret;

            return new OkObjectResult(joiningInfo);

        }
    }
}
