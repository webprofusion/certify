using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Certify.Models.Plugins;
using Certify.Models.Shared;
using Newtonsoft.Json;
using Registration.Core.Models.Shared;

namespace Certify.Providers.Internal
{
    public static class StringCipher
    {
        // This constant is used to determine the keysize of the encryption algorithm in bits. We
        // divide this by 8 within the code below to get the equivalent number of bytes.
        private const int Keysize = 256;

        // This constant determines the number of iterations for the password bytes generation function.
        private const int DerivationIterations = 1000;

        public static string Encrypt(string plainText, string passPhrase, bool requireFIPS = false, int preferredBlockSize = 128)
        {

            if (!requireFIPS)
            {
                try
                {
                    var test = new RijndaelManaged();
                    test.Dispose();
                }
                catch (Exception)
                {
                    requireFIPS = true;
                }
            }

            if (!requireFIPS)
            {
                // Salt and IV is randomly generated each time, but is prepended to encrypted cipher
                // text so that the same Salt and IV values can be used when decrypting.

                var blockSize = preferredBlockSize;

                var saltStringBytes = blockSize == 256 ? Generate256BitsOfRandomEntropy() : Generate128BitsOfRandomEntropy();
                var ivStringBytes = blockSize == 256 ? Generate256BitsOfRandomEntropy() : Generate128BitsOfRandomEntropy();
                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

                using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
                {
                    var keyBytes = password.GetBytes(Keysize / 8);

                    using (var symmetricKey = new RijndaelManaged())
                    {
                        symmetricKey.BlockSize = blockSize; // block size must be 128 on .net core/.net5 etc
                        symmetricKey.Mode = CipherMode.CBC;
                        symmetricKey.Padding = PaddingMode.PKCS7;
                        using (var encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes))
                        {
                            using (var memoryStream = new MemoryStream())
                            {
                                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                                {
                                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                                    cryptoStream.FlushFinalBlock();
                                    // Create the final bytes as a concatenation of the random salt
                                    // bytes, the random iv bytes and the cipher bytes.
                                    var cipherTextBytes = saltStringBytes;
                                    cipherTextBytes = cipherTextBytes.Concat(ivStringBytes).ToArray();
                                    cipherTextBytes = cipherTextBytes.Concat(memoryStream.ToArray()).ToArray();
                                    memoryStream.Close();
                                    cryptoStream.Close();
                                    return Convert.ToBase64String(cipherTextBytes);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Salt and IV is randomly generated each time, but is prepended to encrypted cipher
                // text so that the same Salt and IV values can be used when decrypting.
                var saltStringBytes = Generate128BitsOfRandomEntropy();
                var ivStringBytes = Generate128BitsOfRandomEntropy();
                var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

                using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
                {
                    var keyBytes = password.GetBytes(Keysize / 8);

                    using (var symmetricKey = Aes.Create())
                    {
                        symmetricKey.BlockSize = 128;
                        symmetricKey.Mode = CipherMode.CBC;
                        symmetricKey.Padding = PaddingMode.PKCS7;
                        using (var encryptor = symmetricKey.CreateEncryptor(keyBytes, ivStringBytes))
                        {
                            using (var memoryStream = new MemoryStream())
                            {
                                using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                                {
                                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                                    cryptoStream.FlushFinalBlock();
                                    // Create the final bytes as a concatenation of the random salt
                                    // bytes, the random iv bytes and the cipher bytes.
                                    var cipherTextBytes = saltStringBytes;
                                    cipherTextBytes = cipherTextBytes.Concat(ivStringBytes).ToArray();
                                    cipherTextBytes = cipherTextBytes.Concat(memoryStream.ToArray()).ToArray();
                                    memoryStream.Close();
                                    cryptoStream.Close();
                                    return Convert.ToBase64String(cipherTextBytes);
                                }
                            }
                        }
                    }
                }
            }
        }

        public static string Decrypt(string cipherText, string passPhrase, bool requireFIPS = false, int preferredBlockSize = 128)
        {
            if (!requireFIPS)
            {
                try
                {
                    var test = new RijndaelManaged();
                    test.Dispose();
                }
                catch (Exception)
                {
                    requireFIPS = true;
                }
            }

            if (!requireFIPS)
            {
                // Get the complete stream of bytes that represent: [32 bytes of Salt] + [32 bytes of IV]
                // + [n bytes of CipherText]
                var cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText);
                // Get the saltbytes by extracting the first 32 bytes from the supplied cipherText bytes.
                var saltStringBytes = cipherTextBytesWithSaltAndIv.Take(preferredBlockSize / 8).ToArray();
                // Get the IV bytes by extracting the next 32 bytes from the supplied cipherText bytes.
                var ivStringBytes = cipherTextBytesWithSaltAndIv.Skip(preferredBlockSize / 8).Take(preferredBlockSize / 8).ToArray();
                // Get the actual cipher text bytes by removing the first 64 bytes from the cipherText string.
                var cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip(preferredBlockSize / 8 * 2).Take(cipherTextBytesWithSaltAndIv.Length - preferredBlockSize / 8 * 2).ToArray();

                using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
                {
                    var keyBytes = password.GetBytes(Keysize / 8);

                    using (var symmetricKey = new RijndaelManaged())
                    {
                        symmetricKey.BlockSize = preferredBlockSize; // different versions of .net support different block sizes
                        symmetricKey.Mode = CipherMode.CBC;
                        symmetricKey.Padding = PaddingMode.PKCS7;

                        using (var decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes))
                        {
                            using (var memoryStream = new MemoryStream(cipherTextBytes))
                            {
                                using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                                {
                                    var decryptedBytes = new byte[cipherTextBytes.Length];

                                    var totalRead = 0;
                                    while (totalRead < cipherTextBytes.Length)
                                    {
                                        var bytesRead = cryptoStream.Read(decryptedBytes, totalRead, cipherTextBytes.Length - totalRead);
                                        if (bytesRead == 0)
                                        {
                                            break;
                                        }

                                        totalRead += bytesRead;
                                    }

                                    memoryStream.Close();
                                    cryptoStream.Close();
                                    return Encoding.UTF8.GetString(decryptedBytes, 0, totalRead);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // Get the complete stream of bytes that represent: [32 bytes of Salt] + [32 bytes of IV]
                // + [n bytes of CipherText]
                var cipherTextBytesWithSaltAndIv = Convert.FromBase64String(cipherText);
                // Get the saltbytes by extracting the first 32 bytes from the supplied cipherText bytes.
                var saltStringBytes = cipherTextBytesWithSaltAndIv.Take(Keysize / 16).ToArray();
                // Get the IV bytes by extracting the next 32 bytes from the supplied cipherText bytes.
                var ivStringBytes = cipherTextBytesWithSaltAndIv.Skip(Keysize / 16).Take(Keysize / 16).ToArray();
                // Get the actual cipher text bytes by removing the first 64 bytes from the cipherText string.
                var cipherTextBytes = cipherTextBytesWithSaltAndIv.Skip(Keysize / 16 * 2).Take(cipherTextBytesWithSaltAndIv.Length - Keysize / 16 * 2).ToArray();

                using (var password = new Rfc2898DeriveBytes(passPhrase, saltStringBytes, DerivationIterations))
                {
                    var keyBytes = password.GetBytes(Keysize / 8);

                    //failed to create crypto algorithm, try Aes instead
                    using (var symmetricKey = Aes.Create())
                    {
                        symmetricKey.BlockSize = 128;
                        symmetricKey.Mode = CipherMode.CBC;
                        symmetricKey.Padding = PaddingMode.PKCS7;
                        using (var decryptor = symmetricKey.CreateDecryptor(keyBytes, ivStringBytes))
                        {
                            using (var memoryStream = new MemoryStream(cipherTextBytes))
                            {
                                using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                                {
                                    var decryptedBytes = new byte[cipherTextBytes.Length];

                                    var totalRead = 0;
                                    while (totalRead < cipherTextBytes.Length)
                                    {
                                        var bytesRead = cryptoStream.Read(decryptedBytes, totalRead, cipherTextBytes.Length - totalRead);
                                        if (bytesRead == 0)
                                        {
                                            break;
                                        }

                                        totalRead += bytesRead;
                                    }

                                    memoryStream.Close();
                                    cryptoStream.Close();
                                    return Encoding.UTF8.GetString(decryptedBytes, 0, totalRead);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static byte[] Generate256BitsOfRandomEntropy()
        {
            var randomBytes = new byte[32]; // 32 Bytes will give us 256 bits.
            using (var rngCsp = new RNGCryptoServiceProvider())
            {
                // Fill the array with cryptographically secure random bytes.
                rngCsp.GetBytes(randomBytes);
            }

            return randomBytes;
        }

        private static byte[] Generate128BitsOfRandomEntropy()
        {
            var randomBytes = new byte[16]; // 16 Bytes will give us 128 bits.
            using (var rngCsp = new RNGCryptoServiceProvider())
            {
                // Fill the array with cryptographically secure random bytes.
                rngCsp.GetBytes(randomBytes);
            }

            return randomBytes;
        }
    }

    public class LicensingManager : ILicensingManager
    {
        private bool _enableLog = false;

        public LicensingManager()
        {
#if DEBUG
            _enableLog = true;
#else
      if (Environment.GetEnvironmentVariable("CERTIFY_ENABLE_LICENSING_LOG") == "true")
            {
                _enableLog = true;
            }
#endif

        }

        public void Log(string message)
        {
            if (_enableLog)
            {
                var logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "certify", "logs", "licensing.log");
                if (!File.Exists(logFile))
                {
                    File.CreateText(logFile).Close();
                }

                File.AppendAllText(logFile, message + "\r\n");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(message + "\r\n");
            }
        }

        public async Task<LicenseCheckResult> Validate(int productTypeId, string email, string key)
        {
            Log("----------------");
            Log("Validating key:" + key);
            Log("Validating email:" + email);
            using (var client = new HttpClient())
            {
                Log("Created HttpClient");
                SetSupportedTLSVersions(_enableLog);
                Log("Set TLS");

                var jsonRequest = JsonConvert.SerializeObject(
                    new
                    {
                        ProductTypeId = productTypeId,
                        Email = email,
                        Key = key
                    });

                Log("Data: " + JsonConvert.SerializeObject(jsonRequest));

                var data = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var url = Models.API.Config.APIBaseURI + "license/validate";

                Log($"Posting to: {url}");

                try
                {
                    var response = await client.PostAsync(url, data);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResult = await response.Content.ReadAsStringAsync();

                        Log($"Received: {response.ToString()}");
                        Log($"Received result: {jsonResult}");
                        //validate given license key has not expired

                        try
                        {
                            var result = JsonConvert.DeserializeObject<LicenseCheckResult>(jsonResult);
                            return result;
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to deserialize: {ex}");
                            throw;
                        }
                    }
                    else
                    {
                        Log($"submission failed: {response}");
                        return new LicenseCheckResult { IsValid = false, StatusCode = "OFFLINE", ValidationMessage = "There was a problem validating this key. Please ensure you have the latest app version and try again later." };
                    }
                }
                catch (Exception ex)
                {
                    Log($"Post to API failed: {ex}");
                    throw;
                }
            }
        }

        public static void SetSupportedTLSVersions(bool enableLog)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12 | (SecurityProtocolType)1228;
                return;
            }
            catch
            {
                if (enableLog)
                {
                    new LicensingManager().Log("ServicePointManager.SecurityProtocol : Tls 1.3 is not a supported protocol");
                }
            }

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            if (enableLog)
            {
                new LicensingManager().Log("ServicePointManager.SecurityProtocol : Tls 1.2 is the highest supported protocol");
            }
        }

        public async Task<LicenseKeyInstallResult> RegisterInstall(int productTypeId, string email, string key, RegisteredInstance instance)
        {
            Log("Registering Install based on key:" + key);

            instance.MachineName = Environment.MachineName;
            instance.OS = Environment.OSVersion.ToString();

            using (var client = new HttpClient())
            {

                SetSupportedTLSVersions(_enableLog);

                var jsonRequest = JsonConvert.SerializeObject(
                    new
                    {
                        ProductTypeId = productTypeId,
                        Email = email,
                        Key = key,
                        Instance = instance
                    });

                Log("Instance: " + JsonConvert.SerializeObject(jsonRequest));

                var data = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(Models.API.Config.APIBaseURI + "license/install", data);
                if (response.IsSuccessStatusCode)
                {
                    var jsonResult = await response.Content.ReadAsStringAsync();

                    Log("Install Response: " + JsonConvert.SerializeObject(jsonRequest));
                    //validate given license key has not expired
                    var result = JsonConvert.DeserializeObject<LicenseKeyInstallResult>(jsonResult);

                    return result;
                }
                else
                {
                    Log("Install Response Failed: " + JsonConvert.SerializeObject(response.ToString()));
                    return new LicenseKeyInstallResult { IsSuccess = false, Message = "There was a problem validating this key. Please ensure you have the latest app version and try again later." };
                }
            }
        }

        public async Task<bool> DeactivateInstall(int productTypeId, string settingsPath, string email, RegisteredInstance instance)
        {
            Log("Deactivating Install");
            var install = GetLicenseKeyInstallResult(productTypeId, settingsPath);
            if (install != null)
            {
                instance.MachineName = Environment.MachineName;
                instance.OS = Environment.OSVersion.ToString();

                using (var client = new HttpClient())
                {

                    SetSupportedTLSVersions(_enableLog);

                    var jsonRequest = JsonConvert.SerializeObject(
                        new
                        {
                            ProductTypeId = productTypeId,
                            Email = email,
                            install.UsageToken,
                            Instance = instance
                        });

                    var data = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(Models.API.Config.APIBaseURI + "license/uninstall", data);
                    if (response.IsSuccessStatusCode)
                    {
                        DeleteLicenseKeyInstall(productTypeId, settingsPath);
                        return true;
                    }
                }
            }

            Log("Deactivating Install Failed");
            return false;
        }

        public bool FinaliseInstall(int productTypeId, LicenseKeyInstallResult result, string settingsPath)
        {
            var json = JsonConvert.SerializeObject(result);

            Log("Finalising Install: " + json);
            Log("Machine Name: " + Environment.MachineName);
            var data = StringCipher.Encrypt(json, Environment.MachineName);

            Log("Cipher: " + data);

            var licenseFilePath = Path.Combine(settingsPath, "reg_" + productTypeId);
            try
            {
                File.WriteAllText(licenseFilePath, data);
                Log($"License file written");
            }
            catch (Exception exp)
            {
                Log($"Failed to write license file {licenseFilePath}: " + exp.ToString());
                return false;
            }

            return true;
        }

        private bool DeleteLicenseKeyInstall(int productTypeId, string settingsPath)
        {
            try
            {
                var filePath = Path.Combine(settingsPath, "reg_" + productTypeId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private LicenseKeyInstallResult GetLicenseKeyInstallResult(int productTypeId, string settingsPath)
        {
            var filePath = Path.Combine(settingsPath, "reg_" + productTypeId);
            if (File.Exists(filePath))
            {
                try
                {
                    var data = File.ReadAllText(filePath);

                    var json = StringCipher.Decrypt(data, Environment.MachineName, preferredBlockSize: 128);

                    var licenseResult = JsonConvert.DeserializeObject<LicenseKeyInstallResult>(json);

                    return licenseResult;
                }
                catch (Exception)
                {
                    try
                    {
                        // earlier version may have used a block size of 256 which is now unsupported in .net 5 upwards. If this succeeds, re-encrypt for future
                        var data = File.ReadAllText(filePath);

                        var json = StringCipher.Decrypt(data, Environment.MachineName, preferredBlockSize: 256);

                        var licenseResult = JsonConvert.DeserializeObject<LicenseKeyInstallResult>(json);

                        if (licenseResult != null)
                        {
                            // migrate encrypted file to supported block size
                            FinaliseInstall(productTypeId, licenseResult, settingsPath);
                        }

                        return licenseResult;

                    }
                    catch (Exception exp)
                    {
                        Log("GetLicenseKeyInstallResult: " + exp.ToString());
                    }

                    return null;
                }
            }
            else
            {
                Log($"GetLicenseKeyInstallResult: {filePath} does not exist");
                return null;
            }
        }

        public bool IsInstallRegistered(int productTypeId, string settingsPath)
        {
            var result = GetLicenseKeyInstallResult(productTypeId, settingsPath);
            if (result?.IsSuccess == true)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public DateTime? GetInstallDate(string settingsPath)
        {
            /// get oldest file in setting path
            var fileInfo = new DirectoryInfo(settingsPath)
                .GetFileSystemInfos()
                .OrderBy(fi => fi.CreationTime)
                .FirstOrDefault();

            return fileInfo?.CreationTime;
        }

        public async Task<bool> IsInstallActive(int productTypeId, string settingsPath)
        {

            var result = GetLicenseKeyInstallResult(productTypeId, settingsPath);
            if (result != null)
            {

                try
                {
                    var id = result.UsageToken;

                    using (var client = new HttpClient())
                    {

                        SetSupportedTLSVersions(_enableLog);

                        var response = await client.GetAsync(Models.API.Config.APIBaseURI + "license/check?id=" + id);
                        if (response.IsSuccessStatusCode)
                        {
                            return JsonConvert.DeserializeObject<bool>(await response.Content.ReadAsStringAsync());
                        }
                        else
                        {
                            //default to true if request fails
                            return true;
                        }
                    }
                }
                catch (Exception)
                {
                    // failed to query status
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        public LicenseCheckResult GetCurrentLicense(int productTypeId, string settingsPath)
        {

            var result = GetLicenseKeyInstallResult(productTypeId, settingsPath);
            if (result?.IsSuccess == true)
            {
                return new LicenseCheckResult { IsValid = false, StatusCode = "ACTIVE" };
            }
            else
            {
                return new LicenseCheckResult { IsValid = false, StatusCode = "UNLICENSED" };
            }
        }
    }
}
