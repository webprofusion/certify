using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Server.Api.Public.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

        [HttpGet]
        [Route("{managedCertId}/download/{format?}")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Get(string managedCertId, string format)
        {
            if (format == null)
            {
                format = "pfx";
            }
            // TODO: certify manager to do all the cert conversion work
            var managedCert = await _client.GetManagedCertificate(managedCertId);

            var content = await System.IO.File.ReadAllBytesAsync(managedCert.CertificatePath);

            return new FileContentResult(content, "application/x-pkcs12") { FileDownloadName = "certificate.pfx" };
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [Produces("application/json")]
        public async Task<IActionResult> Get(string keyword)
        {

            var managedCerts = await _client.GetManagedCertificates(new Models.ManagedCertificateFilter { Keyword = keyword });

            var list = managedCerts.Select(i => new ManagedCertificateInfo
            {
                Id = i.Id,
                Title = i.Name,
                PrimaryDomain = i.RequestConfig.PrimaryDomain,
                Domains = i.RequestConfig.SubjectAlternativeNames,
                DateRenewed = i.DateRenewed,
                DateExpiry = i.DateExpiry
            });

            return new OkObjectResult(list);
        }
    }
}
