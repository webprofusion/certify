using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Certify.Forms.Controls.Details
{
    public partial class SimpleDetails : BaseDetailsControl, IDetailsControl<String>
    {
        public SimpleDetails(MainForm parentApp)
        {
            InitializeComponent();
            this.parentApp = parentApp;
        }

        public void Populate(string item)
        {
            this.lblDetails.Text = item;
        }
    }
}