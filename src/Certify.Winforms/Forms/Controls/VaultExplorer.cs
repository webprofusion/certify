using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Certify.Management;
using ACMESharp.Vault.Model;
using System.IO;

namespace Certify.Forms.Controls
{
    public partial class VaultExplorer : UserControl
    {
        internal VaultManager VaultManager = null;

        private MainForm GetParentMainForm()
        {
            return (MainForm)this.Parent.FindForm();
        }

        public VaultExplorer()
        {
            InitializeComponent();
        }

        private void populateTreeView(VaultInfo vaultConfig)
        {
            if (this.treeView1.Nodes != null)
            {
                this.treeView1.Nodes.Clear();
            }

            CertificateManager certManager = new CertificateManager();
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

                                //get info from get if possible, use that to style the parent node (expiry warning)

                                string certPath = VaultManager.GetCertificateFilePath(c.Id);
                                string crtDerFilePath = certPath + "\\" + c.CrtDerFile;

                                if (File.Exists(crtDerFilePath))
                                {
                                    var cert = certManager.GetCertificate(crtDerFilePath);

                                    DateTime expiryDate = DateTime.Parse(cert.GetExpirationDateString());
                                    TimeSpan timeLeft = expiryDate - DateTime.Now;
                                    node.Text += " (" + timeLeft.Days + " days remaining)";
                                    if (timeLeft.Days < 30)
                                    {
                                        node.ForeColor = Color.Orange;
                                    }
                                    if (timeLeft.Days < 7)
                                    {
                                        node.ForeColor = Color.Red;
                                    }
                                }
                                else
                                {
                                    node.ForeColor = Color.Gray;
                                }
                                node.Nodes.Add(certNode);
                            }
                        }
                    }
                    domainsNode.Nodes.Add(node);
                }

                mainNode.Nodes.Add(domainsNode);
                domainsNode.Expand();
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

                contactsNode.Expand();
            }

            if (mainNode.Nodes.Count == 0)
            {
                mainNode.Nodes.Add("(Empty)");
            }
            else
            {
                mainNode.Expand();
            }

            // this.treeView1.ExpandAll();
        }

        private void ReloadVault()
        {
            this.VaultManager = ((MainForm)this.Parent.FindForm()).VaultManager;
            VaultManager.ReloadVaultConfig();
            var vaultInfo = VaultManager.GetVaultConfig();
            if (vaultInfo != null)
            {
                this.lblVaultLocation.Text = VaultManager.VaultFolderPath;
                this.lblAPIBaseURI.Text = vaultInfo.BaseUri;

                populateTreeView(vaultInfo);

                this.UpdateLogView(VaultManager.GetActionLogSummary());

                //store setting for current vault path
                if (Properties.Settings.Default.VaultPath != VaultManager.VaultFolderPath)
                {
                    Properties.Settings.Default.VaultPath = VaultManager.VaultFolderPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void UpdateLogView(string logContent)
        {
            //TODO: update app log view
        }

        private void treeView1_MouseClick(object sender, MouseEventArgs e)
        {
            // right click on treeview node
            if (e.Button == MouseButtons.Right)
            {
                // Point where the mouse is clicked.
                Point p = new Point(e.X, e.Y);

                // Get the node that the user has clicked.
                TreeNode node = treeView1.GetNodeAt(p);

                if (node != null)
                {
                    if (node.Tag is IdentifierInfo)
                    {
                        treeView1.SelectedNode = node;
                        treeViewContextMenu.Show(treeView1, p);
                    }
                    else
                    {
                        treeView1.SelectedNode = node;
                        treeViewContextMenu.Show(treeView1, p);
                    }
                }
            }
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
                    var infoControl = new Forms.Controls.Details.RegistrationInfoDetails(this.GetParentMainForm());
                    infoControl.Populate(i);
                    panelItemInfo.Controls.Add(infoControl);
                }

                if (selectedItem is CertificateInfo)
                {
                    var i = (CertificateInfo)selectedItem;
                    panelItemInfo.Controls.Clear();
                    var infoControl = new Forms.Controls.Details.CertificateDetails(this.GetParentMainForm());
                    infoControl.Populate(i);
                    infoControl.Dock = DockStyle.Fill;
                    panelItemInfo.Controls.Add(infoControl);
                }

                if (selectedItem is IdentifierInfo)
                {
                    var i = (IdentifierInfo)selectedItem;
                    panelItemInfo.Controls.Clear();
                    var infoControl = new Forms.Controls.Details.SimpleDetails(this.GetParentMainForm());
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

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // User has clicked delete in tree view context menu
            var node = treeView1.SelectedNode;

            if (node != null)
            {
                if (node.Tag is IdentifierInfo)
                {
                    var i = (IdentifierInfo)node.Tag;
                    VaultManager.CleanupVault(i.Id);
                    ReloadVault();
                    return;
                }
                else
                {
                    DeleteVaultItem(node.Tag);
                    ReloadVault();
                    return;
                }
            }
        }

        private void VaultExplorer_Load(object sender, EventArgs e)
        {
            //this.ReloadVault();
            if (LicenseManager.UsageMode == LicenseUsageMode.Runtime)
            {
                this.ReloadVault();
            }
        }

        private void groupBoxVaultInfo_Enter(object sender, EventArgs e)
        {

        }
    }
}