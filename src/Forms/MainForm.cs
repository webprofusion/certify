using ACMESharp.Vault.Model;
using ACMESharp.Vault.Providers;
using Certify.Forms;
using Certify.Management;
using Microsoft.ApplicationInsights;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        private void populateTreeView(VaultInfo vaultConfig)
        {
            if (this.treeView1.Nodes != null)
            {
                this.treeView1.Nodes.Clear();
            }

            // start off by adding a base treeview node
            TreeNode mainNode = new TreeNode();

            mainNode.Name = "Vault";
            mainNode.Text = "Vault";
            mainNode.ImageIndex = (int)ImageList.Vault;
            mainNode.SelectedImageIndex = mainNode.ImageIndex;

            this.treeView1.Nodes.Add(mainNode);

            if (vaultConfig.Identifiers != null)
            {
                var domainsNode = new TreeNode("Domains & Certificates (" + vaultConfig.Identifiers.Count + ")");

                domainsNode.ImageIndex = (int)ImageList.Globe;
                domainsNode.SelectedImageIndex = domainsNode.ImageIndex;

                foreach (var i in vaultConfig.Identifiers)
                {
                    var node = new TreeNode(i.Dns);
                    node.Tag = i;

                    node.ImageIndex = (int)ImageList.Globe;
                    node.SelectedImageIndex = node.ImageIndex;

                    if (vaultConfig.Certificates != null)
                    {
                        foreach (var c in vaultConfig.Certificates)
                        {
                            if (c.IdentifierRef == i.Id)
                            {
                                //add cert
                                var certNode = new TreeNode(c.Alias);
                                certNode.Tag = c;

                                certNode.ImageIndex = (int)ImageList.Cert;
                                certNode.SelectedImageIndex = certNode.ImageIndex;

                                node.Nodes.Add(certNode);
                            }
                        }
                    }
                    domainsNode.Nodes.Add(node);
                }

                mainNode.Nodes.Add(domainsNode);
            }

            if (vaultConfig.Registrations != null)
            {
                var contactsNode = new TreeNode("Registered Contacts (" + vaultConfig.Registrations.Count + ")");

                contactsNode.ImageIndex = (int)ImageList.Person;
                contactsNode.SelectedImageIndex = contactsNode.ImageIndex;

                foreach (var i in vaultConfig.Registrations)
                {
                    var title = i.Registration.Contacts.FirstOrDefault();
                    var node = new TreeNode(title);
                    node.Tag = i;

                    node.ImageIndex = (int)ImageList.Person;
                    node.SelectedImageIndex = node.ImageIndex;

                    contactsNode.Nodes.Add(node);
                }

                mainNode.Nodes.Add(contactsNode);
            }

            if (mainNode.Nodes.Count == 0)
            {
                mainNode.Nodes.Add("(Empty)");
            }
        }

        private void ReloadVault()
        {
            VaultManager.ReloadVaultConfig();
            var vaultInfo = VaultManager.GetVaultConfig();
            if (vaultInfo != null)
            {
                this.lblVaultLocation.Text = VaultManager.VaultFolderPath;
                this.lblAPIBaseURI.Text = vaultInfo.BaseUri;

                populateTreeView(vaultInfo);

                txtOutput.Text = VaultManager.GetActionLogSummary();

                //store setting for current vault path
                if (Properties.Settings.Default.VaultPath != VaultManager.VaultFolderPath)
                {
                    Properties.Settings.Default.VaultPath = VaultManager.VaultFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        public void DataGridShowIdentifiers()
        {
            var identifiers = VaultManager.GetIdentifiers();

            if (identifiers != null)
            {
                //refresh
                /*this.SetupGridViewForIdentifiers();

                var list = new BindingList<IdentifierInfo>(identifiers);
                var source = new BindingSource(list, null);
                this.dataGridView1.DataSource = source;
                */
            }
        }

        /*private void SetupGridViewForIdentifiers()
        {
            this.dataGridView1.DataSource = null;
            this.dataGridView1.AutoGenerateColumns = false;

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Alias", Name = "Alias" });
            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Label", Name = "Label" });
            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Dns", Name = "Dns" });
        }*/

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

            this.VaultManager = new VaultManager(Properties.Settings.Default.VaultPath, LocalDiskVault.VAULT);

            if (!VaultManager.IsCompatiblePowershell())
            {
                MessageBox.Show("This application requires PowerShell version 4.0 or higher. You can update it using the latest Windows Management Framework download from Microsoft.", Properties.Resources.AppName);
                Application.Exit();
                return;
            }

            if (Properties.Settings.Default.ShowBetaWarning)
            {
                MessageBox.Show(Properties.Resources.BetaWarning, Properties.Resources.AppName);
            }

            /*if (this.VaultManager.IsValidVaultPath(Properties.Settings.Default.VaultPath))
            {
                this.ReloadVault();
            }*/
            var vaultInfo = VaultManager.GetVaultConfig();
            /*if (vaultInfo == null)
            {
                LocateOrCreateVault(useDefaultCreationPath: false);

                vaultInfo = VaultManager.GetVaultConfig();
            }
            else
            {
                lblGettingStarted.Text = Properties.Resources.GettingStartedExistingVault;
            }
            */

            if (vaultInfo != null && vaultInfo.Registrations == null)
            {
                //got an existing vault. If no contact registrations setup, prompt to add one
                var promptResult = MessageBox.Show("No certificate contact registrations have been setup. Add a new contact now? ", "Create New Contact?", MessageBoxButtons.YesNo);

                if (promptResult == DialogResult.Yes)
                {
                    ShowContactRegistrationDialog();
                }
            }
            ReloadVault();
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeView1.SelectedNode != null)
            {
                object selectedItem = treeView1.SelectedNode.Tag;
                if (selectedItem is RegistrationInfo)
                {
                    var i = (RegistrationInfo)selectedItem;
                    panelItemInfo.Controls.Clear();
                    var infoControl = new Forms.Controls.Details.RegistrationInfoDetails(this);
                    infoControl.Populate(i);
                    panelItemInfo.Controls.Add(infoControl);
                }

                if (selectedItem is CertificateInfo)
                {
                    var i = (CertificateInfo)selectedItem;
                    panelItemInfo.Controls.Clear();
                    var infoControl = new Forms.Controls.Details.CertificateDetails(this);
                    infoControl.Populate(i);
                    panelItemInfo.Controls.Add(infoControl);
                }

                if (selectedItem is IdentifierInfo)
                {
                    var i = (IdentifierInfo)selectedItem;
                    panelItemInfo.Controls.Clear();
                    var infoControl = new Forms.Controls.Details.SimpleDetails(this);
                    infoControl.Populate(i.Dns + " : " + i.Id);
                    panelItemInfo.Controls.Add(infoControl);
                }
            }
        }

        public bool DeleteVaultItem(object item)
        {
            if (item is RegistrationInfo)
            {
                var dialogResult = MessageBox.Show("Are you sure you wish to delete this item?", "Delete Vault Item", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    bool success = this.VaultManager.DeleteRegistrationInfo(((RegistrationInfo)item).Id);
                    return success;
                }
            }

            return false;
        }

        private void reloadVaultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ReloadVault();
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
            var promptResult = MessageBox.Show("Do you want to create a new vault? Choose No to browse to an existing Vault folder.", "Change Vault", MessageBoxButtons.YesNoCancel);

            if (promptResult == DialogResult.Yes)
            {
                var useProductionPrompt = MessageBox.Show("Do you want to use the Live LetsEncrypt.org API? Choose No to use the staging (test) API for this vault.", Properties.Resources.AppName, MessageBoxButtons.YesNo);

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
                        MessageBox.Show("The selected folder is not a valid Vault.");
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

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //user has clicked delete in tree view context menu
            var node = treeView1.SelectedNode;
            if (node.Tag is IdentifierInfo)
            {
                var i = (IdentifierInfo)node.Tag;
                VaultManager.CleanupVault(i.Id);
                ReloadVault();
                return;
            }
        }

        private void treeView1_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                //right click on treeview node

                // Point where the mouse is clicked.
                Point p = new Point(e.X, e.Y);

                // Get the node that the user has clicked.
                TreeNode node = treeView1.GetNodeAt(p);
                if (node.Tag is IdentifierInfo)
                {
                    treeView1.SelectedNode = node;
                    treeViewContextMenu.Show(treeView1, p);
                }
            }
        }
    }
}