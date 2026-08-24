using System.Net;
using System.Text;
using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Shared.Core.Utils.PKI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Provides managed certificate related operations
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
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
        [Route("/api/v1/certificate/{instanceId}/download/{managedCertId}/{format?}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        public async Task<IActionResult> Download(string instanceId, string managedCertId, string format)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.Certificate, StandardResourceActions.CertificateDownload));

            if (!accessCheck.IsSuccess)
            {
                accessCheck = await CheckManagedInstanceSubscriptionDownloadAuthorized(managedCertId);
                if (!accessCheck.IsSuccess)
                {
                    return Problem(detail: accessCheck.Message, statusCode: (int)HttpStatusCode.Unauthorized);
                }
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
            var strictExport = Request.Query["strictExport"].ToString() == "true";
            var exportResult = await _mgmtAPI.ExportCertificate(instanceId, managedCertId, format, strictExport, CurrentAuthContext);

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

        private async Task<Certify.Models.Config.ActionResult> CheckManagedInstanceSubscriptionDownloadAuthorized(string managedCertId)
        {
            var accessToken = GetAccessTokenFromRequest();
            if (accessToken == null)
            {
                return new Certify.Models.Config.ActionResult("X-Client-ID or X-Client-Secret HTTP header missing in request", false);
            }

            var joiningAccessCheck = await IsAccessTokenAuthorized(_client, accessToken, new AccessCheck(default!, ResourceTypes.ManagedInstance, StandardResourceActions.ManagementHubInstanceJoin));
            if (!joiningAccessCheck.IsSuccess)
            {
                return joiningAccessCheck;
            }

            var requestingInstanceId = Request.Headers["X-Certify-HubAssignedId"].ToString();
            if (string.IsNullOrWhiteSpace(requestingInstanceId))
            {
                return new Certify.Models.Config.ActionResult("X-Certify-HubAssignedId header is required.", false);
            }

            var instanceAuth = await ValidateManagedInstanceRequestAuthAsync();
            if (!instanceAuth.IsSuccess)
            {
                return new Certify.Models.Config.ActionResult(instanceAuth.Message, false);
            }

            var matchingInstance = instanceAuth.ManagedInstance;

            if (matchingInstance == null || string.IsNullOrWhiteSpace(matchingInstance.SecurityPrincipalId))
            {
                return new Certify.Models.Config.ActionResult("Managed instance is not registered with a linked security principal.", false);
            }

            var tags = await _client.GetHubItemTags(TaggedItemTypes.ManagedCertificate, managedCertId, SystemAuthContext);

            var certAccessCheck = new AccessCheck
            {
                SecurityPrincipalId = matchingInstance.SecurityPrincipalId,
                ResourceType = ResourceTypes.Certificate,
                ResourceActionId = StandardResourceActions.CertificateDownload,
                Identifier = managedCertId,
                ResourceTags = tags?.ToList()
            };

            var isAuthorized = await _client.CheckSecurityPrincipalHasAccess(certAccessCheck, new AuthContext { UserId = matchingInstance.SecurityPrincipalId });

            return isAuthorized
                ? new Certify.Models.Config.ActionResult("Authorized as managed instance subscription consumer", true)
                : new Certify.Models.Config.ActionResult("Managed instance is not permitted to download this subscribed certificate.", false);
        }

        [HttpGet]
        [Route("{instanceId}/{managedCertId}/decoded")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(object))]
        public async Task<object> GetDecodedCertificate(string instanceId, string managedCertId, bool strictExport)
        {
            var exportResult = await _mgmtAPI.ExportCertificate(instanceId, managedCertId, "pem_fullchain_root", strictExport, CurrentAuthContext);

            if (exportResult.IsSuccess && exportResult.Result != null)
            {
                var pem = Encoding.ASCII.GetString(exportResult.Result ?? []);

                var attributes = CertUtils.DecodePemToAttributes(pem);

                return attributes != null
                    ? Ok(attributes)
                    : new BadRequestResult();
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
        [AuthorizedApi]
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
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LogResult))]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        public async Task<IActionResult> DownloadLogText(string instanceId, string managedCertId)
        {
            var log = await _mgmtAPI.GetItemLog(instanceId, managedCertId, -1, CurrentAuthContext);

            var content = string.Join("\r\n", log.Select(l => $"{l.EventDate?.ToLocalTime().ToString("yyyy-MM-dd H:mm")}\t[{l.LogLevel}]\t{l.Message}"));

            return new FileContentResult(Encoding.UTF8.GetBytes(content), "text/plain") { FileDownloadName = $"{managedCertId}.log" };
        }

        /// <summary>
        /// Get summary counts of all managed certs
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("summary")]
        [AuthorizedApi]
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
        [AuthorizedApi]
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
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificate))]
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
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificate))]
        public async Task<IActionResult> UpdateManagedCertificateDetails(string instanceId, ManagedCertificate managedCertificate)
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
        /// Add a new managed certificate to the given target instance
        /// </summary>

        [HttpPost]
        [Route("/api/v1/certificate")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificateSummary))]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddManagedCertificate([FromBody] ManagedCertificateAddRequest request)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemAdd));
            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)HttpStatusCode.Unauthorized);
            }

            if (request == null)
            {
                return Problem(detail: "A request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(request.InstanceId))
            {
                return Problem(detail: "A target instanceId is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var targetInstance = await _client.GetHubManagedInstance(request.InstanceId, CurrentAuthContext);
            if (targetInstance == null)
            {
                return Problem(detail: $"Managed instance '{request.InstanceId}' was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            if (!request.Identifiers?.Any() == true)
            {
                return Problem(detail: "At least one domain or IP identifier is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var managedCertificate = new ManagedCertificate
            {
                Id = Guid.NewGuid().ToString(),
                InstanceId = request.InstanceId,
                IncludeInAutoRenew = true,
                ItemType = ManagedCertificateType.SSL_ACME
            };

            managedCertificate.InstanceId = request.InstanceId;
            managedCertificate.ItemType = ManagedCertificateType.SSL_ACME;

            managedCertificate.Name = !string.IsNullOrWhiteSpace(request.Title)
                ? request.Title.Trim()
                : managedCertificate.Name.WithDefault(request.Identifiers.FirstOrDefault()?.ToString() ?? "<no title>");

            managedCertificate.RequestConfig ??= new CertRequestConfig();
            managedCertificate.RequestConfig.PrimaryDomain = request.Identifiers.FirstOrDefault(i => i.IdentifierType == CertIdentifierType.Dns)?.Value?.Trim();
            managedCertificate.RequestConfig.SubjectAlternativeNames = [.. NormalizeIdentifierValues(request.Identifiers.Where(i => i.IdentifierType == CertIdentifierType.Dns).Select(i => i.Value).ToArray())];
            managedCertificate.RequestConfig.SubjectIPAddresses = [.. NormalizeIdentifierValues(request.Identifiers.Where(i => i.IdentifierType == CertIdentifierType.Ip).Select(i => i.Value).ToArray())];
            managedCertificate.RequestConfig.Challenges =
            [
                new CertRequestChallengeConfig { ChallengeProvider = "DNS01.API.CertifyManaged", ChallengeType = "dns-01", Parameters = [] },
            ];

            managedCertificate.UseStagingMode = false;

            // populate domain options with defaults from instance if not set in request

            managedCertificate.DomainOptions = [];
            foreach (var identifier in request.Identifiers)
            {
                var domainOption = new DomainOption
                {
                    Type = identifier.IdentifierType,
                    Domain = identifier.Value,
                    IsManualEntry = true,
                    IsSelected = true
                };
                managedCertificate.DomainOptions.Add(domainOption);
            }

            var updated = await _mgmtAPI.UpdateManagedCertificate(request.InstanceId, managedCertificate, CurrentAuthContext);
            if (updated == null)
            {
                return Problem(detail: "The managed certificate could not be created or updated.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.PerformRequest)
            {
                await _mgmtAPI.PerformManagedCertificateRequest(request.InstanceId, updated.Id, CurrentAuthContext);
            }

            return new OkObjectResult(ToManagedCertificateSummary(updated, targetInstance));
        }

        [HttpPost]
        [Route("order")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> BeginOrder(string instanceId, string id)
        {
            await _mgmtAPI.PerformManagedCertificateRequest(instanceId, id, CurrentAuthContext);

            return new OkResult();
        }

        /// <summary>
        /// Perform default tests for the given configuration
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("test")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<StatusMessage>))]
        public async Task<IActionResult> PerformConfigurationTest(string instanceId, ManagedCertificate item)
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

        /// <summary>
        /// Reset status of a managed item to allow it to be re-processed.
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="id">managed item id</param>
        /// <returns></returns>
        [HttpPost]
        [Route("reset")]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ManagedCertificate))]
        public async Task<IActionResult> ResetStatus(string instanceId, string id)
        {

            var results = await _mgmtAPI.ResetManagedItemStatus(instanceId, id, CurrentAuthContext);

            if (results != null)
            {
                return new OkObjectResult(results);
            }
            else
            {
                return new BadRequestResult();
            }
        }

        private static List<string> NormalizeIdentifierValues(ICollection<string>? values)
        {
            return values?
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }

        private static ManagedCertificateSummary ToManagedCertificateSummary(ManagedCertificate item, ManagedInstanceInfo? instance)
        {
            var identifiers = item.GetCertificateIdentifiers();

            return new ManagedCertificateSummary
            {
                InstanceId = item.InstanceId ?? string.Empty,
                InstanceTitle = instance?.DisplayTitle ?? instance?.Title ?? string.Empty,
                OS = instance?.OS ?? string.Empty,
                ClientDetails = instance != null ? $"{instance.ClientName} {instance.ClientVersion}".Trim() : string.Empty,
                Id = item.Id ?? string.Empty,
                Title = item.Name ?? string.Empty,
                PrimaryIdentifier = identifiers.FirstOrDefault(p => p.Value == item.RequestConfig.PrimaryDomain) ?? identifiers.FirstOrDefault(),
                Identifiers = identifiers,
                DateRenewed = item.DateRenewed,
                DateExpiry = item.DateExpiry,
                DateRetrieved = item.DateRetrieved,
                Status = item.Health.ToString(),
                Comments = item.Comments ?? string.Empty,
                HasCertificate = !string.IsNullOrEmpty(item.CertificatePath),
                IsExternallyManaged = item.IsExternallyManaged,
                IsSubscription = item.IsSubscription
            };
        }
    }
}
