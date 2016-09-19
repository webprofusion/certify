using ACMESharp.Vault.Model;
using ACMESharp.Vault.Providers;
using Certify.Management;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Certify.Forms.Controls.Details
{
    public partial class CertificateDetails : BaseDetailsControl, IDetailsControl<CertificateInfo>
    {
        private CertificateInfo item;

        public CertificateDetails(MainForm parentApp)
        {
            InitializeComponent();
            this.parentApp = parentApp;
        }

        public void Populate(CertificateInfo item)
        {
            this.item = item;

            lblID.Text = item.Id.ToString();
            lblAlias.Text = item.Alias;
            if (item.CertificateRequest != null)
            {
                CertificateManager certManager = new CertificateManager();
                string certPath = parentApp.VaultManager.GetCertificateFilePath(item.Id);
                string crtDerFilePath = certPath + "\\" + item.CrtDerFile;
                lblFilePath.Text = crtDerFilePath;

                if (File.Exists(crtDerFilePath))
                {
                    var cert = certManager.GetCertificate(crtDerFilePath);
                    lblExpiryDate.Text = cert.GetExpirationDateString();
                    lblIssuer.Text = cert.Issuer;
                    lblSubject.Text = cert.Subject;

                    DateTime expiryDate = DateTime.Parse(cert.GetExpirationDateString());
                    TimeSpan timeLeft = expiryDate - DateTime.Now;
                    lblDaysRemaining.Text = timeLeft.Days.ToString();
                    if (timeLeft.Days < 7)
                    {
                        lblDaysRemaining.ForeColor = Color.Red;
                    }
                    else
                    {
                        lblDaysRemaining.ForeColor = Color.Black;
                    }
                }
                else
                {
                    lblFilePath.Text = "[Not Found] " + lblFilePath.Text;
                }
            }
        }

        private void btnRenew_Click(object sender, EventArgs e)
        {
            //attempt to renew and then re-export the selected certificate
            if (item != null)
            {
                this.Cursor = Cursors.WaitCursor;
                //update and create certificate
                if (parentApp.VaultManager.RenewCertificate(item.IdentifierRef))
                {
                    Populate(item); // update display with renewed info

                    MessageBox.Show("Renewal requested. Check certificate info for expiry. Auto Apply to update IIS certificate");
                }
                else
                {
                    MessageBox.Show("Could not process renewal.");
                }
                this.Cursor = Cursors.Default;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (item != null)
            {
                parentApp.VaultManager.ExportCertificate("=" + item.Id, pfxOnly: true);
                MessageBox.Show("PFX file has been exported.");
            }
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            //attempt to match iis site with cert domain, auto create mappinngs
            var ident = parentApp.VaultManager.GetIdentifier(item.IdentifierRef.ToString());
            if (ident != null)
            {
                string certFolderPath = parentApp.VaultManager.GetCertificateFilePath(item.Id, LocalDiskVault.ASSET);
                string pfxFile = item.Id.ToString() + "-all.pfx";
                string pfxPath = Path.Combine(certFolderPath, pfxFile);

                IISManager iisManager = new IISManager();
                if (iisManager.InstallCertForDomain(ident.Dns, pfxPath, cleanupCertStore: true, skipBindings: false))
                {
                    //all done
                    MessageBox.Show("Certificate installed and SSL bindings updated for " + ident.Dns);
                    return;
                }
            }

            MessageBox.Show("Could not match cert identifier to site.");
        }
    }
}