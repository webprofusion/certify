using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Certify.Models.Config
{
    /// <summary>
    /// A secret field of a stored credential can take its value from an API Key or Token stored credential, so one secret can feed
    /// many credentials. The link is stored in place of the value, as the linked credential's storage key under the field's key plus <see cref="LinkKeySuffix"/>.
    /// </summary>
    public static class StoredCredentialLinks
    {
        public const string LinkKeySuffix = ".credential";

        /// <summary>
        /// The type of stored credential a secret field can link to
        /// </summary>
        public const string LinkedCredentialType = StandardAuthTypes.STANDARD_AUTH_API_TOKEN;

        /// <summary>
        /// The field of the linked credential which supplies the value
        /// </summary>
        public const string LinkedValueKey = "api_token";

        public static string GetLinkKey(string key) => key + LinkKeySuffix;

        public static bool IsLinkKey(string? key) => key?.EndsWith(LinkKeySuffix, System.StringComparison.Ordinal) == true;

        /// <summary>
        /// Get the fields to edit for a credential type: its credential fields, with each secret field followed by a choice of credential to link it to instead
        /// </summary>
        public static List<ProviderParameter> GetEditableFields(ProviderDefinition? definition)
        {
            var fields = new List<ProviderParameter>();

            foreach (var p in definition?.ProviderParameters?.Where(p => p.IsCredential) ?? [])
            {
                fields.Add((ProviderParameter)p.Clone());

                if (p.IsPassword && definition!.Id != LinkedCredentialType)
                {
                    fields.Add(new ProviderParameter
                    {
                        Key = GetLinkKey(p.Key!),
                        Name = $"{p.Name} from Stored Credential",
                        Description = $"(optional) use the value of an API Key or Token stored credential instead of entering the {p.Name}",
                        IsCredential = true,
                        Type = OptionType.StoredCredential,
                        ExtendedConfig = LinkedCredentialType
                    });
                }
            }

            return fields;
        }

        /// <summary>
        /// Get the values to store for edited credential fields. A secret field linked to another credential stores the link instead of a value
        /// </summary>
        public static ActionResult<Dictionary<string, string>> GetValuesToStore(IEnumerable<ProviderParameter> fields)
        {
            var list = fields.Where(f => !string.IsNullOrEmpty(f?.Key)).ToList();
            var values = new Dictionary<string, string>();

            foreach (var f in list.Where(f => !IsLinkKey(f.Key)))
            {
                var linkKey = GetLinkKey(f.Key!);
                var linkedStorageKey = list.FirstOrDefault(l => l.Key == linkKey)?.Value?.Trim();

                if (!string.IsNullOrEmpty(linkedStorageKey))
                {
                    values[linkKey] = linkedStorageKey!;
                }
                else if (!string.IsNullOrEmpty(f.Value))
                {
                    values[f.Key!] = f.Value!;
                }
                else if (f.IsRequired)
                {
                    return new ActionResult<Dictionary<string, string>>($"{f.Name} is a required value", false);
                }
            }

            return new ActionResult<Dictionary<string, string>>("OK", true, values);
        }

        /// <summary>
        /// Get the storage keys of the credentials linked from a credential's values
        /// </summary>
        public static List<string> GetLinkedStorageKeys(Dictionary<string, string>? values)
            => values?.Where(v => IsLinkKey(v.Key) && !string.IsNullOrWhiteSpace(v.Value)).Select(v => v.Value.Trim()).Distinct().ToList() ?? [];

        /// <summary>
        /// Get the storage keys of the credentials linked from a credential's unlocked secret. Secrets which are not a set of values link to nothing
        /// </summary>
        public static List<string> GetLinkedStorageKeys(string? secret)
        {
            if (string.IsNullOrEmpty(secret) || !secret!.Contains(LinkKeySuffix))
            {
                return [];
            }

            try
            {
                return GetLinkedStorageKeys(JsonConvert.DeserializeObject<Dictionary<string, string>>(secret));
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
