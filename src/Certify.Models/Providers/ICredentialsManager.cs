#nullable disable
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Models.Config;

namespace Certify.Management
{
    public interface ICredentialsManager
    {
        Task<bool> Delete(string storageKey);
        Task<List<StoredCredential>> GetCredentials(string type = null);
        Task<StoredCredential> GetCredential(string storageKey);
        Task<string> GetUnlockedCredential(string storageKey);
        Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey);
        Task<StoredCredential> Update(StoredCredential credentialInfo);
    }
}
