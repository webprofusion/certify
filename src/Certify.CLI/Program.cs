using System;
using System.Linq;
using System.Threading.Tasks;

namespace Certify.CLI
{
    internal class Program
    {
        const int MAX_CHALLENGE_SERVER_RUNTIME = 1000 * 60 * 30;  // Allow up to 30 mins of run time for the challenge server (normall run time is

        private static int Main(string[] args)
        {
            var p = new CertifyCLI();

            if (args.Length == 0)
            {
                p.ShowHelp();
                p.ShowACMEInfo();
            }
            else
            {
                if (args.Contains("storeserverconfig", StringComparer.InvariantCultureIgnoreCase))
                {
                    SharedUtils.ServiceConfigManager.StoreCurrentAppServiceConfig();
                    return 0;
                }

                if (args.Contains("httpchallenge", StringComparer.InvariantCultureIgnoreCase))
                {
                    return StartHttpChallengeServer(args);
                }

                p.ShowVersion();

                if (!p.IsServiceAvailable().Result)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine("Certify SSL Manager service not started.");
                    Console.ForegroundColor = ConsoleColor.White;
                    return -1;
                }

                Task.Run(async () =>
                {
                    await p.LoadPreferences();
                });

                p.ShowACMEInfo();

                if (args.Contains("renew", StringComparer.InvariantCultureIgnoreCase))
                {
                    // perform auto renew all
                    var renewalTask = p.PerformAutoRenew();
                    renewalTask.ConfigureAwait(true);
                    renewalTask.Wait();
                }

                if (args.Contains("list", StringComparer.InvariantCultureIgnoreCase))
                {
                    //list managed sites and status
                    p.ListManagedCertificates();
                }

                if (args.Contains("diag", StringComparer.InvariantCultureIgnoreCase))
                {
                    p.RunCertDiagnostics();
                }

         

                if (args.Contains("importcsv", StringComparer.InvariantCultureIgnoreCase))
                {
                    var importTask = p.ImportCSV(args);
                    importTask.ConfigureAwait(true);
                    importTask.Wait();
                }
            }

#if DEBUG
            Console.ReadKey();
#endif
            return 0;
        }

        private static int StartHttpChallengeServer(string[] args)
        {
            System.Console.WriteLine("Starting Certify Http Challenge Server");

            if (args.Length < 2)
            {
                System.Console.WriteLine("Error: control key arguments required e.g.  certify httpchallenge keys=CONTROLKEY,CHECKKEY");
                return -1;
            }
            //syntax: certify httpchallenge keys=CONTROLKEY,CHECKKEY

            var keys = args[1].Replace("keys=", "").Split(',');

            var task = Task.Run(async () =>
            {
                // start an http challenge server
                var challengeServer = new Core.Management.Challenges.HttpChallengeServer();
                var config = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

                if (!challengeServer.Start(config.HttpChallengeServerPort, controlKey: keys[0], checkKey: keys[1]))
                {
                    // failed to start http challenge server
                    return -1;
                }

                // wait for server to stop

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (challengeServer.IsRunning() && stopwatch.ElapsedMilliseconds < MAX_CHALLENGE_SERVER_RUNTIME )
                {
                    await Task.Delay(500);
                }

                // if we exceeded the allowed time for challenge server to run, ensure it is closed and quit
                if (challengeServer.IsRunning())
                {
                    challengeServer.Stop();
                }
                return 0;
            });

            return task.Result;
        }
    }
}
