using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Microsoft.ApplicationInsights;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        private TelemetryClient _tc = null;
        private ICertifyClient _certifyClient = null;
        private Preferences _prefs = new Preferences();
        private PluginManager _pluginManager { get; set; }

        public CertifyCLI()
        {
            _certifyClient = new CertifyServiceClient(new SharedUtils.ServiceConfigManager());
        }

        public async Task<bool> IsServiceAvailable()
        {
            var isAvailable = false;

            try
            {
                await _certifyClient.GetAppVersion();
                isAvailable = true;
            }
            catch (Exception exp)
            {
                System.Console.WriteLine(exp.ToString());
                isAvailable = false;
            }
            return isAvailable;
        }

        private void InitPlugins()
        {
            if (_pluginManager == null)
            {
                _pluginManager = new Management.PluginManager();

                _pluginManager.LoadPlugins(new List<string> { "Licensing" });
            }
        }

        public async Task LoadPreferences() => _prefs = await _certifyClient.GetPreferences();

        private bool IsTelematicsEnabled() => _prefs.EnableAppTelematics;

        private string GetInstrumentationKey() => Certify.Locales.ConfigResources.AIInstrumentationKey;

        private async Task<string> GetAppVersion()
        {
            try
            {
                return await _certifyClient.GetAppVersion();
            }
            catch (Exception)
            {
                return await Task.FromResult("--- (Service Not Started)");
            }
        }

        private string GetAppWebsiteURL() => Certify.Locales.ConfigResources.AppWebsiteURL;

        private void InitTelematics()
        {
            if (IsTelematicsEnabled())
            {
                _tc = new TelemetryClient();
                _tc.Context.InstrumentationKey = GetInstrumentationKey();
                _tc.InstrumentationKey = GetInstrumentationKey();

                // Set session data:

                _tc.Context.Session.Id = Guid.NewGuid().ToString();
                _tc.Context.Component.Version = GetAppVersion().Result;
                _tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
                _tc.TrackEvent("StartCLI");
            }
        }

        internal void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Certify SSL Manager - CLI Certify.Core v" + GetAppVersion().Result.Replace("\"", ""));
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see " + GetAppWebsiteURL());
            System.Console.WriteLine("");
        }

        internal void ShowACMEInfo()
        {
            /*
                        var certifyManager = new CertifyManager();
                        string vaultInfo = certifyManager.GetVaultSummary();
                        string acmeInfo = certifyManager.GetAcmeSummary();

                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        System.Console.WriteLine("ACME API: " + acmeInfo);
                        System.Console.WriteLine("ACMESharp Vault: " + vaultInfo);
            */
            System.Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.White;
        }

        internal void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("Usage: certify <command> \n");
            System.Console.WriteLine("certify renew : renew certificates for all auto renewed managed sites");
            System.Console.WriteLine("certify deploy \"<ManagedCertName>\" \"<TaskName>\" : run a specific deployment task for the given managed certificate");
            System.Console.WriteLine("certify list : list managed certificates and current running/not running status in IIS");
            System.Console.WriteLine("certify diag : check existing ssl bindings and managed certificate integrity");
            System.Console.WriteLine("certify importcsv : import managed certificates from a CSV file.");
            System.Console.WriteLine("certify add <managed cert id or new> <domain1;domain2> : add domains to a managed cert using the default validation, use --perform-request to immediately attempt cert request");
            System.Console.WriteLine("certify remove <managed cert id> <domain1;domain2> : remove domains from managed cert, use --perform-request to immediately attempt cert request");
            System.Console.WriteLine("\n\n");
            System.Console.WriteLine("For help, see the docs at https://docs.certifytheweb.com");

        }
    }
}
