using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Core.Management.Challenges
{
    public class LibcloudDNSProvider : IDnsProvider
    {
        private ILog _log;
        private string _pythonPath = "";
        private Dictionary<string, string> _credentials;

        public LibcloudDNSProvider(Dictionary<string, string> credentials)
        {
            _credentials = credentials;
            _pythonPath = Certify.Management.Util.GetAppDataFolder("python-embedded") + "\\python.exe";
        }

        public int PropagationDelaySeconds => 60;

        public string ProviderId => "DNS01.Apache.Libcloud";

        public string ProviderTitle => "Apache Libcloud";

        public string ProviderDescription => "Multi provider DNS API";

        public string ProviderHelpUrl => "";

        public List<ProviderParameter> ProviderParameters => new List<ProviderParameter>();

        public async Task<ActionResult> Test()
        {
            // test connection and credentials
            try
            {
                var zones = await this.GetZones();

                if (zones != null && zones.Any())
                {
                    return new ActionResult { IsSuccess = true, Message = "Test Completed OK." };
                }
                else
                {
                    return new ActionResult { IsSuccess = true, Message = "Test completed, but no zones returned." };
                }
            }
            catch (Exception exp)
            {
                return new ActionResult { IsSuccess = true, Message = $"Test Failed: {exp.Message}" };
            }
        }

        public async Task<ActionResult> CreateRecord(DnsRecord request)
        {
            // for a given managed site configuration, attempt to complete the required challenge by
            // creating the required TXT record

            // run script dns_helper_init.py -p <providername> -c <user,pwd> -d <domain> -n <record
            // name> -v <record value>

            string providerSpecificConfig = "ROUTE53";
            string credentialsString = String.Join(",", _credentials);

            // var config = providerDetails.Config.Split(';');
            //get our driver type
            //   providerSpecificConfig = config.First(c => c.StartsWith("Driver")).Replace("Driver=", "");

            // Run python helper, specifying driver to use
            var helperResult = RunPythonScript($"dns_helper_util.py -p {providerSpecificConfig} -c {_credentials} -d {request.TargetDomainName} -n {request.RecordName} -v {request.RecordValue}");

            if (helperResult.IsSuccess)
            {
                // test - wait for DNS changes
                await Task.Delay(5000);

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

        public async Task<ActionResult> DeleteRecord(DnsRecord request)
        { 
            throw new NotImplementedException();
        }

        public Task<List<DnsZone>> GetZones()
        {
            throw new NotImplementedException();
        }

        public async Task<bool> InitProvider(ILog log)
        {
            _log = log;
            return await Task.FromResult(true);
        }

        private ActionResult RunPythonScript(string args)
        {
            try
            {
                var start = new ProcessStartInfo();

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

                return new ActionResult
                {
                    IsSuccess = !failEncountered,
                    Message = msg
                };
            }
            catch (Exception exp)
            {
                return new ActionResult
                {
                    IsSuccess = false,
                    Message = exp.Message
                };
            }
        }
    }
}
