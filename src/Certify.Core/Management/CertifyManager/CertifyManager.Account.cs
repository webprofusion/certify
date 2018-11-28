using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Management
{
    public partial class CertifyManager
    {
        public List<RegistrationItem> GetContactRegistrations()
        {
            return _vaultProvider.GetContactRegistrations();
        }

        public async Task<bool> AddRegisteredContact(ContactRegistration reg)
        {
            // in practise only one registered contact is used, so remove alternatives to avoid cert
            // processing picking up the wrong one
            RemoveAllContacts();

            // now attempt to register the new contact
            if (reg.AgreedToTermsAndConditions)
            {
                _serviceLog?.Information($"Registering contact with ACME CA: {reg.EmailAddress}");

                return await _acmeClientProvider.AddNewAccountAndAcceptTOS(_serviceLog, reg.EmailAddress);
            }
            else
            {
                // did not agree to terms
                return false;
            }
        }

        public void RemoveAllContacts()
        {
            var regList = _vaultProvider.GetContactRegistrations();
            foreach (var reg in regList)
            {
                _vaultProvider.DeleteContactRegistration(reg.Id);
            }
        }

        public string GetPrimaryContactEmail()
        {
            var contacts = GetContactRegistrations();
            if (contacts.Any())
            {
                return contacts.FirstOrDefault()?.Name.Replace("mailto:", "");
            }
            else
            {
                return null;
            }
        }
    }
}
