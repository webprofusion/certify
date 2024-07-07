
using System.Collections.Frozen;
using Certify.API.Management;
using Certify.Client;
using Certify.Models.API;
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
    [Route("api/v1/[controller]")]
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
        public async Task<IActionResult> GetHubManagedItems(string? keyword, int? page = null, int? pageSize = null)
        {

            var result = new ManagedCertificateSummaryResult();

            var managedItems = _mgmtStateProvider.GetManagedInstanceItems();
            var instances = _mgmtStateProvider.GetConnectedInstances();

            result.TotalResults = managedItems.Values.SelectMany(s => s.Items).Count();

            var list = new List<ManagedCertificateSummary>();
            foreach (var remote in managedItems.Values)
            {
                list.AddRange(remote.Items.Select(i => new ManagedCertificateSummary
                {
                    InstanceId = remote.InstanceId,
                    InstanceTitle = instances.FirstOrDefault(i => i.InstanceId == remote.InstanceId)?.Title,
                    Id = i.Id ?? "",
                    Title = $"[remote] {i.Name}" ?? "",
                    PrimaryIdentifier = i.GetCertificateIdentifiers().FirstOrDefault(p => p.Value == i.RequestConfig.PrimaryDomain) ?? i.GetCertificateIdentifiers().FirstOrDefault(),
                    Identifiers = i.GetCertificateIdentifiers(),
                    DateRenewed = i.DateRenewed,
                    DateExpiry = i.DateExpiry,
                    Comments = i.Comments ?? "",
                    Status = i.LastRenewalStatus?.ToString() ?? "",
                    HasCertificate = !string.IsNullOrEmpty(i.CertificatePath)
                }));
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
            var managedInstances = _mgmtStateProvider.GetConnectedInstances();
            return new OkObjectResult(managedInstances);
        }

        [HttpGet]
        [Route("flush")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedInstanceInfo>))]
        public async Task<IActionResult> FlushHubManagedInstances()
        {
            _mgmtStateProvider.Clear();
            await _mgmtHubContext.Clients.All.SendCommandRequest(new InstanceCommandRequest(ManagementHubCommands.Reconnect));
            return new OkResult();
        }
    }
}
