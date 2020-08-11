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
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled("CA_EDITOR");
                set => ToggleFeature("CA_EDITOR", value);
            }

            public bool IsPrivKeyPwdEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled("PRIVKEY_PWD");
                set => ToggleFeature("PRIVKEY_PWD", value);
            }

            public bool IsImportExportEnabled
            {
                get => ViewModel.AppViewModel.Current.IsFeatureEnabled("IMPORT_EXPORT");
                set => ToggleFeature("IMPORT_EXPORT", value);
            }

            internal void ToggleFeature(string feature, bool isEnabled)
            {

                var appModel = ViewModel.AppViewModel.Current;
                var featureList = ViewModel.AppViewModel.Current.Preferences.FeatureFlags;

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

                appModel.SavePreferences();

            }

        }

        public Model EditModel { get; set; } = new Model();

        public Experiments()
        {
            InitializeComponent();

            this.DataContext = EditModel;

        }

        private async void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            /* if (sender is CheckBox)
             {
                 string feature = ((CheckBox)sender).Tag.ToString();
                 bool isChecked = ((CheckBox)sender).IsChecked ?? false;




             }*/
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.DataContext = EditModel;
            EditModel.RaisePropertyChangedEvent(null);
        }
    }
}
