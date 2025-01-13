using Certify.Client;
using Certify.Models.Hub;
using Certify.Server.Api.Public.SignalR.ManagementHub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Certify.Server.Api.Public.Controllers
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
        /// <param name="keyword"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("items")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificateSummaryResult))]
        public async Task<IActionResult> GetHubManagedItems(string? instanceId, string? keyword, int? page = null, int? pageSize = null)
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
                        .Where(i => string.IsNullOrWhiteSpace(keyword) || (!string.IsNullOrWhiteSpace(keyword) && i.Name?.Contains(keyword) == true))
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
                                Status = i.LastRenewalStatus?.ToString() ?? "",
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

        [HttpGet]
        [Route("instances")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedInstanceInfo>))]
        public async Task<IActionResult> GetHubManagedInstances()
        {
            if (!await IsAuthorized(_client, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstancesList))
            {
                return Unauthorized();
            }

            var managedInstances = _mgmtStateProvider.GetConnectedInstances();
            return new OkObjectResult(managedInstances);
        }

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

        [HttpGet]
        [Route("info")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HubInfo))]
        public async Task<IActionResult> GetHubInfo()
        {
            var hubInfo = new HubInfo();

            var hubprefs = await _client.GetPreferences();

            hubInfo.InstanceId = hubprefs.InstanceId;

            var versionInfo = await _client.GetAppVersion();

            hubInfo.Version = new Models.Hub.VersionInfo { Version = versionInfo, Product = "Certify Management Hub" };

            return new OkObjectResult(hubInfo);
        }
    }
}
