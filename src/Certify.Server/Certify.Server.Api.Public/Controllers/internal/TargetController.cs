using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Certify.Server.API.Controllers
{
    /// <summary>
    /// Internal API for extended certificate management. Not intended for general use.
    /// </summary>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public class TargetController : ControllerBase
    {

        private readonly ILogger<SystemController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public TargetController(ILogger<SystemController> logger, ICertifyInternalApiClient client)
        {
            _logger = logger;
            _client = client;
        }

        /// <summary>
        /// Get list of known service items (e.g. websites etc) we may want to then check for domains etc to add to cert. May return many items (e.g. thousands of sites)
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [Route("{serverType}/items")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SiteInfo>))]

        public async Task<IActionResult> GetTargetServiceItems(string serverType)
        {
            var targetList = new List<Models.SiteInfo>();

            if (string.Equals(StandardServerTypes.IIS.ToString(), serverType, System.StringComparison.OrdinalIgnoreCase))
            {
                if (await _client.IsServerAvailable(StandardServerTypes.IIS))
                {
                    targetList.AddRange(await _client.GetServerSiteList(StandardServerTypes.IIS));
                }
            }

            return new OkObjectResult(targetList);
        }


        /// <summary>
        /// Return details of single target server item (e.g. 1 site)
        /// </summary>
        /// <param name="serverType"></param>
        /// <param name="itemId"></param>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [Route("{serverType}/item/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SiteInfo))]

        public async Task<IActionResult> GetTargetServiceItem(string serverType, string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return new BadRequestResult();
            }

            var results = new List<SiteInfo>();

            if (serverType == StandardServerTypes.IIS.ToString())
            {
                results = await _client.GetServerSiteList(StandardServerTypes.IIS, itemId);
            }

            if (results.Count == 0)
            {
                return new NotFoundResult();
            }
            else
            {
                return new OkObjectResult(results.First());
            }
        }

        /// <summary>
        /// Get list of known service items (e.g. websites etc) we may want to then check for domains etc to add to cert
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [Route("{serverType}/item/{itemId}/identifiers")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DomainOption>))]

        public async Task<IActionResult> GetTargetServiceItemIdentifiers(string serverType, string itemId)
        {
            var targetList = new List<Models.DomainOption>();

            if (string.Equals(StandardServerTypes.IIS.ToString(), serverType, System.StringComparison.OrdinalIgnoreCase))
            {
                targetList.AddRange(await _client.GetServerSiteDomains(StandardServerTypes.IIS, itemId));
            }

            return new OkObjectResult(targetList);
        }

        /// <summary>
        /// Get list of target services this server supports (e.g. IIS etc)
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [Route("services")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string[]))]
        public async Task<IActionResult> GetTargetServiceTypes()
        {
            var list = new List<string>();

            if (await _client.IsServerAvailable(StandardServerTypes.IIS))
            {
                list.Add(StandardServerTypes.IIS.ToString());
            };

            return new OkObjectResult(list);
        }
    }
}
