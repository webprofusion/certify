using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

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
        private IHubContext<InstanceManagementHub, IInstanceManagementHub> _mgmtHubContext;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="mgmtStateProvider"></param>
        /// <param name="mgmtHubContext"></param>
        public HubController(ILogger<CertificateController> logger, ICertifyInternalApiClient client, IInstanceManagementStateProvider mgmtStateProvider, IHubContext<InstanceManagementHub, IInstanceManagementHub> mgmtHubContext)
        {
            _logger = logger;
            _client = client;
            _mgmtStateProvider = mgmtStateProvider;
            _mgmtHubContext = mgmtHubContext;
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

            result.TotalResults = managedItems.Values.SelectMany(s => s.Items).Count();

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

            result.Results = list.OrderBy(l => l.Title);

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

            var managedInstances = _mgmtStateProvider.GetConnectedInstances();
            return new OkObjectResult(managedInstances);
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
            _mgmtStateProvider.Clear();
            await _mgmtHubContext.Clients.All.SendCommandRequest(new InstanceCommandRequest(ManagementHubCommands.Reconnect));
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
            var hubInfo = new HubInfo();

            var hubprefs = await _client.GetPreferences();

            hubInfo.InstanceId = hubprefs.InstanceId;

            _mgmtStateProvider.SetManagementHubInstanceId(hubInfo.InstanceId);

            var versionInfo = await _client.GetAppVersion();

            hubInfo.Version = new Models.Hub.VersionInfo { Version = versionInfo, Product = "Certify Management Hub" };

            return new OkObjectResult(hubInfo);
        }
    }
}
