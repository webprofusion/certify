using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Certify.Models;
using PropertyChanged;

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Experiments.xaml
    /// </summary>
    public partial class Experiments : UserControl
    {
        public class Model : BindableBase
        {
            public Models.Preferences Prefs => ViewModel.AppViewModel.Current.Preferences;


            public bool IsCAEditorEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.CA_EDITOR);
                set => ToggleFeature(FeatureFlags.CA_EDITOR, value);
            }

            public bool IsPrivKeyPwdEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.PRIVKEY_PWD);
                set => ToggleFeature(FeatureFlags.PRIVKEY_PWD, value);
            }

            public bool IsImportExportEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.IMPORT_EXPORT);
                set => ToggleFeature(FeatureFlags.IMPORT_EXPORT, value);
            }

            public bool IsExternalCertManagersEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.EXTERNAL_CERT_MANAGERS);
                set => ToggleFeature(FeatureFlags.EXTERNAL_CERT_MANAGERS, value);
            }

            public bool IsServerConnectionsEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.SERVER_CONNECTIONS);
                set => ToggleFeature(FeatureFlags.SERVER_CONNECTIONS, value);
            }


            internal void ToggleFeature(string feature, bool isEnabled)
            {

                var appModel = ViewModel.AppViewModel.Current;
                var featureList = ViewModel.AppViewModel.Current.Preferences.FeatureFlags ?? new string[] { };

                if (!isEnabled)
                {
                    //remove feature
                    var list = new List<string>(featureList);
                    list.Remove(feature);
                    appModel.Preferences.FeatureFlags = list.ToArray();
                }
                else
                {
                    var list = new List<string>(featureList);
                    if (!list.Contains(feature))
                    {
                        list.Add(feature);
                    }
                    appModel.Preferences.FeatureFlags = list.ToArray();
                }

                // note: pref changes are saved by the property change listener in General settings

            }

        }

        public Model EditModel { get; set; } = new Model();

        public Experiments()
        {
            InitializeComponent();

            this.DataContext = EditModel;

        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.DataContext = EditModel;
            EditModel.RaisePropertyChangedEvent(null);
        }
    }
}
