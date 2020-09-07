using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Certify.UI.Utils
{
    public class Helpers
    {
        public static void LaunchBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url));
               
            }
            catch (Exception)
            {
                MessageBox.Show("Could not start a browser for " + url);
            }
        }
    }
}
