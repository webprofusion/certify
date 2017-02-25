using Certify.Forms;
using Certify.Management;
using Microsoft.ApplicationInsights;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ACMESharp.Vault.Model;
using ACMESharp.Vault.Providers;

namespace Certify
{
    internal enum ImageList
    {
        Vault = 0,
        Globe = 1,
        Cert = 2,
        Person = 3
    }

    public partial class MainForm : Form
    {
        internal VaultManager VaultManager = null;
        private TelemetryClient tc = null;
        private bool requirePowershell = false;

        public MainForm()
        {
            InitializeComponent();

            this.Text = Properties.Resources.LongAppName;
            if (Properties.Settings.Default.CheckForUpdatesAtStartup)
            {
                PerformCheckForUpdates(silent: true);
            }
        }

        private void InitAI()
        {
            if (Properties.Settings.Default.EnableAppTelematics)
            {
                tc = new TelemetryClient();
                tc.Context.InstrumentationKey = Properties.Resources.AIInstrumentationKey;
                tc.InstrumentationKey = Properties.Resources.AIInstrumentationKey;

                // Set session data:

                tc.Context.Session.Id = Guid.NewGuid().ToString();
                tc.Context.Device.OperatingSystem = Environment.OSVersion.ToString();
            }
            else
            {
                tc = null;
            }
        }

        internal void TrackPageView(string pageName)
        {
            tc?.TrackPageView(pageName);
        }

        private void fileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void ReloadVault()
        {
            VaultManager.ReloadVaultConfig();
        }

        private void ShowCertificateRequestDialog()
        {
            try
            {
                using (var form = new CertRequestDialog(VaultManager))
                {
                    form.ShowDialog();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            ReloadVault();
        }

        private void ShowSettingsDialog()
        {
            try
            {
                using (var form = new Certify.Forms.Settings())
                {
                    var result = form.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        form.SaveSettings();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ShowContactRegistrationDialog()
        {
            using (var form = new ContactRegistration(VaultManager))
            {
                var result = form.ShowDialog();
            }
            ReloadVault();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            InitAI();
            TrackPageView(nameof(MainForm));

            if (this.requirePowershell)
            {
                var powershellVersion = PowershellManager.GetPowershellVersion();
                if (powershellVersion < 4)
                {
                    MessageBox.Show("This application requires PowerShell version 4.0 or higher. You can update it using the latest Windows Management Framework download from Microsoft.", Properties.Resources.AppName);

                    Application.Exit();
                    return;
                }
            }

            this.VaultManager = new VaultManager(Properties.Settings.Default.VaultPath, LocalDiskVault.VAULT);

            if (Properties.Settings.Default.ShowBetaWarning)
            {
                // this.lblGettingStarted.Text += "\r\n\r\n" + Properties.Resources.BetaWarning;
            }

            var vaultInfo = VaultManager.GetVaultConfig();

            if (vaultInfo != null && vaultInfo.Registrations == null)
            {
                //got an existing vault. If no contact registrations setup, prompt to add one
                var promptResult = MessageBox.Show("No certificate contact registrations have been setup. Would you like to add a new contact now? ", "Create New Contact?", MessageBoxButtons.YesNo);

                if (promptResult == DialogResult.Yes)
                {
                    ShowContactRegistrationDialog();
                }
            }
            ReloadVault();
        }

        private void reloadVaultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ReloadVault();
            //TODO: inform vault control of changes
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var aboutDialog = new AboutDialog())
            {
                aboutDialog.ShowDialog();
            }
        }

        private async void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await PerformCheckForUpdates(silent: false);
        }

        private async Task<bool> PerformCheckForUpdates(bool silent = false)
        {
            var updateCheck = await new Util().CheckForUpdates(Application.ProductVersion);

            if (updateCheck != null)
            {
                if (updateCheck.IsNewerVersion)
                {
                    var gotoDownload = MessageBox.Show(updateCheck.Message.Body + "\r\nVisit download page now?", Properties.Resources.AppName, MessageBoxButtons.YesNo);
                    if (gotoDownload == DialogResult.Yes)
                    {
                        ProcessStartInfo sInfo = new ProcessStartInfo(Properties.Resources.AppWebsiteURL);
                        Process.Start(sInfo);
                    }
                }
                else
                {
                    if (!silent)
                    {
                        MessageBox.Show(Properties.Resources.UpdateCheckLatestVersion, Properties.Resources.AppName);
                    }
                }
            }
            return true;
        }

        private void changeVaultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LocateOrCreateVault(false);
        }

        private bool LocateOrCreateVault(bool useDefaultCreationPath = true)
        {
            var promptResult = MessageBox.Show("Do you want to create a new vault? Choose 'No' to browse to an existing vault folder.", "Change Vault", MessageBoxButtons.YesNoCancel);

            if (promptResult == DialogResult.Yes)
            {
                var useProductionPrompt = MessageBox.Show("Do you want to use the live LetsEncrypt.org API? Choose 'No' to use the staging (test) API for this vault.", Properties.Resources.AppName, MessageBoxButtons.YesNo);

                bool useStagingAPI = false;
                if (useProductionPrompt == DialogResult.No)
                {
                    useStagingAPI = true;
                }

                var useDefaultPath = MessageBox.Show("Do you want to use the default vault path of " + Properties.Settings.Default.DefaultVaultPath + "?", Properties.Resources.AppName, MessageBoxButtons.YesNo);
                if (useDefaultPath == DialogResult.Yes)
                {
                    useDefaultCreationPath = true;
                }

                string newVaultPath = Properties.Settings.Default.DefaultVaultPath;
                if (!useDefaultCreationPath)
                {
                    //browse to a follder to store vault in
                    var d = new FolderBrowserDialog();
                    var dialogResult = d.ShowDialog();
                    if (dialogResult == DialogResult.OK)
                    {
                        newVaultPath = d.SelectedPath;
                    }
                    else
                    {
                        return false;
                    }
                }

                if (Directory.Exists(newVaultPath) && Directory.GetFiles(newVaultPath).Any())
                {
                    MessageBox.Show("You need to create the vault in a new empty folder. The specified folder is not empty.");
                    return false;
                }

                if (VaultManager.InitVault(useStagingAPI))
                {
                    //vault created

                    ReloadVault();
                    return true;
                }
            }

            if (promptResult == DialogResult.No)
            {
                //folder picker browse to vault folder
                var d = new FolderBrowserDialog();
                var dialogResult = d.ShowDialog();
                if (dialogResult == DialogResult.OK)
                {
                    if (VaultManager.IsValidVaultPath(d.SelectedPath))
                    {
                        VaultManager = new VaultManager(d.SelectedPath, LocalDiskVault.VAULT);
                        ReloadVault();
                        return true;
                    }
                    else
                    {
                        MessageBox.Show("The selected folder is not a valid vault.");
                        return false;
                    }
                }
            }

            return false;
        }

        private void toolStripButtonNewContact_Click(object sender, EventArgs e)
        {
            ShowContactRegistrationDialog();
        }

        private void contactRegistrationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowContactRegistrationDialog();
        }

        private void toolStripButtonNewCertificate_Click(object sender, EventArgs e)
        {
            ShowCertificateRequestDialog();
        }

        private void domainCertificateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowCertificateRequestDialog();
        }

        private void websiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo(Properties.Resources.AppWebsiteURL);
            Process.Start(sInfo);
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowSettingsDialog();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            if (tc != null)
            {
                tc.Flush(); // only for desktop apps

                // Allow time for flushing:
                System.Threading.Thread.Sleep(1000);
            }
            base.OnClosing(e);
        }

        private void cleanupVaultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            VaultManager.CleanupVault();
            ReloadVault();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
        }
    }
}