using System;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.CLI
{
    internal class Program
    {
        const int MAX_CHALLENGE_SERVER_RUNTIME = 1000 * 60 * 30;  // Allow up to 30 mins of run time for the challenge server (normall run time is

        private static async Task<int> Main(string[] args)
        {
            var defaultFontColour = Console.ForegroundColor;

            var p = new CertifyCLI();

            if (args.Length == 0)
            {
                p.ShowHelp();
                p.ShowACMEInfo();
            }
            else
            {
                var command = "";

                if (args.Any())
                {
                    command = args[0].ToLower().Trim();
                }

                if (command == "storeserverconfig")
                {
                    var cfg = SharedUtils.ServiceConfigManager.GetAppServiceConfig();
                    SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(cfg);
                    return 0;
                }

                if (command == "httpchallenge")
                {
                    return await StartHttpChallengeServer(args);
                }

                p.ShowVersion();

                if (!p.IsServiceAvailable().Result)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine("Certify SSL Manager service not started.");
                    Console.ForegroundColor = defaultFontColour;
                    return -1;
                }

                await p.LoadPreferences();

                p.ShowACMEInfo();

                if (command == "renew")
                {
                    // perform auto renew all
                    await p.PerformAutoRenew(args);
                }

                if (command == "deploy")
                {
                    string managedCertId = null;
                    string taskId = null;

                    if (args.Length >= 3)
                    {
                        // got command, cert and task name
                        managedCertId = args[1].Trim();
                        taskId = args[2].Trim();
                    }
                    else if (args.Length == 2)
                    {
                        // got command and cert id, run all deployment tasks
                        managedCertId = args[1].Trim();
                    }
                    else
                    {
                        // incomplete args
                        Console.ForegroundColor = ConsoleColor.Red;
                        System.Console.WriteLine("Deploy: Missing arguments for Managed Certificate Name and Task Name");
                        Console.ForegroundColor = defaultFontColour;

                        p.ShowHelp();
                    }

                    if (managedCertId != null)
                    {
                        var result = await p.PerformDeployment(managedCertId, taskId);
                    }

                }

                if (command == "list")
                {
                    // list managed sites and status
                    p.ListManagedCertificates(args);
                }

                if (command == "diag")
                {
                    var autoFix = false;
                    var forceAutoDeploy = false;
                    var includeOcspCheck = false;

                    if (args.Contains("autofix"))
                    {
                        autoFix = true;
                    }

                    if (args.Contains("forceautodeploy"))
                    {
                        forceAutoDeploy = true;
                    }

                    if (args.Contains("checkocsp"))
                    {
                        includeOcspCheck = true;
                    }
                    await p.RunCertDiagnostics(autoFix, forceAutoDeploy, includeOcspCheck: includeOcspCheck);
                }

                if (command == "pending")
                {
                    var autoFix = false;

                    if (args.Contains("autofix"))
                    {
                        autoFix = true;
                    }

                    await p.FindPendingAuthorizations(autoFix);
                }

                if (command == "importcsv")
                {
                    await p.ImportCSV(args);
                }

                if (command == "add")
                {
                    await p.AddIdentifiers(args);
                }

                if (command == "remove")
                {
                    await p.RemoveIdentifiers(args);
                }

                if (command == "activate")
                {
                    await p.Activate(args);
                }

                if (command == "deactivate")
                {
                    await p.Deactivate(args);
                }

                if (command == "acmeaccount" && args.Contains("add"))
                {
                    await p.AddACMEAccount(args);
                }

                if (command == "acmeaccount" && args.Contains("list"))
                {
                    await p.ListACMEAccounts();
                }
            }
#if DEBUG
            System.Console.WriteLine("CLI: Completed (DEBUG)");
            Console.ReadKey();
#endif
            return 0;
        }

        private static async Task<int> StartHttpChallengeServer(string[] args)
        {
            System.Console.WriteLine("Starting Certify Http Challenge Server");

            if (args.Length < 2)
            {
                System.Console.WriteLine("Error: control key arguments required e.g.  certify httpchallenge keys=CONTROLKEY,CHECKKEY");
                return -1;
            }
            //syntax: certify httpchallenge keys=CONTROLKEY,CHECKKEY

            var keys = args[1].Replace("keys=", "").Split(',');

            // start an http challenge server
            var challengeServer = new Core.Management.Challenges.HttpChallengeServer();
            var config = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            if (!challengeServer.Start(config, controlKey: keys[0], checkKey: keys[1]))
            {
                // failed to start http challenge server
                return -1;
            }

            // wait for server to stop

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (challengeServer.IsRunning() && stopwatch.ElapsedMilliseconds < MAX_CHALLENGE_SERVER_RUNTIME)
            {
                await Task.Delay(500);
            }

            // if we exceeded the allowed time for challenge server to run, ensure it is closed and quit
            if (challengeServer.IsRunning())
            {
                challengeServer.Stop();
            }
            return 0;
        }
    }
}
