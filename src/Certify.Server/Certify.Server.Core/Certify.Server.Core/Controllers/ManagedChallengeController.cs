using Certify.Management;
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

        /// <summary>
        /// Whether a security principal may use managed challenges for a set of identifiers. Callers which need to
        /// answer that before there is a challenge request to perform - the ACME endpoints accepting an order, for
        /// one - ask it here rather than reassembling it from access control primitives.
        /// </summary>
        [HttpPost, Route("authorize")]
        public async Task<Models.Config.ActionResult> AuthorizeManagedChallengeIdentifiers(ManagedChallengeAuthorizationCheck check)
        {
            DebugLog();

            return await _certifyManager.AuthorizeManagedChallengeIdentifiers(check);
        }

        [HttpPost, Route("request")]
        public async Task<Models.Config.ActionResult> PerformChallengeResponse(ManagedChallengeRequest request)
        {
            DebugLog();

            var result = await _certifyManager.PerformManagedChallengeRequest(request);

            return result;
        }

        [HttpPost, Route("requestbegin")]
        public async Task<ManagedChallengeOperation> BeginChallengeResponse(ManagedChallengeRequest request)
        {
            DebugLog();

            return await _certifyManager.BeginManagedChallengeRequest(request);
        }

        [HttpGet, Route("requeststatus/{id}")]
        public async Task<ActionResult<ManagedChallengeOperation>> GetChallengeResponseOperation(string id)
        {
            DebugLog();

            var result = await _certifyManager.GetManagedChallengeOperation(id);

            if (result == null)
            {
                return NotFound();
            }

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
