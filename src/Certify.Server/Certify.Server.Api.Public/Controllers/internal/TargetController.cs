using Certify.Client;
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
    public partial class TargetController : ApiControllerBase
    {

        private readonly ILogger<TargetController> _logger;

        private readonly ICertifyInternalApiClient _client;

        private readonly ManagementAPI _mgmtAPI;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="client"></param>
        public TargetController(ILogger<TargetController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI)
        {
            _logger = logger;
            _client = client;
            _mgmtAPI = mgmtAPI;
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
    }
}
