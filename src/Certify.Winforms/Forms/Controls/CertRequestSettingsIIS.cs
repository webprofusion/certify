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

        private void btnRequestCertificate_Click(object sender, EventArgs e)
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

            //primary domain and each subject alternative name must now be registered as an identifier with LE and validated

            List<string> allDomains = new List<string>();
            allDomains.Add(config.PrimaryDomain);
            if (config.SubjectAlternativeNames != null) allDomains.AddRange(config.SubjectAlternativeNames);
            bool allIdentifiersValidated = true;

            List<PendingAuthorization> identifierAuthorizations = new List<PendingAuthorization>();

            foreach (var domain in allDomains)
            {
                //check if domain already has an associated identifier
                var identifierAlias = VaultManager.ComputeIdentifierAlias(domain);

                managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestStarted, Message = "Attempting Certificate Request (IIS)" });

                //begin authorixation process (register identifier, request authorization if not already given)
                var authorization = VaultManager.BeginRegistrationAndValidation(config, identifierAlias, challengeType: "http-01", domain: domain);

                if (authorization != null)
                {
                    if (authorization.Identifier.Authorization.IsPending())
                    {
                        //ask LE to check our answer to their authorization challenge (http), LE will then attempt to fetch our answer, if all accessible and correct (authorized) LE will then allow us to request a certificate
                        //prepare IIS with answer for the LE challenege
                        authorization = VaultManager.PerformIISAutomatedChallengeResponse(config, authorization);

                        //if we attempted extensionless config checks, report any errors
                        if (!chkSkipConfigCheck.Checked && !authorization.ExtensionlessConfigCheckedOK)
                        {
                            managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (IIS)" });
                            siteManager.StoreSettings();

                            //MessageBox.Show("Automated checks for extensionless content failed. Authorizations will not be able to complete. Change the web.config in <your site>\\.well-known\\acme-challenge and ensure you can browse to http://<your site>/.well-known/acme-challenge/configcheck before proceeding.");
                            //CloseParentForm();
                            return;
                        }
                        else
                        {
                            //ask LE to validate our challenge response
                            VaultManager.SubmitChallenge(identifierAlias, "http-01");

                            bool identifierValidated = VaultManager.CompleteIdentifierValidationProcess(authorization.Identifier.Alias);

                            if (!identifierValidated)
                            {
                                allIdentifiersValidated = false;
                            }
                            else
                            {
                                identifierAuthorizations.Add(authorization);
                            }
                        }
                    }
                }
            }

            this.Cursor = Cursors.Default;

            if (allIdentifiersValidated)
            {
                string primaryDnsIdentifier = identifierAuthorizations.First().Identifier.Alias;
                string[] alternativeDnsIdentifiers = identifierAuthorizations.Where(i => i.Identifier.Alias != primaryDnsIdentifier).Select(i => i.Identifier.Alias).ToArray();

                var certRequestResult = VaultManager.PerformCertificateRequestProcess(primaryDnsIdentifier, alternativeDnsIdentifiers);
                if (certRequestResult.IsSuccess)
                {
                    string pfxPath = certRequestResult.Result.ToString();

                    //Install certificate into certificate store and bind to IIS site
                    if (iisManager.InstallCertForDomain(config.PrimaryDomain, pfxPath, cleanupCertStore: true, skipBindings: !chkAutoBindings.Checked))
                    {
                        //all done
                        managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertificateRequestSuccessful, Message = "Completed certificate request and automated bindings update (IIS)" });
                        siteManager.StoreSettings();

                        MessageBox.Show("Certificate installed and SSL bindings updated for " + config.PrimaryDomain, Properties.Resources.AppName);
                        CloseParentForm();
                        return;
                    }
                    else
                    {
                        MessageBox.Show("An error occurred installing the certificate. Certificate file may not be valid.");
                        CloseParentForm();
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("LE did not issue a valid certificate in the time allowed.");
                    CloseParentForm();
                    return;
                }
            }
            else
            {
                MessageBox.Show("Validation of the required challenges did not complete successfully.");
                CloseParentForm();
                return;
            }
            /*else
                 {
                 MessageBox.Show("Could not begin authorization. Check Logs. Ensure the domain being authorized is whitelisted with LetsEncrypt service.");
                 managedSite.AppendLog(new ManagedSiteLogItem { EventDate = DateTime.UtcNow, LogItemType = LogItemType.CertficateRequestFailed, Message = "Failed prerequisite configuration (IIS)" });

                 siteManager.StoreSettings();
             }*/
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