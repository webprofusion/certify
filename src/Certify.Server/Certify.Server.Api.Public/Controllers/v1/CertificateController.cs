using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Server.Api.Public.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class CertificateController : ControllerBase
    {

        private readonly ILogger<CertificateController> _logger;

        private readonly ICertifyInternalApiClient _client;

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
        public async Task<IActionResult> DownloadCertificate(string managedCertId, string format, string mode)
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
            foreach (byte[] array in logByteArrays)
            {
                System.Buffer.BlockCopy(array, 0, bytes, offset, array.Length);
                offset += array.Length;
            }

            return new FileContentResult(bytes, "text/plain") { FileDownloadName = "log.txt" };

        }

        /// <summary>
        /// Get all managed certificates matching criteria
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ManagedCertificateInfo>))]
        public async Task<IActionResult> GetManagedCertificates(string keyword)
        {

            var managedCerts = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { Keyword = keyword });

            //TODO: this assumes all identifiers are DNS, may be IPs in the future.

            var list = managedCerts.Select(i => new ManagedCertificateInfo
            {
                Id = WebUtility.UrlEncode(i.Id),
                Title = i.Name,
                PrimaryIdentifier = new Identifier { Type = "dns", Value = i.RequestConfig.PrimaryDomain },
                Identifiers = i.RequestConfig.SubjectAlternativeNames.Select(s => new Identifier { Type = "dns", Value = s }),
                DateRenewed = i.DateRenewed,
                DateExpiry = i.DateExpiry,
                Comments = i.Comments,
                Status = i.LastRenewalStatus?.ToString() ?? "",
                HasCertificate = !string.IsNullOrEmpty(i.CertificatePath)
            });

            return new OkObjectResult(list);
        }
    }
}
