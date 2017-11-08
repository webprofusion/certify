using Certify.Models;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace Certify.Service
{
    [RoutePrefix("api/contacts")]
    public class ContactsController : ApiController
    {
        private Management.CertifyManager _certifyManager = new Certify.Management.CertifyManager();

        [HttpGet, Route("primary")]
        public string GetPrimaryContact()
        {
            var contacts = _certifyManager.GetContactRegistrations();
            if (contacts.Any())
            {
                return contacts.FirstOrDefault()?.Name;
            }
            else
            {
                return null;
            }
        }

        [HttpPost, Route("primary")]
        public bool SetPrimaryContact(ContactRegistration contact)
        {
            return _certifyManager.AddRegisteredContact(contact);
        }
    }
}