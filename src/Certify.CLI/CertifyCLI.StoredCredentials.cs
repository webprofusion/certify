using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Certify.Models.Config;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {

        /// <summary>
        /// Json Serializer Context hints for StoredCredResult to enable source generation for json serialization.
        /// </summary>
        [JsonSerializable(typeof(StoredCredResult))]
        [JsonSerializable(typeof(StoredCredOperationResult))]
        [JsonSerializable(typeof(IEnumerable<StoredCredResult>))]
        public partial class StoredCredResultContext : JsonSerializerContext
        {
        }

        JsonSerializerOptions _credSerializationOptions = new()
        {
            TypeInfoResolver = StoredCredResultContext.Default,
            WriteIndented = true
        };

        public partial class StoredCredResult
        {
            public string Title { get; set; } = string.Empty;
            public string StorageKey { get; set; } = string.Empty;
            public string ProviderType { get; set; } = string.Empty;
            public DateTimeOffset DateCreated { get; set; }
            public DateTimeOffset? DateExpiry { get; set; }
        }
        public partial class StoredCredOperationResult
        {
            public string Status { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string StorageKey { get; set; } = string.Empty;
        }

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
                DateCreated = DateTimeOffset.UtcNow,
                ProviderType = credentialType,
                Secret = secretValue,
                Title = title
            };

            try
            {
                var result = await _certifyClient.UpdateCredentials(cred);
                if (result != null)
                {

                    var resultObject = new StoredCredOperationResult { Status = "OK", Message = "Credential updated", StorageKey = result?.StorageKey };
                    var output = JsonConvert.SerializeObject(resultObject, Formatting.Indented);
                    Console.WriteLine(output);
                }
                else
                {
                    var resultObject = new StoredCredOperationResult { Status = "Error", Message = "Credential update failed" };
                    var output = JsonConvert.SerializeObject(resultObject, Formatting.Indented);
                    Console.WriteLine(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating credentials: {ex.Message}");
            }
        }

        internal async Task ListStoredCredentials(string[] args)
        {
            try
            {
                var result = await _certifyClient.GetCredentials();

                var list = result.Select(s => new StoredCredResult
                {
                    Title = s.Title ?? "N/A",
                    StorageKey = s.StorageKey ?? "N/A",
                    ProviderType = s.ProviderType ?? "N/A",
                    DateCreated = s.DateCreated,
                    DateExpiry = s.DateExpiry
                });

                var output = System.Text.Json.JsonSerializer.Serialize(list, options: _credSerializationOptions);

                Console.WriteLine(output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine($"STACK: {ex.StackTrace}");
            }
        }
        private void WriteOutput(object resultObject)
        {
            var output = JsonConvert.SerializeObject(resultObject, Formatting.Indented);
            Console.WriteLine(output);
        }
    }
}
