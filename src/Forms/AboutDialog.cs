using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Certify.Forms
{
    public partial class AboutDialog : Form
    {
        public AboutDialog()
        {
            InitializeComponent();
            this.lblAppName.Text = Properties.Resources.LongAppName;
            this.lblAppVersion.Text = ProductVersion + " - " + Properties.Resources.ReleaseDate;
            this.lnkPublisherWebsite.Text = Properties.Resources.AppWebsiteURL;
            this.txtCredits.Text = Properties.Resources.Credits;
        }

        private void lnkPublisherWebsite_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ProcessStartInfo sInfo = new ProcessStartInfo(Properties.Resources.AppWebsiteURL);
            Process.Start(sInfo);
        }
    }
}