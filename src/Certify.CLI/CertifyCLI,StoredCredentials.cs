using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {

        internal async Task UpdateStoredCredential(string[] args)
        {
            if (args.Length < 6)
            {
                Console.WriteLine("Not enough arguments");
                return;
            }

            var storageKey = args[2];
            var title = args[3];
            var credentialType = args[4];
            var secretValue = args[5];

            var cred = new StoredCredential
            {
                StorageKey = storageKey,
                DateCreated = DateTime.Now,
                ProviderType = credentialType,
                Secret = secretValue,
                Title = title
            };

            var result = await _certifyClient.UpdateCredentials(cred);

            if (result != null)
            {
                var resultObject = new { Status = "OK", Message = "Credential updated", StorageKey = result?.StorageKey };
                var output = JsonConvert.SerializeObject(resultObject, Formatting.Indented);
                Console.WriteLine(output);
            }
            else
            {
                var resultObject = new { Status = "Error", Message = "Credential update failed" };
                var output = JsonConvert.SerializeObject(resultObject, Formatting.Indented);
                Console.WriteLine(output);
            }
        }

        internal async Task ListStoredCredentials(string[] args)
        {
            var result = await _certifyClient.GetCredentials();

            var output = JsonConvert.SerializeObject(result.Select(s => new { s.Title, s.StorageKey, s.ProviderType, s.DateCreated }), Formatting.Indented);

            Console.WriteLine(output);
        }
    }
}
