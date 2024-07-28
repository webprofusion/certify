using Certify.Models;
using Certify.Server.Api.Public.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Api.Public.Controllers
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
    }
}
