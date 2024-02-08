using Certify.Client;
using Certify.Models;
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
    public partial class TargetController : ApiControllerBase
    {

        private readonly ILogger<TargetController> _logger;

        private readonly ICertifyInternalApiClient _client;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public TargetController(ILogger<TargetController> logger, ICertifyInternalApiClient client)
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
            var knownServerType = GetServerTypeFromString(serverType);
            if (knownServerType == null)
            {
                return new NotFoundResult();
            }

            var targetList = new List<Models.SiteInfo>();

            if (await _client.IsServerAvailable((StandardServerTypes)knownServerType))
            {
                targetList.AddRange(await _client.GetServerSiteList((StandardServerTypes)knownServerType));
            }

            return new OkObjectResult(targetList);
        }

        private static StandardServerTypes? GetServerTypeFromString(string value)
        {
            if (System.Enum.TryParse<StandardServerTypes>(value, out var result))
            {
                return result;
            }
            else
            {
                return null;
            }
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

            var knownServerType = GetServerTypeFromString(serverType);
            if (knownServerType == null)
            {
                return new NotFoundResult();
            }

            var results = await _client.GetServerSiteList((StandardServerTypes)knownServerType, itemId);

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

            var knownServerType = GetServerTypeFromString(serverType);
            if (knownServerType == null)
            {
                return new NotFoundResult();
            }

            targetList.AddRange(await _client.GetServerSiteDomains((StandardServerTypes)knownServerType, itemId));

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

            // TODO: make dynamic from service
            if (await _client.IsServerAvailable(StandardServerTypes.IIS))
            {
                list.Add(StandardServerTypes.IIS.ToString());
            };

            if (await _client.IsServerAvailable(StandardServerTypes.Nginx))
            {
                list.Add(StandardServerTypes.Nginx.ToString());
            };
            return new OkObjectResult(list);
        }
    }
}
