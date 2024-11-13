using Certify.Management;
using Certify.Models.Config;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/managedchallenge")]
    public class ManagedChallengeController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public ManagedChallengeController(ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("")]
        public async Task<ICollection<ManagedChallenge>> Get()
        {
            DebugLog();

            return await _certifyManager.GetManagedChallenges();
        }

        [HttpPost, Route("")]
        public async Task<Models.Config.ActionResult> Update(ManagedChallenge update)
        {
            DebugLog();

            return await _certifyManager.UpdateManagedChallenge(update);
        }

        [HttpDelete, Route("{id}")]
        public async Task<Models.Config.ActionResult> Delete(string id)
        {
            DebugLog();

            return await _certifyManager.DeleteManagedChallenge(id);
        }

        [HttpPost, Route("request")]
        public async Task<Models.Config.ActionResult> PerformChallengeResponse(ManagedChallengeRequest request)
        {
            DebugLog();

            var result = await _certifyManager.PerformManagedChallengeRequest(request);

            return result;
        }

        [HttpPost, Route("cleanup")]
        public async Task<Models.Config.ActionResult> CleanupChallengeResponse(ManagedChallengeRequest request)
        {
            DebugLog();

            var result = await _certifyManager.CleanupManagedChallengeRequest(request);

            return result;
        }
    }
}
