using Certify.Client;
using Certify.Models.API;
using Certify.Models.Reporting;
using Certify.Server.Api.Public.Services;
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
    public partial class CertificateController : ApiControllerBase
    {

        private readonly ILogger<CertificateController> _logger;

        private readonly ICertifyInternalApiClient _client;

        private ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public CertificateController(ILogger<CertificateController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtApi)
        {
            _logger = logger;
            _client = client;

            _mgmtAPI = mgmtApi;
        }

        /// <summary>
        /// Download the latest certificate for the given managed certificate
        /// </summary>
        /// <param name="managedCertId"></param>
        /// <param name="format"></param>
        /// <param name="mode"></param>
        /// <returns>The certificate file in the chosen format</returns>
        [HttpGet]
        [Route("{managedCertId}/download/{format?}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]

        [ProducesResponseType(typeof(FileContentResult), 200)]
        public async Task<IActionResult> Download(string managedCertId, string format, string? mode = null)
        {
            if (format == null)
            {
                format = "pfx";
            }

            if (mode == null)
            {
                mode = "fullchain";
            }

            // TODO: certify manager to do all the cert conversion work, server may be on another machine
            var managedCert = await _client.GetManagedCertificate(managedCertId, CurrentAuthContext);

            if (managedCert?.CertificatePath == null)
            {
                return new NotFoundResult();
            }

            var content = await System.IO.File.ReadAllBytesAsync(managedCert.CertificatePath);

            return new FileContentResult(content, "application/x-pkcs12") { FileDownloadName = "certificate.pfx" };

        }

        /// <summary>
        /// Download text log for the given managed certificate
        /// </summary>
        /// <param name="managedCertId"></param>
        /// <param name="maxLines"></param>
        /// <returns>Log file in text format</returns>
        [HttpGet]
        [Route("{managedCertId}/log")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LogResult))]
        public async Task<IActionResult> DownloadLog(string instanceId, string managedCertId, int maxLines = 1000)
        {

            if (maxLines > 1000)
            {
                maxLines = 1000;
            }

            LogItem[] log = await _mgmtAPI.GetItemLog(instanceId, managedCertId, maxLines, CurrentAuthContext);

            return new OkObjectResult(new LogResult { Items = log });
        }

        /// <summary>
        /// Get all managed certificates matching criteria
        /// </summary>
        /// <param name="keyword"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificateSummaryResult))]
        [Route("search")]
        public async Task<IActionResult> GetManagedCertificates(string? keyword, int? page = null, int? pageSize = null)
        {
            var managedCertResult = await _client.GetManagedCertificateSearchResult(
                new Models.ManagedCertificateFilter
                {
                    Keyword = keyword,
                    PageIndex = page,
                    PageSize = pageSize
                }, CurrentAuthContext);

            var list = managedCertResult.Results.Select(i => new ManagedCertificateSummary
            {
                InstanceId = i.InstanceId,
                Id = i.Id ?? "",
                Title = i.Name ?? "",
                PrimaryIdentifier = i.GetCertificateIdentifiers().FirstOrDefault(p => p.Value == i.RequestConfig.PrimaryDomain) ?? i.GetCertificateIdentifiers().FirstOrDefault(),
                Identifiers = i.GetCertificateIdentifiers(),
                DateRenewed = i.DateRenewed,
                DateExpiry = i.DateExpiry,
                Comments = i.Comments ?? "",
                Status = i.LastRenewalStatus?.ToString() ?? "",
                HasCertificate = !string.IsNullOrEmpty(i.CertificatePath)
            }).OrderBy(a => a.Title);

            var result = new ManagedCertificateSummaryResult
            {
                Results = list,
                TotalResults = managedCertResult.TotalResults,
                PageIndex = page ?? 0,
                PageSize = pageSize ?? list.Count()
            };

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Get summary counts of all managed certs
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("summary")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusSummary))]
        public async Task<IActionResult> GetManagedCertificateSummary()
        {
            var summary = await _mgmtAPI.GetManagedCertificateSummary(CurrentAuthContext);
            return new OkObjectResult(summary);
        }

        /// <summary>
        /// Gets the full settings for a specific managed certificate
        /// </summary>
        /// <param name="instanceId">target instance</param>
        /// <param name="managedCertId">managed item</param>
        /// <returns></returns>
        [HttpGet]
        [Route("settings/{instanceId}/{managedCertId}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        public async Task<IActionResult> GetManagedCertificateDetails(string instanceId, string managedCertId)
        {
            var managedCert = await _mgmtAPI.GetManagedCertificate(instanceId, managedCertId, CurrentAuthContext);

            return new OkObjectResult(managedCert);
        }

        /// <summary>
        /// Add/update the full settings for a specific managed certificate
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("settings/{instanceId}/update")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        public async Task<IActionResult> UpdateManagedCertificateDetails(string instanceId, Models.ManagedCertificate managedCertificate)
        {
            var result = await _mgmtAPI.UpdateManagedCertificate(instanceId, managedCertificate, CurrentAuthContext);

            if (result != null)
            {
                return new OkObjectResult(result);
            }
            else
            {
                return new BadRequestResult();
            }
        }

        /// <summary>
        /// Begin the managed certificate request/renewal process for the given managed certificate id (on demand)
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("order")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> BeginOrder(string instanceId, string id)
        {
            await _mgmtAPI.PerformManagedCertificateRequest(instanceId, id, CurrentAuthContext);

            return new OkResult();
        }

        /// <summary>
        /// Begin the managed certificate request/renewal process a set of managed certificates
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("renew")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        public async Task<IActionResult> PerformRenewal(string instanceId, Models.RenewalSettings settings)
        {
            // TODO: send to instance
            var results = await _client.BeginAutoRenewal(settings, CurrentAuthContext);
            if (results != null)
            {
                return new OkObjectResult(results);
            }
            else
            {
                return new BadRequestResult();
            }
        }

        /// <summary>
        /// Perform default tests for the given configuration
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("test")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Models.StatusMessage>))]
        public async Task<IActionResult> PerformConfigurationTest(string instanceId, Models.ManagedCertificate item)
        {

            var results = await _mgmtAPI.TestManagedCertificateConfiguration(instanceId, item, CurrentAuthContext);

            if (results != null)
            {
                return new OkObjectResult(results);
            }
            else
            {
                return new BadRequestResult();
            }
        }
    }
}
