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

        internal async Task SetPreferredCertificateAuthority(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Not enough arguments. Usage: certify ca setpreferred <CA ID|any>");
                return;
            }

            var certificateAuthorityId = args[2]?.Trim();
            if (string.IsNullOrWhiteSpace(certificateAuthorityId))
            {
                Console.WriteLine("Certificate Authority ID is required.");
                return;
            }

            if (certificateAuthorityId.Equals("any", StringComparison.OrdinalIgnoreCase) || certificateAuthorityId.Equals("auto", StringComparison.OrdinalIgnoreCase) || certificateAuthorityId.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                certificateAuthorityId = null;
            }

            CertificateAuthority ca = null;
            if (certificateAuthorityId != null)
            {
                var certificateAuthorities = await _certifyClient.GetCertificateAuthorities();
                ca = certificateAuthorities.FirstOrDefault(c => string.Equals(c.Id, certificateAuthorityId, StringComparison.OrdinalIgnoreCase));

                if (ca == null)
                {
                    Console.WriteLine($"Certificate Authority '{certificateAuthorityId}' was not found. Use 'certify ca list' to see available Certificate Authorities.");
                    return;
                }

                certificateAuthorityId = ca.Id;
            }

            _prefs.DefaultCertificateAuthority = certificateAuthorityId;

            if (await _certifyClient.SetPreferences(_prefs))
            {
                Console.WriteLine(certificateAuthorityId == null ? "Preferred Certificate Authority cleared." : $"Preferred Certificate Authority set to {ca.Title} ({ca.Id}).");
            }
            else
            {
                Console.WriteLine("Failed to update preferred Certificate Authority.");
            }
        }

        internal async Task ListCertificateAuthorities()
        {
            var results = await _certifyClient.GetCertificateAuthorities();

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
