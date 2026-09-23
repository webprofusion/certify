using Certify.Client;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Hub activity history and overview: the activity feed, request run history, daily totals, what needs attention,
    /// upcoming renewals and instance connection history. The operations are generated from the API method
    /// definitions (see Certify.SourceGenerators ApiMethods).
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="client"></param>
    /// <param name="mgmtAPI"></param>
    [ApiController]
    [Route("internal/v1/[controller]")]
    public partial class ActivityController(ILogger<ActivityController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI) : ApiControllerBase
    {
        private readonly ILogger<ActivityController> _logger = logger;
        private readonly ICertifyInternalApiClient _client = client;
        private readonly ManagementAPI _mgmtAPI = mgmtAPI;
    }
}
