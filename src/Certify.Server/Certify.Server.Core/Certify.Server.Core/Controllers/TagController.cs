using Certify.Management;
using Certify.Models.Hub;
using Microsoft.AspNetCore.Mvc;

namespace Certify.Service.Controllers
{
    [ApiController]
    [Route("api/tags")]
    public class TagController : ControllerBase
    {
        private ICertifyManager _certifyManager;

        public TagController(ICertifyManager certifyManager)
        {
            _certifyManager = certifyManager;
        }

        [HttpPost, Route("add")]
        public async Task<Models.Config.ActionResult> AddTag([FromBody] ItemTag tag)
        {
            return await _certifyManager.AddHubItemTags([tag]);
        }

        [HttpPost, Route("update")]
        public async Task<Models.Config.ActionResult> UpdateTag([FromBody] ItemTag tag)
        {
            throw new NotImplementedException("UpdateTag is not implemented yet. Use AddTag instead to create or update tags.");
        }

        [HttpDelete, Route("delete/{id}")]
        public async Task<Models.Config.ActionResult> DeleteTag(string id)
        {
            return await _certifyManager.RemoveHubItemTags([id]);
        }

        [HttpGet, Route("list")]
        public async Task<ICollection<ItemTag>> GetTags()
        {
            return await _certifyManager.GetAllHubItemTags();
        }
    }
}
