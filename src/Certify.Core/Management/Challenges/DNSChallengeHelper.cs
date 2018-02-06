using Certify.Management;
using Certify.Models;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.Core.Management.Challenges
{
    public enum DNSChallengeHandlerType
    {
        MANUAL = 1,
        CUSTOM_SCRIPT = 2,
        PYTHON_HELPER = 3
    }

    public class ChallengeHelperResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
    }

    public class DNSChallengeHelper
    {
        private string _pythonPath = "";

        public DNSChallengeHelper()
        {
            _pythonPath = Certify.Management.Util.GetAppDataFolder("python-embedded") + "\\python.exe";
        }

        public async Task<ChallengeHelperResult> CompleteDNSChallenge(ManagedSite managedsite, string domain, string txtRecordName, string txtRecordValue)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            // if provider is python based

            // get stored credentials, for passing as arguments to script

            // run script dns_helper_init.py -p <providername> -c <user,pwd> -d <domain> -n <record
            // name> -v <record value>
            string providerType = "PythonHelper";
            string providerSpecificConfig = "ROUTE53";
            string credentials = "user,pwd";

            var credentialsManager = new CredentialsManager();

            if (!String.IsNullOrEmpty(managedsite.RequestConfig.ChallengeProvider))
            {
                var providerDetails = Models.Config.ChallengeProviders.Providers.FirstOrDefault(p => p.Id == managedsite.RequestConfig.ChallengeProvider);
                var config = providerDetails.Config.Split(';');
                //get our driver type
                providerSpecificConfig = config.First(c => c.StartsWith("Driver")).Replace("Driver=", "");
            }

            if (!String.IsNullOrEmpty(managedsite.RequestConfig.ChallengeCredentialKey))
            {
                // decode credentials string array
                string credentialsJson = await credentialsManager.GetUnlockedCredential(managedsite.RequestConfig.ChallengeCredentialKey);
                string[] credentialArray = JsonConvert.DeserializeObject<string[]>(credentialsJson);
                credentials = String.Join(",", credentialArray);
            }

            // Run python helper, specifying driver to use
            var helperResult = RunPythonScript($"dns_helper_util.py -p {providerSpecificConfig} -c {credentials} -d {domain} -n {txtRecordName} -v {txtRecordValue}");

            if (helperResult.IsSuccess)
            {
                // test - wait for DNS changes
                await Task.Delay(15000);

                // do our own txt record query before proceeding with challenge completion
                /*
                int attempts = 3;
                bool recordCheckedOK = false;
                var networkUtil = new NetworkUtils(false);

                while (attempts > 0 && !recordCheckedOK)
                {
                    recordCheckedOK = networkUtil.CheckDNSRecordTXT(domain, txtRecordName, txtRecordValue);
                    attempts--;
                    if (!recordCheckedOK)
                    {
                        await Task.Delay(1000); // hold on a sec
                    }
                }
                */
                return helperResult;
            }
            else
            {
                return helperResult;
            }
        }

        private ChallengeHelperResult RunPythonScript(string args)
        {
            try
            {
                var start = new ProcessStartInfo();
                //FIXME: remove hard coded test paths
                start.FileName = _pythonPath;

                start.WorkingDirectory = Environment.CurrentDirectory + "\\Scripts\\Python\\";
                start.Arguments = string.Format("{0}", args);
                start.UseShellExecute = false;
                start.RedirectStandardOutput = true;

                bool failEncountered = false;
                string msg = "";
                using (Process process = Process.Start(start))
                {
                    using (StreamReader reader = process.StandardOutput)
                    {
                        string result = reader.ReadToEnd();
                        if (result.ToLower().Contains("failed") || result.ToLower().Contains("error"))
                        {
                            failEncountered = true;
                            msg = result;
                        }
                        Debug.Write(result);
                    }
                }

                return new ChallengeHelperResult
                {
                    IsSuccess = !failEncountered,
                    Message = msg
                };
            }
            catch (Exception exp)
            {
                return new ChallengeHelperResult
                {
                    IsSuccess = false,
                    Message = exp.Message
                };
            }
        }
    }
}