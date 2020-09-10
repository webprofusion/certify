using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Certify.Locales;
using Certify.Models;
using Certify.Models.Config;
using Microsoft.ApplicationInsights;
using Microsoft.Win32;

namespace Certify.Management
{
    public class Util
    {

        /// <summary>
        /// check for problems which could affect app use
        /// </summary>
        /// <returns>  </returns>
        public static async Task<List<ActionResult>> PerformAppDiagnostics(string ntpServer)
        {
            var results = new List<ActionResult>();

            var tempFilePath = "";
            var tempFolder = Path.GetTempPath();

            // if current user can create temp files, attempt to create a 1MB temp file, detect if it fails
            if (!string.IsNullOrEmpty(tempFolder))
            {
                try
                {
                    tempFilePath = Path.GetTempFileName();

                    using (var fs = new FileStream(tempFilePath, FileMode.Open))
                    {
                        fs.Seek(1024 * 1024, SeekOrigin.Begin);
                        fs.WriteByte(0);
                        fs.Close();
                    }

                    File.Delete(tempFilePath);
                    results.Add(new ActionResult { IsSuccess = true, Message = $"Created test temp file OK." });
                }
                catch (Exception exp)
                {
                    results.Add(new ActionResult { IsSuccess = false, Message = $"Could not create a temp file ({tempFilePath}). Windows has a limit of 65535 files in the temp folder ({tempFolder}). Clear temp files before proceeding. {exp.Message}" });
                }
            }

            // check free disk space
            try
            {
                var cDrive = new DriveInfo("c");
                if (cDrive.IsReady)
                {
                    var freeSpaceBytes = cDrive.AvailableFreeSpace;

                    // Check disk has at least 128MB free
                    if (freeSpaceBytes < (1024L * 1024 * 128))
                    {
                        results.Add(new ActionResult { IsSuccess = false, Message = $"Drive C: has less than 128MB of disk space free. The application may not run correctly." });
                    }
                    else
                    {
                        results.Add(new ActionResult { IsSuccess = true, Message = $"Drive C: has more than 128MB of disk space free." });
                    }
                }
            }
            catch (Exception)
            {
                results.Add(new ActionResult { IsSuccess = false, Message = $"Could not check how much disk space is left on drive C:" });
            }

            // check internet time service, unless ntpServer pref set to ""

            if (ntpServer != "")
            {
                var timeResult = await CheckTimeServer(ntpServer);
                if (timeResult != null)
                {
                    var diff = timeResult - DateTime.Now;
                    if (Math.Abs(diff.Value.TotalSeconds) > 50)
                    {
                        results.Add(new ActionResult { IsSuccess = false, Message = $"Note: Your system time does not appear to be in sync with an internet time service, this can result in certificate request errors." });
                    }
                    else
                    {
                        results.Add(new ActionResult { IsSuccess = true, Message = $"System time is correct." });
                    }

                }
                else
                {
                    results.Add(new ActionResult { IsSuccess = false, Message = $"Note: Could not confirm system time sync using NTP server ({ntpServer}). Ensure system time is correct to avoid certificate request errors." });
                }
            }


            // check if FIPS is enabled
            try
            {
                _ = System.Security.Cryptography.SHA256.Create();
            }
            catch (Exception)
            {
                // if creating managed SHA256 fails may be FIPS validation
                results.Add(new ActionResult { IsSuccess = false, Message = $"Your system cannot create a SHA256 Cryptography instance. You may have inadvertently have FIPS enabled, which prevents the use of some standard cryptographic functions in .Net - features such as verifying app updates will not work. " });
            }


            // check powershell version
            string subkey = @"SOFTWARE\Microsoft\PowerShell\3\PowerShellEngine";
            bool isPSAvailable = true;
            try
            {
                using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(subkey))
                {
                    var vals = (ndpKey.GetValue("PSCompatibleVersion") as string).Split(',');
                    if (!vals.Any(v => v.Trim() == "5.0"))
                    {
                        isPSAvailable = false;
                    }
                }
            }
            catch
            {
                isPSAvailable = false;
            }

            if (!isPSAvailable)
            {
                results.Add(new ActionResult { IsSuccess = false, Message = $"PowerShell 5.0 or higher is required for some functionality and does not appear to be available on this system. See https://docs.microsoft.com/en-us/powershell/scripting/windows-powershell/install/windows-powershell-system-requirements" });
            }
            else
            {
                results.Add(new ActionResult { IsSuccess = true, Message = $"PowerShell 5.0 or higher is available." });
            }


            return results;
        }

        public static void SetSupportedTLSVersions() => ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;


        public static string GetAppDataFolder(string subFolder = null)
        {
            var parts = new List<string>()
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Models.SharedConstants.APPDATASUBFOLDER
            };

            if (subFolder != null)
            {
                parts.Add(subFolder);
            }

            var path = Path.Combine(parts.ToArray());

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
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

        public static string GetUserAgent()
        {
            var versionName = "Certify/" + GetAppVersion().ToString();
            return $"{versionName} (Windows; {Environment.OSVersion}) ";
        }

        public static Version GetAppVersion()
        {
            // returns the version of Certify.Shared
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            var v = assembly.GetName().Version;
            return v;
        }

        public async Task<UpdateCheck> CheckForUpdates()
        {
            var v = GetAppVersion();
            return await CheckForUpdates(v);
        }

        public async Task<UpdateCheck> CheckForUpdates(Version appVersion) => await CheckForUpdates(appVersion.ToString());

        public async Task<UpdateCheck> CheckForUpdates(string appVersion)
        {
            //get app version
            try
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", Util.GetUserAgent());

                var response = await client.GetAsync(Models.API.Config.APIBaseURI + "update?version=" + appVersion);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
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

                    var checkResult = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateCheck>(json);
                    return CompareVersions(appVersion, checkResult);
                }

                return new UpdateCheck { IsNewerVersion = false, InstalledVersion = AppVersion.FromString(appVersion) };
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

            checkResult.InstalledVersion = AppVersion.FromString(appVersion);

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
                    // system probably has FIPS enabled and doesn't support standard SHA256
                    return null;
                }

                var checksum = sha.ComputeHash(bufferedStream);
                return BitConverter.ToString(checksum).Replace("-", string.Empty).ToLower();
            }
        }



        public bool VerifyUpdateFile(string tempFile, string expectedHash, bool throwOnDeviation = true)
        {
            var performCertValidation = true;

            var signatureVerified = false;

            if (performCertValidation)
            {
                // check digital signature
                var wintrustSignatureVerified = Security.WinTrust.WinTrust.VerifyEmbeddedSignature(tempFile);

                //get verified signed file cert
                var cert = CertificateManager.GetFileCertificate(tempFile);

                //ensure cert subject
                if (!(cert != null && wintrustSignatureVerified && cert.SubjectName.Name.StartsWith("CN=Webprofusion Pty Ltd, O=Webprofusion Pty Ltd")))
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
            }

            //verify file SHA256
            string computedSHA256 = null;
            using (Stream stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.None, 8192, true))
            {
                computedSHA256 = GetFileSHA256(stream);
            }

            var hashVerified = false;

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

            if (hashVerified && (!performCertValidation || (performCertValidation && signatureVerified)))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static string GetUserLocalAppDataFolder()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Models.SharedConstants.APPDATASUBFOLDER);
            if (!System.IO.Directory.Exists(path))
            {
                System.IO.Directory.CreateDirectory(path);
            }
            return path;
        }


        /// <summary>
        /// Create a local app data folder. This method will throw an exception if any IO operations fail.
        /// </summary>
        /// <param name="folder">subfolder to create</param>
        /// <returns></returns>
        public string CreateLocalAppDataPath(string folder)
        {
            // create a new temp folder under our Local User %APPDATA% folder for access by our current user
            var appData = GetUserLocalAppDataFolder();

            var destPath = Path.Combine(appData, folder);

            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            return destPath;
        }

        public async Task<UpdateCheck> DownloadUpdate()
        {
            var result = await CheckForUpdates();

#if DEBUG
            result.IsNewerVersion = true;
#endif
            if (result.IsNewerVersion)
            {
                string updatePath;

                try
                {
                    updatePath = CreateLocalAppDataPath("updates");
                }
                catch (Exception)
                {
                    throw new Exception("Update failed to download. Could not create temp folder under %APPDATA%");
                }

                //https://github.com/dotnet/corefx/issues/6849
                var tempFile = Path.Combine(new string[] { updatePath, "CertifySSL_" + result.Version.ToString() + "_Setup.tmp" });
                var setupFile = tempFile.Replace(".tmp", ".exe");

                var downloadVerified = false;
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
                        using (var client = new HttpClient())
                        {
                            client.DefaultRequestHeaders.Add("User-Agent", Util.GetUserAgent());

                            using (var response = client.GetAsync(result.Message.DownloadFileURL, HttpCompletionOption.ResponseHeadersRead).Result)
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
                        if (File.Exists(setupFile))
                        {
                            File.Delete(setupFile); //delete existing file
                        }

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
        /// <returns>  </returns>
        public static string GetDotNetVersion()
        {
            const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";

            using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey))
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
            if (releaseKey >= 528040)
            {
                return "4.8 or later";
            }

            if (releaseKey >= 461808)
            {
                return "4.7.2";
            }

            if (releaseKey >= 461308)
            {
                return "4.7.1";
            }

            if (releaseKey >= 460798)
            {
                return "4.7";
            }

            if (releaseKey >= 460798)
            {
                return "4.7";
            }

            if (releaseKey >= 394802)
            {
                return "4.6.2";
            }

            if (releaseKey >= 394254)
            {
                return "4.6.1";
            }

            if (releaseKey >= 393295)
            {
                return "4.6";
            }

            if (releaseKey >= 379893)
            {
                return "4.5.2";
            }

            if (releaseKey >= 378675)
            {
                return "4.5.1";
            }

            if (releaseKey >= 378389)
            {
                return "4.5";
            }

            // This code should never execute. A non-null release key should mean that 4.5 or later
            // is installed.
            return "No 4.5 or later version detected";
        }


        public static string ToUrlSafeBase64String(byte[] data)
        {
            var s = Convert.ToBase64String(data);
            s = s.Split('=')[0]; // Remove any trailing '='s
            s = s.Replace('+', '-'); // 62nd char of encoding
            s = s.Replace('/', '_'); // 63rd char of encoding
            return s;
        }

        public static string ToUrlSafeBase64String(string val)
        {
            var bytes = System.Text.UTF8Encoding.UTF8.GetBytes(val);
            return ToUrlSafeBase64String(bytes);
        }

        public static async Task<DateTime?> CheckTimeServer(string ntpServer = "pool.ntp.org")
        {
            // https://stackoverflow.com/questions/1193955/how-to-query-an-ntp-server-using-c

            try
            {

                const int DaysTo1900 = 1900 * 365 + 95; // 95 = offset for leap-years etc.
                const long TicksPerSecond = 10000000L;
                const long TicksPerDay = 24 * 60 * 60 * TicksPerSecond;
                const long TicksTo1900 = DaysTo1900 * TicksPerDay;

                var ntpData = new byte[48];
                ntpData[0] = 0x1B; // LeapIndicator = 0 (no warning), VersionNum = 3 (IPv4 only), Mode = 3 (Client Mode)

                var addresses = Dns.GetHostEntry(ntpServer).AddressList;
                var ipEndPoint = new IPEndPoint(addresses[0], 123);
                var pingDuration = Stopwatch.GetTimestamp(); // temp access (JIT-Compiler need some time at first call)

                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    await socket.ConnectAsync(ipEndPoint);
                    socket.ReceiveTimeout = 5000;
                    socket.Send(ntpData);
                    pingDuration = Stopwatch.GetTimestamp(); // after Send-Method to reduce WinSocket API-Call time

                    socket.Receive(ntpData);
                    pingDuration = Stopwatch.GetTimestamp() - pingDuration;
                }

                var pingTicks = pingDuration * TicksPerSecond / Stopwatch.Frequency;

                // optional: display response-time
                // Console.WriteLine("{0:N2} ms", new TimeSpan(pingTicks).TotalMilliseconds);

                var intPart = (long)ntpData[40] << 24 | (long)ntpData[41] << 16 | (long)ntpData[42] << 8 | ntpData[43];
                var fractPart = (long)ntpData[44] << 24 | (long)ntpData[45] << 16 | (long)ntpData[46] << 8 | ntpData[47];
                var netTicks = intPart * TicksPerSecond + (fractPart * TicksPerSecond >> 32);

                var networkDateTime = new DateTime(TicksTo1900 + netTicks + pingTicks / 2);

                return networkDateTime.ToLocalTime(); // without ToLocalTime() = faster
            }
            catch
            {
                // fail
                return null;
            }
        }
    }
}
