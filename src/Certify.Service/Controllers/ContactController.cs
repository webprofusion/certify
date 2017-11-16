using Certify.Models;
using System.Collections.Generic;
using System.Linq;
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

            var contacts = _certifyManager.GetContactRegistrations();
            if (contacts.Any())
            {
                return contacts.FirstOrDefault()?.Name.Replace("mailto:", "");
            }
            else
            {
                return null;
            }
        }

        [HttpPost, Route("primary")]
        public async Task<bool> SetPrimaryContact(ContactRegistration contact)
        {
            return await _certifyManager.AddRegisteredContact(contact);
        }
    }
}