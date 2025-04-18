using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Provides managed certificate related operations
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public partial class HubController : ApiControllerBase
    {

        private readonly ILogger<CertificateController> _logger;

        private readonly ICertifyInternalApiClient _client;

        private IInstanceManagementStateProvider _mgmtStateProvider;
        private ManagementAPI _mgmtAPI;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="mgmtStateProvider"></param>
        /// <param name="mgmtAPI"></param>
        public HubController(ILogger<CertificateController> logger, ICertifyInternalApiClient client, IInstanceManagementStateProvider mgmtStateProvider, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _client = client;
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtAPI = mgmtAPI;
        }

        /// <summary>
        /// Get all managed certificates matching criteria
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="keyword"></param>
        /// <param name="health"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("items")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificateSummaryResult))]
        public async Task<IActionResult> GetHubManagedItems(string? instanceId, string? keyword, Models.ManagedCertificateHealth? health = null, int? page = null, int? pageSize = null)
        {
            var result = new ManagedCertificateSummaryResult();

            var managedItems = _mgmtStateProvider.GetManagedInstanceItems();
            var instances = _mgmtStateProvider.GetConnectedInstances();

            var list = new List<ManagedCertificateSummary>();

            foreach (var remote in managedItems.Values)
            {
                if (string.IsNullOrEmpty(instanceId) || (instanceId == remote.InstanceId))
                {
                    list.AddRange(
                        remote.Items
                        .Where(i => string.IsNullOrWhiteSpace(keyword) || (!string.IsNullOrWhiteSpace(keyword) && i.Name?.Contains(keyword, StringComparison.InvariantCultureIgnoreCase) == true))
                        .Where(i => health == null || (health != null && i.Health == health))
                        .Select(i =>
                        {
                            var instance = instances.FirstOrDefault(i => i.InstanceId == remote.InstanceId);

                            return new ManagedCertificateSummary
                            {
                                InstanceId = remote.InstanceId,
                                InstanceTitle = instance?.Title,
                                Id = i.Id ?? "",
                                Title = $"{i.Name}" ?? "",
                                OS = instance?.OS,
                                ClientDetails = instance?.ClientName,
                                PrimaryIdentifier = i.GetCertificateIdentifiers().FirstOrDefault(p => p.Value == i.RequestConfig.PrimaryDomain) ?? i.GetCertificateIdentifiers().FirstOrDefault(),
                                Identifiers = i.GetCertificateIdentifiers(),
                                DateRenewed = i.DateRenewed,
                                DateExpiry = i.DateExpiry,
                                Comments = i.Comments ?? "",
                                Status = i.Health.ToString(),
                                DateRetrieved = i.DateRetrieved,
                                HasCertificate = !string.IsNullOrEmpty(i.CertificatePath)
                            };
                        }
                        )
                    );
                }
            }

            result.TotalResults = list.Count;

            var skip = 0;

            if (page != null && page > 0)
            {
                skip = (int)page * (pageSize ?? 100);
            }

            result.Results = list.OrderBy(l => l.Title).Skip(skip).Take(pageSize ?? 100);

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Get all hub managed instances
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("instances")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedInstanceInfo>))]
        public async Task<IActionResult> GetHubManagedInstances()
        {
            if (!await IsAuthorized(_client, new AccessCheck(CurrentAuthContext?.UserId, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList)))
            {
                return Unauthorized();
            }

            var managedInstances = await _client.GetHubManagedInstances(CurrentAuthContext);

            var connectedInstances = _mgmtStateProvider.GetConnectedInstances();
            foreach (var i in managedInstances)
            {
                var connected = connectedInstances.FirstOrDefault(c => c.InstanceId == i.InstanceId);
                if (connected != null)
                {
                    i.DateLastReported = connected.DateLastReported;
                    i.ConnectionStatus = connected.ConnectionStatus;

                    i.IsAuthenticated = true;

                    // copy db values to in memory connect instance representation
                    connected.DateRegistered = i.DateRegistered;
                    connected.Tags = i.Tags;

                }
            }

            return new OkObjectResult(managedInstances.Where(c => c.IsAuthenticated).OrderBy(o => o.Title));
        }

        /// <summary>
        /// Flush all hub managed instances
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("flush")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> FlushHubManagedInstances()
        {
            _mgmtAPI.ReconnectInstances();

            return new OkResult();
        }

        /// <summary>
        /// Get info about the hub instance
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("info")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubInfo))]
        public async Task<IActionResult> GetHubInfo()
        {
            // see also SystemController.CheckJoining which has similar/same info
            var hubInfo = await _client.GetHubInfo();
            return new OkObjectResult(hubInfo);
        }

        /// <summary>
        /// Retrieves the current system status items 
        /// </summary>
        /// <returns>Returns an OK response containing a list of ActionStep objects.</returns>
        [HttpGet]
        [Route("status/{instanceId?}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ActionStep>))]
        public async Task<IActionResult> GetSystemStatusItems(string? instanceId = null)
        {

            var status = _mgmtStateProvider.GetSystemStatusItems();

            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                var instanceStatus = await _mgmtAPI.GetInstanceStatusItems(instanceId, CurrentAuthContext);

                if (instanceStatus?.Count > 0)
                {
                    status.AddRange(instanceStatus);
                }
            }

            return new OkObjectResult(status);
        }
    }
}
