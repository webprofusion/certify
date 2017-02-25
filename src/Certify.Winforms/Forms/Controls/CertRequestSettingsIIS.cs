using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ACMESharp.Vault.Providers;
using Certify.Management;
using Certify.Models;
using System.Collections.Generic;

namespace Certify.Forms.Controls
{
    public partial class CertRequestSettingsIIS : CertRequestBaseControl
    {
        private readonly IdnMapping _idnMapping = new IdnMapping();
        private SiteManager siteManager;
        private IISManager iisManager = new IISManager();

        private BindingSource domainListBindingSource = new BindingSource();
        private List<Models.DomainOption> domains = new List<Models.DomainOption>();

        public CertRequestSettingsIIS()
        {
            InitializeComponent();

            siteManager = new SiteManager(); //registry of sites we manage certificate requests for
            siteManager.LoadSettings();
        }

        private void PopulateWebsitesFromIIS()
        {
            var siteList = iisManager.GetPrimarySites(includeOnlyStartedSites: false);
            this.lstSites.Items.Clear();
            this.lstSites.DisplayMember = "Description";

            foreach (var s in siteList)
            {
                this.lstSites.Items.Add(s);
            }

            if (lstSites.Items.Count > 0)
            {
                this.lstSites.SelectedIndex = 0;
                RefreshSelectedWebsite();
            }
        }

        private void RefreshSelectedWebsite()
        {
            var selectItem = (SiteBindingItem)lstSites.SelectedItem;
            //  lblDomain.Text = selectItem.Host;
            lblWebsiteRoot.Text = selectItem.PhysicalPath;

            this.PopulateSiteDomainList(selectItem.SiteId);
        }

        private void PopulateSiteDomainList(string siteId)
        {
            //for the given selected web site, allow the user to choose which domains to combine into one certificate
            var allSites = iisManager.GetSiteBindingList(false);
            this.domains = new List<DomainOption>();
            foreach (var d in allSites)
            {
                if (d.SiteId == siteId)
                {
                    DomainOption opt = new DomainOption { Domain = d.Host, IsPrimaryDomain = false, IsSelected = true };
                    domains.Add(opt);
                }
            }

            this.domainListBindingSource.DataSource = domains;
            this.dataGridViewDomains.DataSource = this.domainListBindingSource;

            dataGridViewDomains.Columns[0].Name = "Domain";
            dataGridViewDomains.Columns[1].Name = "Primary Domain";
            dataGridViewDomains.Columns[2].Name = "Include";
        }

        private void ShowProgressBar()
        {
            progressBar1.Enabled = true;
            progressBar1.Visible = true;
            btnCancel.Visible = false;
            btnRequestCertificate.Enabled = false;
        }

        private void HideProgressBar()
        {
            progressBar1.Enabled = false;
            progressBar1.Visible = false;
            btnCancel.Visible = true;
            btnRequestCertificate.Enabled = true;
        }

        private async void btnRequestCertificate_Click(object sender, EventArgs e)
        {
            if (lstSites.SelectedItem == null)
            {
                MessageBox.Show("No IIS site selected");
                return;
            }

            //prevent further clicks on request button
            btnRequestCertificate.Enabled = false;
            ShowProgressBar();
            this.Cursor = Cursors.WaitCursor;

            CertRequestConfig config = new CertRequestConfig();
            var siteInfo = (SiteBindingItem)lstSites.SelectedItem;

            var primaryDomain = this.domains.FirstOrDefault(d => d.IsPrimaryDomain == true && d.IsSelected == true);
            if (primaryDomain == null) primaryDomain = this.domains.FirstOrDefault(d => d.IsSelected == true);

            config.PrimaryDomain = _idnMapping.GetAscii(primaryDomain.Domain); // ACME service requires international domain names in ascii mode
            if (this.domains.Count(d => d.IsSelected) > 1)
            {
                //apply remaining selected domains as subject alternative names
                config.SubjectAlternativeNames =
                    this.domains.Where(dm => dm.Domain != primaryDomain.Domain && dm.IsSelected == true)
                    .Select(i => i.Domain)
                    .ToArray();
            }

            config.PerformChallengeFileCopy = true;
            config.PerformExtensionlessConfigChecks = !chkSkipConfigCheck.Checked;
            config.PerformExtensionlessAutoConfig = true;
            config.WebsiteRootPath = Environment.ExpandEnvironmentVariables(siteInfo.PhysicalPath);

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one
            ManagedSite managedSite = siteManager.GetManagedSite(siteInfo.SiteId);
            if (managedSite == null)
            {
                managedSite = new ManagedSite();
                managedSite.SiteId = siteInfo.SiteId;
                managedSite.IncludeInAutoRenew = chkIncludeInAutoRenew.Checked;
            }
            else
            {
                managedSite.IncludeInAutoRenew = chkIncludeInAutoRenew.Checked;
            }

            //store domain options settings and request config for this site so we can replay for automated renewal
            managedSite.DomainOptions = this.domains;
            managedSite.RequestConfig = config;

            //perform the certificate validations and request process
            var certifyManager = new CertifyManager();
            await certifyManager.PerformCertificateRequest(VaultManager, siteManager, managedSite);

            this.Cursor = Cursors.Default;
        }

        private void lstSites_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshSelectedWebsite();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
        }

        private void CloseParentForm()
        {
            Form tmp = this.FindForm();
            tmp?.Close();
            tmp?.Dispose();
        }

        private void CertRequestSettingsIIS_Load(object sender, EventArgs e)
        {
            if (this.DesignMode) return;

            btnRequestCertificate.Enabled = true;
            PopulateWebsitesFromIIS();
            HideProgressBar();

            if (lstSites.Items.Count == 0)
            {
                MessageBox.Show("You have no applicable IIS sites configured. Setup a website in IIS or use a Generic Request.");
            }
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {
        }
    }
}