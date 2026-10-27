using System;
using System.Windows;

namespace Certify.UI.Utils
{
    public class Helpers
    {
        public static void LaunchBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

            }
            catch (Exception)
            {
                MessageBox.Show("Could not start a browser for " + url);
            }
        }

        /// <summary>
        /// True if the user changed the selection (from the open list or the keyboard), rather than the list showing the value
        /// of the item being displayed (initial binding, or another item selected for editing)
        /// </summary>
        public static bool IsUserSelectionChange(System.Windows.Controls.ComboBox list) => list.IsDropDownOpen || list.IsKeyboardFocusWithin;
    }
}
