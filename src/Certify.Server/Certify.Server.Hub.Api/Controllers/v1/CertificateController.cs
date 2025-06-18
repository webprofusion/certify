using System.Net;
using System.Text;
using Certify.Client;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
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
        /// <param name="mgmtApi"></param>
        public CertificateController(ILogger<CertificateController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtApi)
        {
            _logger = logger;
            _client = client;

            _mgmtAPI = mgmtApi;
        }

        /// <summary>
        /// Download the latest certificate for the given managed certificate. For auth provide either a valid JWT via Authorization header or use an API token (using X-ClientID and X-Client-Secret HTTP headers).
        /// 
        /// </summary>
        /// <param name="instanceId">Instance to fetch managed certificate info from</param>
        /// <param name="managedCertId">Id of managed cert to fetch</param>
        /// <param name="format">pfx = PKCS#12 archive, pem_key = private key only, pem encoded, pem_fullchain = end-entity + intermediates chain, pem_fullchain_key = chain plus key, pem_fullchain_root = chain plus root, pem_fullchain_root_key = chain plus root and key </param>
        /// <returns>The certificate file in the chosen format</returns>
        [HttpGet]
        [Route("{instanceId}/download/{managedCertId}/{format?}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        public async Task<IActionResult> Download(string instanceId, string managedCertId, string format)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.Certificate, StandardResourceActions.CertificateDownload));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)HttpStatusCode.Unauthorized);
            }

            // default to PFX output
            if (format == null)
            {
                format = "pfx";
            }

            // fetch managed cert info an check if we have a cert available and if any of our caching headers are applicable
            var managedCert = await _mgmtAPI.GetManagedCertificate(instanceId, managedCertId, CurrentAuthContext);

            if (managedCert == null)
            {
                return new NotFoundResult();
            }

            if (managedCert.DateRenewed == null)
            {
                // item exists but a cert is not yet available, set Retry-After header in RC1123 date format
                var nextAttempt = managedCert.DateNextScheduledRenewalAttempt ?? DateTimeOffset.UtcNow.AddHours(1);
                Response.Headers.RetryAfter = nextAttempt.ToString("r");
            }

            var headers = Request.GetTypedHeaders();

            // allow client to skip the download by sending an If-Modified-Since http header. If not renewed since that date return 304 Not Modified.
            if (headers.IfModifiedSince.HasValue && headers.IfModifiedSince.Value > managedCert.DateRenewed)
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            // allow client to skip the download by sending an If-None-Match header with a quote "<thumbprint hash>" of the cert they currently have. wildcard/weak tags not supported.
            if (headers.IfNoneMatch.Any(etag => string.Equals(etag.Tag.ToString().Replace("\"", ""), managedCert.CertificateThumbprintHash, StringComparison.InvariantCultureIgnoreCase)))
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            // perform the export from the instance holding the cert
            var exportResult = await _mgmtAPI.ExportCertificate(instanceId, managedCertId, format, CurrentAuthContext);

            //return the cert or cert component as a file
            if (exportResult.IsSuccess && exportResult.Result != null)
            {
                if (!string.IsNullOrEmpty(managedCert.CertificateThumbprintHash))
                {
                    Response.Headers.Append("ETag", managedCert.CertificateThumbprintHash.ToLowerInvariant());
                }

                if (format == "pfx")
                {
                    return new FileContentResult(exportResult.Result, "application/x-pkcs12") { FileDownloadName = "certificate.pfx" };
                }
                else
                {
                    // for PEM formats, return as text/plain
                    return new FileContentResult(exportResult.Result, "text/plain") { FileDownloadName = $"{format}.pem" };
                }
            }
            else
            {
                return Problem(detail: exportResult.Message, statusCode: (int)HttpStatusCode.BadRequest);
            }
        }

        /// <summary>
        /// Download log entries for the given managed certificate
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertId"></param>
        /// <param name="maxLines"></param>
        /// <returns>Log file as LogItem list</returns>
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

            var log = await _mgmtAPI.GetItemLog(instanceId, managedCertId, maxLines, CurrentAuthContext);

            return new OkObjectResult(new LogResult { Items = log });
        }

        /// <summary>
        /// Download text log for the given managed certificate
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="managedCertId"></param>
        /// <returns>Log file in text format</returns>
        [HttpGet]
        [Route("{managedCertId}/log/download")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LogResult))]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        public async Task<IActionResult> DownloadLogText(string instanceId, string managedCertId)
        {
            var log = await _mgmtAPI.GetItemLog(instanceId, managedCertId, -1, CurrentAuthContext);

            var content = string.Join("\r\n", log.Reverse().Select(l => $"{l.EventDate}\t[{l.LogLevel}]\t{l.Message}"));

            return new FileContentResult(Encoding.UTF8.GetBytes(content), "text/plain") { FileDownloadName = $"{managedCertId}.log" };
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
                Status = i.Health.ToString(),
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
        /// <returns></returns>
        [HttpGet]
        [Route("summary")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusSummary))]
        public async Task<IActionResult> GetManagedCertificateSummary()
        {
            var summary = await _mgmtAPI.GetManagedCertificateSummary(CurrentAuthContext);
            return new OkObjectResult(summary);
        }

        /// <summary>
        /// Retrieves the summary of a managed certificate for a specific instance using the provided instance ID.
        /// </summary>
        /// <returns>Returns an IActionResult containing the summary of the managed certificate.</returns>
        [HttpGet]
        [Route("{instanceId}/summary")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(StatusSummary))]
        public async Task<IActionResult> GetInstanceManagedCertificateSummary(string instanceId)
        {
            var summary = await _mgmtAPI.GetManagedCertificateSummary(instanceId, CurrentAuthContext);
            return new OkObjectResult(summary);
        }

        /// <summary>
        /// Gets the full settings for a specific managed certificate
        /// </summary>
        /// <param name="instanceId">target instance</param>
        /// <param name="managedCertId">managed item</param>
        /// <returns></returns>
        [HttpGet]
        [Route("{instanceId}/settings/{managedCertId}")]
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
        /// <param name="instanceId"></param>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("{instanceId}/settings/update")]
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
        /// <param name="instanceId"></param>
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
        /// <param name="instanceId"></param>
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
        /// <param name="instanceId"></param>
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
