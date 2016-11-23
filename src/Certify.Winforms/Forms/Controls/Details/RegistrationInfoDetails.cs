using ACMESharp.Vault.Model;
using System;

namespace Certify.Forms.Controls.Details
{
    public partial class RegistrationInfoDetails : BaseDetailsControl, IDetailsControl<RegistrationInfo>
    {
        private RegistrationInfo registrationInfo;

        public RegistrationInfoDetails(MainForm parentApp)
        {
            InitializeComponent();
            this.parentApp = parentApp;
        }

        public void Populate(RegistrationInfo info)
        {
            this.registrationInfo = info;

            lblID.Text = info.Id.ToString();
            lblAlias.Text = info.Alias;
            lblLabel.Text = info.Label;
            lblMemo.Text = info.Memo;
            lblSignerProvider.Text = info.SignerProvider;
            lblContacts.Text = "";
            if (info.Registration != null && info.Registration.Contacts != null)
            {
                foreach (var c in info.Registration.Contacts)
                {
                    lblContacts.Text += c;
                }
            }

            lnkUri.Text = info.Registration.RegistrationUri;
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (this.registrationInfo != null)
            {
                bool success = parentApp.DeleteVaultItem(this.registrationInfo);
                if (success)
                {
                    this.Hide();
                }
            }
        }
    }
}