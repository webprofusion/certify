using Certify.Client;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Server.Hub.Api.Middleware;
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

        private readonly ICertifyInternalApiClient _client;

        private readonly ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        /// <param name="mgmtAPI"></param>
        public PreviewController(ILogger<PreviewController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _client = client;
            _mgmtAPI = mgmtAPI;
        }

        /// <summary>
        /// Get preview of steps for certificate order and deployment
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        [HttpPost]
        [AuthorizedApi]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ActionStep>))]
        public async Task<IActionResult> GetPreview([FromBody] ManagedCertificate item)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            var previewSteps = await _mgmtAPI.GetPreviewActions(item.InstanceId, item, CurrentAuthContext);
            return new OkObjectResult(previewSteps);
        }

        [HttpPost]
        [AuthorizedApi]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
        [Route("managedcertificate")]
        public async Task<IActionResult> GetPreviewAsMarkdown([FromBody] ManagedCertificate item)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

            var previewSteps = await _mgmtAPI.GetPreviewActions(item.InstanceId, item, CurrentAuthContext);

            var markdown = Certify.UI.Blazor.Core.Models.Services.PreviewService.GetStepsAsMarkdown(previewSteps);

            // output steps as html

            var pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

            // returned as text/plain, matching what the string returning version of this action produced
            return Content(Markdown.ToHtml(markdown, pipeline), "text/plain");
        }

        [HttpPost]
        [AuthorizedApi]
        [NoResourceActionRequired("Renders caller supplied markdown as html and reads nothing, so there is no resource to check. Used by the import/export page, whose operators do not all hold managed item permissions.")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
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
        [AuthorizedApi]
        [ProducesResponseType(typeof(List<CertIdentifierItem>), StatusCodes.Status200OK)]
        [Route("csr/identifiers")]
        public async Task<IActionResult> IdentifiersFromCSR([FromBody] string csr)
        {
            var accessCheck = await CheckRequestAuthorized(_client, new AccessCheck(default!, ResourceTypes.ManagedItem, StandardResourceActions.ManagedItemList));

            if (!accessCheck.IsSuccess)
            {
                return Problem(detail: accessCheck.Message, statusCode: (int)System.Net.HttpStatusCode.Unauthorized);
            }

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
