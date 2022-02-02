using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {


        internal async Task AddACMEAccount(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }
            var certificateAuthorityId = args[2];
            var email = args[3];

            var eabKeyId = args.Length >= 5 ? args[4] : null;
            var eabKey = args.Length >= 6 ? args[5] : null;

            var result = await AddACMEAccount(certificateAuthorityId, email, eabKeyId, eabKey);

            if (result.IsSuccess)
            {
                Console.WriteLine("Account created.");
            }
            else
            {
                Console.WriteLine(result.Message);
            }
        }

        private async Task<Models.Config.ActionResult> AddACMEAccount(string certificateAuthorityId, string email, string eabKeyId, string eabKey)
        {
            var accountReg = new ContactRegistration
            {
                CertificateAuthorityId = certificateAuthorityId,
                EmailAddress = email,
                EabKeyId = eabKeyId,
                EabKey = eabKey,
                IsStaging = false,
                AgreedToTermsAndConditions = true
            };
            var result = await _certifyClient.AddAccount(accountReg);

            return result;

        }


        internal async Task ListACMEAccounts()
        {
            var results = await GetACMEAccounts();

            var output = JsonConvert.SerializeObject(results, Formatting.Indented);

            Console.WriteLine(output);

        }
        private async Task<List<AccountDetails>> GetACMEAccounts()
        {
            var results = await _certifyClient.GetAccounts();
            return results;
        }

    }
}
