using System;
using System.Threading.Tasks;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Plugins;
using Certify.Providers.Internal;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        private TelemetryManager _tc = null;
        private ICertifyClient _certifyClient = null;
        private Preferences _prefs = new();
        private ILicensingManager _licensingManager = new LicensingManager();

        public CertifyCLI(bool useNamedPipe = false)
        {
            var configManager = new SharedUtils.ServiceConfigManager();

            Shared.ServerConnection connection = null;

            if (useNamedPipe)
            {
                connection = new Shared.ServerConnection(configManager.GetServiceConfig())
                {
                    DisplayName = "(local named pipe)",
                    Mode = Shared.NamedPipeConnection.ConnectionMode
                };
            }

            _certifyClient = new CertifyServiceClient(configManager, connection);
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
                return await Task.FromResult("--- (Could not connect to Certify Management Agent service, check service started.)");
            }
        }

        private string GetAppWebsiteURL() => Certify.Locales.ConfigResources.AppWebsiteURL;

        private void InitTelematics()
        {
#if ENABLE_TELEMETRY
            if (IsTelematicsEnabled())
            {
                _tc = new TelemetryManager(GetInstrumentationKey());
                _tc.TrackEvent("StartCLI");
            }
#endif
        }

        internal async Task ShowVersion(bool versionOnly = false)
        {
            var version = await GetAppVersion();

            if (versionOnly)
            {
                System.Console.WriteLine(version.Replace("\"", ""));
                return;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("Certify Certificate Manager - CLI Certify.Core v" + version.Replace("\"", ""));
                Console.ForegroundColor = ConsoleColor.White;
                System.Console.WriteLine("For more information see " + GetAppWebsiteURL());
                System.Console.WriteLine("");
            }
        }

        internal void ShowACMEInfo()
        {
            System.Console.WriteLine("");
            Console.ForegroundColor = ConsoleColor.White;
        }

        internal void ShowSettings()
        {
            var output = JsonConvert.SerializeObject(_prefs, Formatting.Indented);
            System.Console.WriteLine(output);
        }

        internal void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("Usage: certify <command> \n");
            System.Console.WriteLine("--pipe : connect to the local service over a named pipe instead of http (can also be set via CERTIFY_CLIENT_MODE=namedpipe)");
            System.Console.WriteLine("certify renew : renew certificates for all auto renewed managed sites");
            System.Console.WriteLine("certify deploy \"<managedcert id>\" \"<task id>\" : run a specific deployment task for the given managed certificate");
            System.Console.WriteLine("certify list : list managed certificates");
            System.Console.WriteLine("certify diag : check existing ssl bindings and managed certificate integrity");
            System.Console.WriteLine("certify importcsv : import managed certificates from a CSV file.");
            System.Console.WriteLine("certify add <managed cert id or new> <domain1;domain2> : add domains to a managed cert using the default validation, use --perform-request to immediately attempt cert request");
            System.Console.WriteLine("certify remove <managed cert id> <domain1;domain2> : remove domains from managed cert, use --perform-request to immediately attempt cert request");
            System.Console.WriteLine("certify acmeaccount add  <ACME CA ID> <your contact email> <optional EAB key id> <optional EAB Key> : add a new ACME account");
            System.Console.WriteLine("certify acmeaccount list : list registered acme accounts");
            System.Console.WriteLine("certify ca list : list available certificate authorities");
            System.Console.WriteLine("certify ca setpreferred <CA ID|any> : set the preferred certificate authority, or use 'any' to clear the preference");
            System.Console.WriteLine("certify settings : show current instance settings");
            System.Console.WriteLine("certify credential store <unique storage key GUID> <title> <type id> <secret> : for advanced automation use, stores or updates a stored credential");
            System.Console.WriteLine("certify credential list : list current stored credential summary information");
            System.Console.WriteLine("certify license check : check license status");
            System.Console.WriteLine("certify license activate <email address> <key> : activate your Certify The Web install using your license key");
            System.Console.WriteLine("certify license deactivate <email address> : deactivate your Certify The Web install");
            System.Console.WriteLine("certify hub join <url of mgmt hub API> <client id> <client secret>> : join instance to a management hub");
            System.Console.WriteLine("certify backup export <directory or full filename> <encryption secret> : export a backup file (auto-named if a directory) using the given secret password for encryption.");
            System.Console.WriteLine("certify backup import preview <full filename> <encryption secret> : import a backup file using the given secret password for encryption. 'preview' is optional and is used to test a backup without importing anything.");
            System.Console.WriteLine("\n\n");
            System.Console.WriteLine("For help, see the docs at https://docs.certifytheweb.com");
        }
    }
}
