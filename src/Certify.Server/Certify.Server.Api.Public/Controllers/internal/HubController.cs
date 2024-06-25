
using Certify.API.Management;
using Certify.Client;
using Certify.Models.API;
using Certify.Server.Api.Public.SignalR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public HubController(ILogger<CertificateController> logger, ICertifyInternalApiClient client, IInstanceManagementStateProvider mgmtStateProvider)
        {
            _logger = logger;
            _client = client;
            _mgmtStateProvider = mgmtStateProvider;
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

            var remoteItems = _mgmtStateProvider.GetManagedInstanceItems();

            result.TotalResults = remoteItems.Values.SelectMany(s => s.Items).Count();

            var list = new List<ManagedCertificateSummary>();
            foreach (var remote in remoteItems.Values)
            {
                list.AddRange(remoteItems.Values.SelectMany(s => s.Items).Select(i => new ManagedCertificateSummary
                {
                    InstanceId = remote.InstanceId,
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
    }
}
