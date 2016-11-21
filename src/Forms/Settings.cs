using Certify.Management;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Certify.Forms
{
    public partial class Settings : Form
    {
        public Settings()
        {
            InitializeComponent();

            chkAutoCheckForUpdates.Checked = Properties.Settings.Default.CheckForUpdatesAtStartup;
            chkAnalytics.Checked = Properties.Settings.Default.EnableAppTelematics;
            chkShowOnlyStartedWebsites.Checked = Properties.Settings.Default.ShowOnlyStartedWebsites;
        }

        public void SaveSettings()
        {
            Properties.Settings.Default.CheckForUpdatesAtStartup = chkAutoCheckForUpdates.Checked;
            Properties.Settings.Default.EnableAppTelematics = chkAnalytics.Checked;
            Properties.Settings.Default.ShowOnlyStartedWebsites = chkShowOnlyStartedWebsites.Checked;

            Properties.Settings.Default.Save();
        }

        private void label1_Click(object sender, EventArgs e)
        {
        }

        private void btnLockdown_Click(object sender, EventArgs e)
        {
            var prompt = MessageBox.Show("This will create/update system-wide registry keys disabling some known insecure SSL protocols and ciphers. Do you wish to continue?", Properties.Resources.AppName, MessageBoxButtons.YesNo);

            if (prompt == DialogResult.Yes)
            {
                var iisManager = new IISManager();
                try
                {
                    iisManager.PerformSSLProtocolLockdown();
                    MessageBox.Show("Registry changes applied. You should restart this machine for changes to take effect.");
                }
                catch (Exception)
                {
                    MessageBox.Show("Sorry, the registry changes failed to apply. You may not have the required permissions.");
                }
            }
        }
    }
}