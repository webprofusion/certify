using Certify.Forms.Controls;
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
    public partial class CertRequestDialog : Form
    {
        private enum CertControlType
        {
            IIS = 0,
            GenericHttp = 1
        }

        private VaultManager vaultManager = null;

        public CertRequestDialog()
        {
            InitializeComponent();
        }

        public CertRequestDialog(VaultManager vaultManager) : this()
        {
            this.vaultManager = vaultManager;

            lstRequestType.SelectedIndex = 0;
            //share the vault manager with the current request control type

            if (!this.vaultManager.HasContacts())
            {
                MessageBox.Show("You need to register a valid contact before you can proceed.");
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void InitSelectedCertTypeControl()
        {
            if (lstRequestType.SelectedIndex == (int)CertControlType.IIS)
            {
                var iisManager = new IISManager();
                var version = iisManager.GetIisVersion();
                if (version.Major == 0)
                {
                    //no iis
                    MessageBox.Show("You do not have IIS installed locally. Automated configuration will be unavailable.");
                    lstRequestType.SelectedIndex = 1; //generic
                }
                else
                {
                    //IIS selected, setup IIS cert request control
                    var iisRequestControl = new CertRequestSettingsIIS();
                    iisRequestControl.IsNewManagedSiteMode = true;
                    SetupSelectedCertRequestControl(iisRequestControl);
                }
            }

            if (lstRequestType.SelectedIndex == (int)CertControlType.GenericHttp)
            {
                SetupSelectedCertRequestControl(new CertRequestHTTPGeneric());
            }
        }

        private void SetupSelectedCertRequestControl(CertRequestBaseControl certControl)
        {
            certControl.VaultManager = this.vaultManager;
            this.panelCertControl.Controls.Clear();
            this.panelCertControl.Controls.Add(certControl);
        }

        private void lstRequestType_SelectedIndexChanged(object sender, EventArgs e)
        {
            //TODO: support for loading the required control type for the type of request we're going to require
            InitSelectedCertTypeControl();
        }
    }
}