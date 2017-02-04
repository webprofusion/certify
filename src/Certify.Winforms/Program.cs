using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Certify
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var winId = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(winId);

            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show("Sorry, you need to run " + Properties.Resources.AppName + " as an administrator in order to allow files to be copied to protected IIS folders (wwwroot, etc).");
                Application.Exit();
                return;
            };

            Application.Run(new MainForm());
        }
    }
}