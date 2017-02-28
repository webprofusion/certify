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
        private BindingSource sanListBindingSource = new BindingSource();
        private List<Models.DomainOption> domains = new List<Models.DomainOption>();

        /// <summary>
        /// If true, all IIS websites are shown, otherwise only a single site is selected
        /// </summary>
        public bool IsNewCertMode { get; set; }

        private ManagedSite _selectedManagedSite { get; set; }

        public CertRequestSettingsIIS()
        {
            InitializeComponent();

            siteManager = new SiteManager(); //registry of sites we manage certificate requests for
            siteManager.LoadSettings();

            IsNewCertMode = true;
        }

        public void LoadManagedSite(ManagedSite site)
        {
            this._selectedManagedSite = site;

            //use options form saved site to populate current site settings
            PopulateSiteDomainList(site.SiteId, site);

            this.lstSites.Visible = false;
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
            txtManagedSiteName.Text = selectItem.SiteName;

            //if we have already saved settings for this site, load them again
            var existingSite = siteManager.GetManagedSite(selectItem.SiteId);
            this.PopulateSiteDomainList(selectItem.SiteId, existingSite);
        }

        private void PopulateSiteDomainList(string siteId, ManagedSite managedSite = null)
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

            if (managedSite != null && managedSite.DomainOptions != null)
            {
                //carry over settings from saved managed site
                txtManagedSiteName.Text = managedSite.SiteName;

                foreach (var d in domains)
                {
                    var opt = managedSite.DomainOptions.FirstOrDefault(o => o.Domain == d.Domain);
                    d.IsPrimaryDomain = opt.IsPrimaryDomain;
                    d.IsSelected = opt.IsSelected;
                }
            }

            if (domains.Any())
            {
                //mark first domain as primary, if we have no other settings
                if (!domains.Any(d => d.IsPrimaryDomain == true))
                {
                    domains[0].IsPrimaryDomain = true;
                }

                this.domainListBindingSource.DataSource = domains;

                this.lstPrimaryDomain.DataSource = this.domainListBindingSource;
                this.lstPrimaryDomain.DisplayMember = "Domain";

                //create filtered view of domains for the san list
                this.PopulateSANList();
            }
            else
            {
                MessageBox.Show("The selected site has no domain bindings setup. Configure the domains first using Edit Bindings in IIS.");
            }
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

            var managedSite = GetUpdatedManagedSiteSettings();

            //store the updated settings
            siteManager.UpdatedManagedSite(managedSite);

            //perform the certificate validations and request process
            var certifyManager = new CertifyManager();
            var result = await certifyManager.PerformCertificateRequest(VaultManager, managedSite);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.ErrorMessage);
            }
            else
            {
                MessageBox.Show("Certificate Request Completed");
            }

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

        private void RefreshDomainOptionSettingsFromUI()
        {
            //update option selected/not selected, based on current selections first (to avoid losing the users settings)
            foreach (var i in chkListSAN.Items)
            {
                var d = domains.FirstOrDefault(o => o.Domain == i.ToString());
                if (d != null)
                {
                    bool isChecked = false;
                    foreach (var c in chkListSAN.CheckedItems)
                    {
                        if (c.ToString() == d.Domain)
                        {
                            isChecked = true;
                            break;
                        }
                    }
                    d.IsSelected = isChecked;
                    d.IsPrimaryDomain = false;
                }
            }

            //selected item becomes the primary domain
            var selectedDomainOption = ((DomainOption)lstPrimaryDomain.SelectedItem);
            foreach (var d in domains)
            {
                if (d.Domain == selectedDomainOption.Domain)
                {
                    d.IsPrimaryDomain = true;
                }
                else
                {
                    d.IsPrimaryDomain = false;
                }
            }
        }

        private void lstPrimaryDomain_SelectedIndexChanged(object sender, EventArgs e)
        {
            //refresh san list based on the primary selected domain
            RefreshDomainOptionSettingsFromUI();

            //now clear san list and populate it again
            this.PopulateSANList();
        }

        private void PopulateSANList()
        {
            this.chkListSAN.Items.Clear();
            foreach (var d in domains.Where(d => d.IsPrimaryDomain == false))
            {
                this.chkListSAN.Items.Add(d.Domain, d.IsSelected);
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            //save settings
            var s = GetUpdatedManagedSiteSettings();
            siteManager.UpdatedManagedSite(s);

            MessageBox.Show("Managed Site settings saved.");

            //TODO: refresh parent app list of managed sites
        }

        /// <summary>
        /// For the given set of options get a new CertRequestConfig to store
        /// </summary>
        /// <returns></returns>
        private ManagedSite GetUpdatedManagedSiteSettings()
        {
            CertRequestConfig config = new CertRequestConfig();

            RefreshDomainOptionSettingsFromUI();

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

            config.EnableFailureNotifications = chkEnableNotifications.Checked;

            //determine if this site has an existing entry in Managed Sites, if so use that, otherwise start a new one
            ManagedSite managedSite = _selectedManagedSite;

            if (managedSite == null)
            {
                managedSite = new ManagedSite();

                var siteInfo = (SiteBindingItem)lstSites.SelectedItem;

                managedSite.SiteId = siteInfo.SiteId;
                managedSite.IncludeInAutoRenew = chkIncludeInAutoRenew.Checked;
                config.WebsiteRootPath = Environment.ExpandEnvironmentVariables(siteInfo.PhysicalPath);
            }
            else
            {
                managedSite.IncludeInAutoRenew = chkIncludeInAutoRenew.Checked;
            }

            managedSite.SiteType = ManagedSiteType.LocalIIS;
            managedSite.SiteName = txtManagedSiteName.Text;

            //store domain options settings and request config for this site so we can replay for automated renewal
            managedSite.DomainOptions = this.domains;
            managedSite.RequestConfig = config;

            return managedSite;
        }
    }
}