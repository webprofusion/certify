using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Certify.Models.API;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Certify.Server.API.Controllers
{
    /// <summary>
    /// Provides managed certificate related operations
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class CertificateController : ControllerBase
    {

        private readonly ILogger<CertificateController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public CertificateController(ILogger<CertificateController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
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
        public async Task<IActionResult> Download(string managedCertId, string format, string mode)
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
            var managedCert = await _client.GetManagedCertificate(managedCertId);

            if (managedCert == null)
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
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
        public async Task<IActionResult> DownloadLog(string managedCertId, int maxLines = 1000)
        {
            var managedCert = await _client.GetManagedCertificate(managedCertId);

            if (managedCert == null)
            {
                return new NotFoundResult();
            }

            if (maxLines > 1000)
            {
                maxLines = 1000;
            }

            var log = await _client.GetItemLog(managedCertId, maxLines);
            var logByteArrays = log.Select(l => System.Text.Encoding.UTF8.GetBytes(l + "\n")).ToArray();

            // combine log lines to one byte array

            var bytes = new byte[logByteArrays.Sum(a => a.Length)];
            var offset = 0;
            foreach (var array in logByteArrays)
            {
                System.Buffer.BlockCopy(array, 0, bytes, offset, array.Length);
                offset += array.Length;
            }

            return new FileContentResult(bytes, "text/plain") { FileDownloadName = "log.txt" };

        }


        /// <summary>
        /// Download text log for the given managed certificate
        /// </summary>
        /// <param name="managedCertId"></param>
        /// <param name="maxLines"></param>
        /// <returns>Log file in text format</returns>
        [HttpGet]
        [Route("{managedCertId}/log/text")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
        public async Task<IActionResult> DownloadLogAsText(string managedCertId, int maxLines = 1000)
        {
            var managedCert = await _client.GetManagedCertificate(managedCertId);

            if (managedCert == null)
            {
                return new NotFoundResult();
            }

            if (maxLines > 1000)
            {
                maxLines = 1000;
            }

            var log = await _client.GetItemLog(managedCertId, maxLines);

            return new OkObjectResult(string.Join("\n", log));

        }

        /// <summary>
        /// Get all managed certificates matching criteria
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedCertificateSummary>))]
        public async Task<IActionResult> GetManagedCertificates(string keyword)
        {

            var managedCerts = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { Keyword = keyword });

            //TODO: this assumes all identifiers are DNS, may be IPs in the future.

            var list = managedCerts.Select(i => new ManagedCertificateSummary
            {
                Id = i.Id,
                Title = i.Name,
                PrimaryIdentifier = i.GetCertificateIdentifiers().FirstOrDefault(p => p.Value == i.RequestConfig.PrimaryDomain) ?? i.GetCertificateIdentifiers().FirstOrDefault(),
                Identifiers = i.GetCertificateIdentifiers(),
                DateRenewed = i.DateRenewed,
                DateExpiry = i.DateExpiry,
                Comments = i.Comments,
                Status = i.LastRenewalStatus?.ToString() ?? "",
                HasCertificate = !string.IsNullOrEmpty(i.CertificatePath)
            }).OrderBy(a => a.Title);

            return new OkObjectResult(list);
        }

        /// <summary>
        /// Gets the full settings for a specific managed certificate
        /// </summary>
        /// <param name="managedCertId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("settings/{managedCertId}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        [SwaggerOperation(OperationId = nameof(GetManagedCertificateDetails))]
        public async Task<IActionResult> GetManagedCertificateDetails(string managedCertId)
        {

            var managedCert = await _client.GetManagedCertificate(managedCertId);

            return new OkObjectResult(managedCert);
        }

        /// <summary>
        /// Add/update the full settings for a specific managed certificate
        /// </summary>
        /// <param name="managedCertificate"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("settings/update")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        [SwaggerOperation(OperationId = nameof(UpdateManagedCertificateDetails))]
        public async Task<IActionResult> UpdateManagedCertificateDetails(Models.ManagedCertificate managedCertificate)
        {

            var result = await _client.UpdateManagedCertificate(managedCertificate);
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
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        [SwaggerOperation(OperationId = nameof(BeginOrder))]
        public async Task<IActionResult> BeginOrder(string id)
        {

            var result = await _client.BeginCertificateRequest(id, true, false);
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
        /// Begin the managed certificate request/renewal process a set of managed certificates
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("renew")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Models.ManagedCertificate))]
        [SwaggerOperation(OperationId = nameof(PerformRenewal))]
        public async Task<IActionResult> PerformRenewal(Models.RenewalSettings settings)
        {

            var results = await _client.BeginAutoRenewal(settings);
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
        [SwaggerOperation(OperationId = nameof(PerformConfigurationTest))]
        public async Task<IActionResult> PerformConfigurationTest(Models.ManagedCertificate item)
        {

            var results = await _client.TestChallengeConfiguration(item);
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
