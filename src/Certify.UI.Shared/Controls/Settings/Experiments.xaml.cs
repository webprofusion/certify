using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;

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

            public bool IsPrivKeyPwdEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.PRIVKEY_PWD);
                set => ToggleFeature(FeatureFlags.PRIVKEY_PWD, value);
            }

            public bool IsDataStoresEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled(FeatureFlags.DATA_STORES);
                set => ToggleFeature(FeatureFlags.DATA_STORES, value);
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

            DataContext = EditModel;

        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            DataContext = EditModel;
            EditModel.RaisePropertyChangedEvent(null);
        }
    }
}
