using System.Collections.Generic;
using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedCertificate
{
    public partial class TestProgress : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateModel ItemViewModel => UI.ViewModel.ManagedCertificateModel.Current;
        protected Certify.UI.ViewModel.AppModel AppViewModel => UI.ViewModel.AppModel.Current;

        private List<string> ResultList
        {
            get
            {
                var list = (List<string>)ItemViewModel.ConfigCheckResult.Result;
                return list;
            }
        }

        public TestProgress()
        {
            InitializeComponent();
        }
    }
}