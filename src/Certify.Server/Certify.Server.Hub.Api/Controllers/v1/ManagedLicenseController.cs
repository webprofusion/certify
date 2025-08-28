using Certify.Client;
using Certify.Server.Hub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Server.Hub.Api.Controllers
{
    /// <summary>
    /// Administer managed product licenses
    /// </summary>
    /// <remarks>
    /// Constructor
    /// </remarks>
    /// <param name="logger"></param>
    /// <param name="client"></param>
    [ApiController]
    [Route("api/v1/[controller]")]
    public partial class ManagedLicenseController(ILogger<ManagedLicenseController> logger, ICertifyInternalApiClient client, ManagementAPI mgmtAPI) : ApiControllerBase
    {
        private readonly ILogger<ManagedLicenseController> _logger = logger;
        private readonly ICertifyInternalApiClient _client = client;
        private readonly ManagementAPI _mgmtAPI = mgmtAPI;
    }
}
