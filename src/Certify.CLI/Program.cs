using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Certify.Management;

namespace Certify.CLI
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowVersion();
                ShowHelp();

                var p = new Program();
                p.ListIISSites();
                return 1;
            }

            return 0;
        }
        static void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            System.Console.WriteLine("Certify SSL Manager - CLI v1.0.0");
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see https://certify.webprofusion.com");
            System.Console.WriteLine("");

        }

        static void ShowHelp()
        {
            System.Console.WriteLine("-r --renew : renew certificates for all active sites");
        }

        void ListIISSites()
        {
            var iisManager = new IISManager();
            var siteList = iisManager.GetSiteList();

            if (siteList == null || siteList.Count == 0)
            {
                System.Console.WriteLine("No IIS Sites configured or access denied.");
            } else
            {
                foreach (var s in siteList)
                {
                    System.Console.WriteLine(s.SiteName);
                }
            }
            
        }
    }
}
