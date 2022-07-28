using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;
using Certify.Models.Config;
using Certify.Providers;
using Newtonsoft.Json;

namespace Certify.Management
{
    public class CredentialsManagerBase
    {
        
        internal bool _useWindowsNativeFeatures = true;

        public CredentialsManagerBase(bool useWindowsNativeFeatures = true)
        {
            _useWindowsNativeFeatures = useWindowsNativeFeatures;
        }

        public async Task<bool> IsCredentialInUse(IManagedItemStore itemStore, string storageKey)
        {
            // TODO: inject item manager or move this check out to certify manager
            var managedCertificates = await itemStore.Find(new Models.ManagedCertificateFilter { StoredCredentialKey = storageKey });
            if (managedCertificates.Any())
            {
                // credential is in use
                return true;
            }
            else
            {
                return false;
            }
        }

        public virtual async Task<StoredCredential> GetCredential(string storageKey)
        {
            throw new NotImplementedException();
        }

        public virtual async Task<Dictionary<string, string>> GetUnlockedCredentialsDictionary(string storageKey)
        {
            throw new NotImplementedException();
        }

        public async Task<ActionResult> TestCredentials(string storageKey)
        {
            // create instance of provider type then test credentials
            var storedCredential = await GetCredential(storageKey);

            if (storedCredential == null)
            {
                return new ActionResult { IsSuccess = false, Message = "No credentials found." };
            }

            var credentials = await GetUnlockedCredentialsDictionary(storedCredential.StorageKey);

            if (credentials == null)
            {
                return new ActionResult { IsSuccess = false, Message = "Failed to retrieve decrypted credentials." };
            }

            /*if (storedCredential.ProviderType.StartsWith("DNS"))
            {
                try
                {
                    var dnsProvider = await ChallengeProviders.GetDnsProvider(storedCredential.ProviderType, credentials, new Dictionary<string, string> { });

                    if (dnsProvider == null)
                    {
                        return new ActionResult { IsSuccess = false, Message = "Could not create DNS provider API. Invalid or unrecognised." };
                    }

                    return await dnsProvider.Test();
                }
                catch (Exception exp)
                {
                    return new ActionResult { IsSuccess = false, Message = "Failed to init DNS Provider " + storedCredential.ProviderType + " :: " + exp.Message };
                }
            }
            else*/
            {
                return new ActionResult { IsSuccess = true, Message = "No test available." };
            }
        }

        /// <summary>
        /// Get protected version of a secret 
        /// </summary>
        /// <param name="clearText"></param>
        /// <param name="optionalEntropy"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        public string Protect(
                string clearText,
                string optionalEntropy = null,
                DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            // https://www.thomaslevesque.com/2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/

            if (clearText == null)
            {
                return null;
            }

            if (_useWindowsNativeFeatures)
            {

                var clearBytes = Encoding.UTF8.GetBytes(clearText);
                var entropyBytes = string.IsNullOrEmpty(optionalEntropy)
                    ? null
                    : Encoding.UTF8.GetBytes(optionalEntropy);
                var encryptedBytes = ProtectedData.Protect(clearBytes, entropyBytes, scope);
                return Convert.ToBase64String(encryptedBytes);
            }
            else
            {
                // TODO: dummy implementation, require alternative implementation for non-windows
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(clearText).Reverse().ToArray());
            }
        }

        /// <summary>
        /// Get unprotected version of a secret 
        /// </summary>
        /// <param name="encryptedText"></param>
        /// <param name="optionalEntropy"></param>
        /// <param name="scope"></param>
        /// <returns></returns>
        public string Unprotect(
            string encryptedText,
            string optionalEntropy = null,
            DataProtectionScope scope = DataProtectionScope.CurrentUser)
        {
            // https://www.thomaslevesque.com/2013/05/21/an-easy-and-secure-way-to-store-a-password-using-data-protection-api/

            if (encryptedText == null)
            {
                throw new ArgumentNullException("encryptedText");
            }

            if (_useWindowsNativeFeatures)
            {
                var encryptedBytes = Convert.FromBase64String(encryptedText);
                var entropyBytes = string.IsNullOrEmpty(optionalEntropy)
                    ? null
                    : Encoding.UTF8.GetBytes(optionalEntropy);
                var clearBytes = ProtectedData.Unprotect(encryptedBytes, entropyBytes, scope);
                return Encoding.UTF8.GetString(clearBytes);
            }
            else
            {

                // TODO: dummy implementation, implement alternative implementation for non-windows
                var bytes = Convert.FromBase64String(encryptedText);
                return Encoding.UTF8.GetString(bytes.Reverse().ToArray());
            }
        }
    }
}
