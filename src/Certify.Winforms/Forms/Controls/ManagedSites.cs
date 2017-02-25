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
            if (sites.Any())
            {
                this.btnRenewAll.Visible = true;
            }
            else
            {
                this.btnRenewAll.Visible = false;
            }

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
            this.RefreshManagedSitesList();
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.lblInfo.Text = "";
            this.panel1.Visible = false;

            //selected site
            if (this.listView1.SelectedItems.Count > 0)
            {
                var selectedNode = this.listView1.SelectedItems[0];
                if (selectedNode.Tag != null)
                {
                    var site = siteManager.GetManagedSite(selectedNode.Tag.ToString());
                    if (site.RequestConfig != null && site.RequestConfig.PrimaryDomain != null)
                    {
                        this.PopulateSiteDetails(site);
                    }
                }
            }
        }

        private void PopulateSiteDetails(ManagedSite site)
        {
            this.panel1.Visible = true;

            this.lblPrimarySubjectDomain.Text = site.RequestConfig.PrimaryDomain;
            this.lblAutoRenew.Text = site.IncludeInAutoRenew ? "Yes" : "No";
            try
            {
                this.lblDateLastRenewed.Text = site.Logs.Last(l => l.LogItemType == LogItemType.CertificateRequestSuccessful).EventDate.ToShortDateString();
            }
            catch (Exception) { }

            if (site.RequestConfig.SubjectAlternativeNames != null && site.RequestConfig.SubjectAlternativeNames.Length > 0)
            {
                this.checkedListBox1.Items.Clear();

                foreach (var s in site.RequestConfig.SubjectAlternativeNames)
                {
                    this.checkedListBox1.Items.Add(s, true);
                }
            }
        }

        private async void btnRenewAll_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Renew all certificates for all managed sites? The previous request settings for each site will be re-used.", "Renew All?", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                //begin renew process for each site
                //start background worker

                this.Cursor = Cursors.WaitCursor;

                var certifyManager = new CertifyManager();
                var vaultManager = GetParentMainForm().VaultManager;

                siteManager.LoadSettings();
                var sites = siteManager.GetManagedSites();

                var results = new List<CertificateRequestResult>();
                foreach (var s in sites)
                {
                    results.Add(await certifyManager.PerformCertificateRequest(vaultManager, siteManager, s));
                }

                this.Cursor = Cursors.Default;

                //TODO: display results
                if (results.Any(r => r.IsSuccess == false))
                {
                    MessageBox.Show("One or more sites failed to request a new certificate.");
                }
            }
        }
    }
}