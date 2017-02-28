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
using Certify.Models;

namespace Certify.Forms.Controls
{
    public partial class ManagedSites : UserControl
    {
        private SiteManager siteManager;
        private ManagedSite selectedSite;

        public ManagedSites()
        {
            InitializeComponent();

            siteManager = new SiteManager();
        }

        private MainForm GetParentMainForm()
        {
            return (MainForm)this.Parent.FindForm();
        }

        private void RefreshManagedSitesList()
        {
            siteManager.LoadSettings();
            var sites = siteManager.GetManagedSites();

            this.listView1.ShowGroups = true;

            if (this.listView1.Items != null)
            {
                this.listView1.Items.Clear();
            }

            foreach (var s in sites)
            {
                var siteNode = new ListViewItem(s.SiteName);
                siteNode.Tag = s.SiteId;
                siteNode.ImageIndex = 0;
                this.listView1.Items.Add(siteNode);
                if (s.IncludeInAutoRenew)
                {
                    siteNode.Group = this.listView1.Groups[0];
                }
                else
                {
                    siteNode.Group = this.listView1.Groups[1];
                }
            }
        }

        private void ManagedSites_Load(object sender, EventArgs e)
        {
            if (!this.DesignMode)
            {
                this.RefreshManagedSitesList();
            }
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.lblInfo.Text = "";

            this.selectedSite = null;

            //selected site
            if (this.listView1.SelectedItems.Count > 0)
            {
                var selectedNode = this.listView1.SelectedItems[0];
                if (selectedNode.Tag != null)
                {
                    var site = siteManager.GetManagedSite(selectedNode.Tag.ToString());
                    if (site.RequestConfig != null && site.RequestConfig.PrimaryDomain != null)
                    {
                        this.selectedSite = site;
                        this.PopulateSiteDetails(site);
                    }
                }
            }
        }

        private void PopulateSiteDetails(ManagedSite site)
        {
            this.certRequestSettingsIIS1.LoadManagedSite(site);
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (this.selectedSite != null)
            {
                var certifyManager = new CertifyManager();
                var vaultManager = GetParentMainForm().VaultManager;

                siteManager.LoadSettings();
                var result = await certifyManager.PerformCertificateRequest(vaultManager, this.selectedSite);
                if (!result.IsSuccess)
                {
                    MessageBox.Show("Failed to request a new certificate.");
                }
                else
                {
                    MessageBox.Show("Certificate request completed.");
                }
            }
        }
    }
}