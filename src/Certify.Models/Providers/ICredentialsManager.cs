#nullable disable
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Providers;

namespace Certify.Management
{
    public interface ICredentialsManager
    {
        Task<bool> Delete(IManagedItemStore itemStore, string storageKey);
        Task<List<StoredCredential>> GetCredentials(string type = null, string storageKey = null);
        Task<StoredCredential> GetCredential(string storageKey);
        Task<string> GetUnlockedCredential(string storageKey);
        Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey);
        Task<StoredCredential> Update(StoredCredential credentialInfo);
        Task<ActionResult> TestCredentials(string storageKey);
    }
}
