using Certify.Models;
using Certify.Server.Hub.Api.Services;
using Markdig;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Internal API for extended certificate management. Not intended for general use.
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public partial class PreviewController : ApiControllerBase
    {

        private readonly ILogger<PreviewController> _logger;

        private readonly ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="mgmtAPI"></param>
        public PreviewController(ILogger<PreviewController> logger, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _mgmtAPI = mgmtAPI;
        }

        /// <summary>
        /// Get preview of steps for certificate order and deployment
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        [HttpPost]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ActionStep>))]
        public async Task<IActionResult> GetPreview([FromBody] ManagedCertificate item)
        {
            var previewSteps = await _mgmtAPI.GetPreviewActions(item.InstanceId, item, CurrentAuthContext);
            return new OkObjectResult(previewSteps);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
        [Route("managedcertificate")]
        public async Task<string> GetPreviewAsMarkdown([FromBody] ManagedCertificate item)
        {
            var previewSteps = await _mgmtAPI.GetPreviewActions(item.InstanceId, item, CurrentAuthContext);

            var markdown = Certify.UI.Blazor.Core.Models.Services.PreviewService.GetStepsAsMarkdown(previewSteps);

            // output steps as html

            var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

            return Markdown.ToHtml(markdown, pipeline);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
        [Route("rendermarkdown")]
        public async Task<string> RenderMarkdown([FromBody] string markdown)
        {

            // output steps as html

            var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

            return Markdown.ToHtml(markdown, pipeline);
        }

        [HttpPost]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [ProducesResponseType(typeof(List<CertIdentifierItem>), StatusCodes.Status200OK)]
        [Route("csr/identifiers")]
        public async Task<IActionResult> IdentifiersFromCSR([FromBody] string csr)
        {

            if (csr.Contains("CERTIFICATE REQUEST"))
            {

                var domains = Certify.Shared.Core.Utils.PKI.CSRUtils.DecodeCsrSubjects(csr);
                var certIdentifiers = new List<CertIdentifierItem>();

                foreach (var item in domains)
                {
                    certIdentifiers.Add(new CertIdentifierItem(CertIdentifierType.Dns, item));
                }

                return new OkObjectResult(certIdentifiers);
            }
            else
            {
                return BadRequest("Invalid CSR");
            }
        }
    }
}
