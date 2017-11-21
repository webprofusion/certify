using Certify.Models;
using System.Threading.Tasks;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/contacts")]
    public class ContactsController : Controllers.ControllerBase
    {
        private Management.ICertifyManager _certifyManager = null;

        public ContactsController(Management.ICertifyManager manager)
        {
            _certifyManager = manager;
        }

        [HttpGet, Route("primary")]
        public string GetPrimaryContact()
        {
            DebugLog();

            return _certifyManager.GetPrimaryContactEmail();
        }

        [HttpPost, Route("primary")]
        public async Task<bool> SetPrimaryContact(ContactRegistration contact)
        {
            return await _certifyManager.AddRegisteredContact(contact);
        }
    }
}