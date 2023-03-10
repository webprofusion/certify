using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        private TelemetryManager _tc = null;
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

                _pluginManager.LoadPlugins(new List<string> { PluginManager.PLUGINS_LICENSING });
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
                _tc = new TelemetryManager(GetInstrumentationKey());
                _tc.TrackEvent("StartCLI");
            }
        }

        internal void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Certify Certificate Manager - CLI Certify.Core v" + GetAppVersion().Result.Replace("\"", ""));
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see " + GetAppWebsiteURL());
            System.Console.WriteLine("");
        }

        internal void ShowACMEInfo()
        {
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
            System.Console.WriteLine("certify acmeaccount add  <ACME CA ID> <your contact email> <optional EAB key id> <optional EAB Key> : add a new ACME account");
            System.Console.WriteLine("certify acmeaccount list : list registered acme accounts");
            System.Console.WriteLine("certify activate <email address> <key> : activate your Certify The Web install using your license key");
            System.Console.WriteLine("certify backup export <directory or full filename> <encryption secret> : export a backup file (autonamed if a directory) using the given secret password for encryption.");
            System.Console.WriteLine("certify backup import preview <full filename> <encryption secret> : import a backup file using the given secret password for encryption. 'preview' is optional and us used to test a backup without importing anything.");
            System.Console.WriteLine("\n\n");
            System.Console.WriteLine("For help, see the docs at https://docs.certifytheweb.com");

        }
    }
}
