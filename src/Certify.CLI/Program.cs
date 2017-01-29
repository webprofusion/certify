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
                p.PreviewAutoManage();
                System.Console.ReadKey();
                return 1;
            }

            return 0;
        }
        static void ShowVersion()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Certify SSL Manager - CLI v1.0.0");
            Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("For more information see https://certify.webprofusion.com");
            System.Console.WriteLine("");

        }

        static void ShowHelp()
        {
            System.Console.WriteLine("Usage: \n\n");
            System.Console.WriteLine("-h --help : show this help information");
            System.Console.WriteLine("-r --renew : renew certificates for all managed sites");
            System.Console.WriteLine("-l --list : list managed sites");
            System.Console.WriteLine("-p --preview : auto scan and preview proposed list of managed sites");
            System.Console.WriteLine("\n\n");
        }

        /// <summary>
        /// Auto scan and preview list of sites to manage
        /// </summary>
        void PreviewAutoManage()
        {
            var siteManager = new SiteManager();
            var siteList = siteManager.Preview();

            if (siteList == null || siteList.Count == 0)
            {
                System.Console.WriteLine("No Sites configured or access denied.");
            }
            else
            {
                foreach (var s in siteList)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    System.Console.WriteLine(String.Format("{0} ({1}): Create single certificate for {2} bindings: \n", s.SiteName, s.SiteType.ToString(), s.SiteBindings.Count));

                    Console.ResetColor();
                    foreach (var b in s.SiteBindings)
                    {
                        System.Console.WriteLine("\t" + b.Hostname + " \n");
                    }
                }

            }

            siteManager.StoreSettings();
        }

   
    }
}
