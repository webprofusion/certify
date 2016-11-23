using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Certify.Forms.Controls
{
    public partial class CertRequestHTTPGeneric : CertRequestBaseControl
    {
        private int wizardStep = 1;

        public CertRequestHTTPGeneric()
        {
            InitializeComponent();
        }

        private void tabPage1_Click(object sender, EventArgs e)
        {
        }

        private bool IsStepValid()
        {
            if (wizardStep == 1)
            {
                if (String.IsNullOrEmpty(txtDomain.Text))
                {
                    return false;
                }
            }
            return true;
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            if (IsStepValid())
            {
                wizardStep++;
            }
            else
            {
                MessageBox.Show("Invalid settings. Please check before proceeding.");
            }
        }
    }
}