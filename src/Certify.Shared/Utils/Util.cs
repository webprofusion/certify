using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Certify.Shared;
using Microsoft.ApplicationInsights;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace Certify.Management
{
    public class Util
    {
        public const string APPDATASUBFOLDER = "Certify";

        /// <summary>
        /// check for problems which could affect app use 
        /// </summary>
        /// <returns></returns>
        public static Task<List<ActionResult>> PerformAppDiagnostics()
        {
            var results = new List<ActionResult>();

            string tempPath = "";
            string tempFolder = Path.GetTempPath();

            // attempt to create a 1MB temp file, detect if it fails
            try
            {
                tempPath = Path.GetTempFileName();

                FileStream fs = new FileStream(tempPath, FileMode.Open);
                fs.Seek(1024 * 1024, SeekOrigin.Begin);
                fs.WriteByte(0);
                fs.Close();

                File.Delete(tempPath);
                results.Add(new ActionResult { IsSuccess = true, Message = $"Created test temp file OK." });
            }
            catch (Exception exp)
            {
                results.Add(new ActionResult { IsSuccess = false, Message = $"Could not create a temp file ({tempPath}). Windows has a limit of 65535 files in the temp folder ({tempFolder}). Clear temp files  before proceeding. {exp.Message}" });
            }
            return Task.FromResult(results);
        }

        public static void SetSupportedTLSVersions()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
        }

        public static string GetAppDataFolder(string subFolder = null)
        {
            var parts = new List<string>()
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                APPDATASUBFOLDER
            };

            if (subFolder != null) parts.Add(subFolder);

            var path = Path.Combine(parts.ToArray());

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }


        /// <summary>
        /// Get default or saved service config settings
        /// </summary>
        /// <returns></returns>
        public static ServiceConfig GetAppServiceConfig()
        {
            var serviceConfig = new ServiceConfig();

            var appDataPath = GetAppDataFolder();
            var serviceConfigFile = appDataPath + "\\serviceconfig.json";
#if DEBUG
            serviceConfigFile = appDataPath + "\\serviceconfig.debug.json";
#endif
            if (File.Exists(serviceConfigFile))
            {
                serviceConfig = JsonConvert.DeserializeObject<ServiceConfig>(File.ReadAllText(serviceConfigFile));
               
            }
            return serviceConfig;
        }


        /// <summary>
        /// Stored updated config for app service
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public static bool SetAppServicePort(int port)
        {
            var appDataPath = GetAppDataFolder();
            var serviceConfigFile = appDataPath + "\\serviceconfig.json";
#if DEBUG
            serviceConfigFile = appDataPath + "\\serviceconfig.debug.json";
#endif
            try
            {
                ServiceConfig settings = new ServiceConfig();

                if (File.Exists(serviceConfigFile))
                {
                    settings = JsonConvert.DeserializeObject<ServiceConfig>(File.ReadAllText(serviceConfigFile));
                }

                settings.Port = port;

                File.WriteAllText(serviceConfigFile, JsonConvert.SerializeObject(settings));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public TelemetryClient InitTelemetry()
        {
            var tc = new TelemetryClient();

            tc.Context.InstrumentationKey = ConfigResources.AIInstrumentationKey;
            tc.InstrumentationKey = ConfigResources.AIInstrumentationKey;

            // Set session data:

            tc.Context.Session.Id = Guid.NewGuid().ToString();
            tc.Context.Component.Version = GetAppVersion().ToString();
            tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();

            return tc;
        }

        public Version GetAppVersion()
        {
            // returns the version of Certify.Shared
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();

            var v = assembly.GetName().Version;
            return v;
        }

        public async Task<UpdateCheck> CheckForUpdates()
        {
            var v = GetAppVersion();
            return await this.CheckForUpdates(v);
        }

        public async Task<UpdateCheck> CheckForUpdates(Version appVersion)
        {
            return await this.CheckForUpdates(appVersion.ToString());
        }

        public async Task<UpdateCheck> CheckForUpdates(string appVersion)
        {
            //get app version
            try
            {
                HttpClient client = new HttpClient();
                var response = await client.GetAsync(ConfigResources.APIBaseURI + "update?version=" + appVersion);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    /*json = @"{
                         'version': {
                             'major': 2,
                             'minor': 0,
                             'patch': 3
                                                 },
                           'message': {
                                                     'body': 'There is an awesome update available.',
                             'downloadPageURL': 'https://certify.webprofusion.com',
                             'releaseNotesURL': 'https://certify.webprofusion.com/home/changelog',
                             'isMandatory': true
                           }
                     }";*/

                    UpdateCheck checkResult = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateCheck>(json);
                    return CompareVersions(appVersion, checkResult);
                }

                return new UpdateCheck { IsNewerVersion = false };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static UpdateCheck CompareVersions(string appVersion, UpdateCheck checkResult)
        {
            checkResult.IsNewerVersion = AppVersion.IsOtherVersionNewer(AppVersion.FromString(appVersion), checkResult.Version);

            // check for mandatory updates
            if (checkResult.Message != null && checkResult.Message.MandatoryBelowVersion != null)
            {
                checkResult.MustUpdate = AppVersion.IsOtherVersionNewer(AppVersion.FromString(appVersion), checkResult.Message.MandatoryBelowVersion);
            }

            return checkResult;
        }

        public string GetFileSHA256(Stream stream)
        {
            using (var bufferedStream = new BufferedStream(stream, 1024 * 32))
            {
                SHA256 sha = null;

                try
                {
                    sha = System.Security.Cryptography.SHA256.Create();
                }
                catch (System.InvalidOperationException)
                {
                    // if creating managed SHA256 fails may be FIPS validation, try SHA256Cng
                    sha = (SHA256)new System.Security.Cryptography.SHA256Cng();
                }

                byte[] checksum = sha.ComputeHash(bufferedStream);
                return BitConverter.ToString(checksum).Replace("-", String.Empty).ToLower();
            }
        }

        /// <summary>
        /// Gets the certificate the file is signed with. 
        /// </summary>
        /// <param name="filename">
        /// The path of the signed file from which to create the X.509 certificate.
        /// </param>
        /// <returns> The certificate the file is signed with </returns>
        public X509Certificate2 GetFileCertificate(string filename)
        {
            // https://blogs.msdn.microsoft.com/windowsmobile/2006/05/17/programmatically-checking-the-authenticode-signature-on-a-file/
            X509Certificate2 cert = null;
            try
            {
                cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filename));

                X509Chain chain = new X509Chain();
                X509ChainPolicy chainPolicy = new X509ChainPolicy()
                {
                    RevocationMode = X509RevocationMode.Online,
                    RevocationFlag = X509RevocationFlag.EntireChain
                };
                chain.ChainPolicy = chainPolicy;

                if (chain.Build(cert))
                {
                    foreach (X509ChainElement chainElement in chain.ChainElements)
                    {
                        foreach (X509ChainStatus chainStatus in chainElement.ChainElementStatus)
                        {
                            System.Diagnostics.Debug.WriteLine(chainStatus.StatusInformation);
                        }
                    }
                }
                else
                {
                    throw new Exception("Could not build cert chain");
                }
            }
            catch (CryptographicException e)
            {
                Console.WriteLine("Error {0} : {1}", e.GetType(), e.Message);
                Console.WriteLine("Couldn't parse the certificate." +
                                  "Be sure it is an X.509 certificate");
                return null;
            }
            return cert;
        }

        public bool VerifyUpdateFile(string tempFile, string expectedHash, bool throwOnDeviation = true)
        {
            bool signatureVerified = false;
            bool hashVerified = false;

            //get verified signed file cert
            var cert = GetFileCertificate(tempFile);

            //ensure cert subject
            if (!(cert != null && cert.SubjectName.Name.StartsWith("CN=Webprofusion Pty Ltd, O=Webprofusion Pty Ltd")))
            {
                if (throwOnDeviation)
                {
                    throw new Exception("Downloaded file failed digital signature check.");
                }
                else
                {
                    return false;
                }
            }
            else
            {
                signatureVerified = true;
            }

            //verify file SHA256
            string computedSHA256 = null;
            using (Stream stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
            {
                computedSHA256 = GetFileSHA256(stream);
            }

            if (expectedHash.ToLower() == computedSHA256)
            {
                hashVerified = true;
            }
            else
            {
                if (throwOnDeviation)
                {
                    throw new Exception("Downloaded file failed SHA256 hash check");
                }
                else
                {
                    return false;
                }
            }

            if (hashVerified && signatureVerified)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public async Task<UpdateCheck> DownloadUpdate()
        {
            string pathname = Path.GetTempPath();

            var result = await CheckForUpdates();

            if (result.IsNewerVersion)
            {
                HttpClient client = new HttpClient();

                //https://github.com/dotnet/corefx/issues/6849
                var tempFile = Path.Combine(new string[] { pathname, "CertifySSL_" + result.Version.ToString() + "_Setup.tmp" });
                var setupFile = tempFile.Replace(".tmp", ".exe");

                bool downloadVerified = false;
                if (File.Exists(setupFile))
                {
                    // file already downloaded, see if it's already valid
                    if (VerifyUpdateFile(setupFile, result.Message.SHA256, throwOnDeviation: false))
                    {
                        downloadVerified = true;
                    }
                }

                if (!downloadVerified)
                {
                    // download and verify new setup
                    try
                    {
                        using (HttpResponseMessage response = client.GetAsync(result.Message.DownloadFileURL, HttpCompletionOption.ResponseHeadersRead).Result)
                        {
                            response.EnsureSuccessStatusCode();

                            using (Stream contentStream = await response.Content.ReadAsStreamAsync(), fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                var totalRead = 0L;
                                var totalReads = 0L;
                                var buffer = new byte[8192];
                                var isMoreToRead = true;

                                do
                                {
                                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                    if (read == 0)
                                    {
                                        isMoreToRead = false;
                                    }
                                    else
                                    {
                                        await fileStream.WriteAsync(buffer, 0, read);

                                        totalRead += read;
                                        totalReads += 1;

                                        if (totalReads % 512 == 0)
                                        {
                                            Console.WriteLine(string.Format("total bytes downloaded so far: {0:n0}", totalRead));
                                        }
                                    }
                                }
                                while (isMoreToRead);
                                fileStream.Close();
                            }
                        }
                    }
                    catch (Exception exp)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to download update: " + exp.ToString());
                        downloadVerified = false;
                    }
                    // verify temp file
                    if (!downloadVerified && VerifyUpdateFile(tempFile, result.Message.SHA256, throwOnDeviation: true))
                    {
                        downloadVerified = true;
                        if (File.Exists(setupFile)) File.Delete(setupFile); //delete existing file
                        File.Move(tempFile, setupFile); // final setup file
                    }
                }

                if (downloadVerified)
                {
                    // setup is ready to run
                    result.UpdateFilePath = setupFile;
                }
            }
            return result;
        }

        /// <summary>
        /// From https://docs.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed#net_d 
        /// </summary>
        /// <returns></returns>
        public static string GetDotNetVersion()
        {
            const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";

            using (RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey))
            {
                if (ndpKey != null && ndpKey.GetValue("Release") != null)
                {
                    return GetDotNetVersion((int)ndpKey.GetValue("Release"));
                }
                else
                {
                    return ".NET Version not detected.";
                }
            }
        }

        private static string GetDotNetVersion(int releaseKey)
        {
            if (releaseKey >= 460798) return "4.7 or later";
            if (releaseKey >= 394802) return "4.6.2";
            if (releaseKey >= 394254) return "4.6.1";
            if (releaseKey >= 393295) return "4.6";
            if (releaseKey >= 379893) return "4.5.2";
            if (releaseKey >= 378675) return "4.5.1";
            if (releaseKey >= 378389) return "4.5";

            // This code should never execute. A non-null release key should mean that 4.5 or later
            // is installed.
            return "No 4.5 or later version detected";
        }
    }
}
