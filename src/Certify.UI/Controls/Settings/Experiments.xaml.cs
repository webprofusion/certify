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

namespace Certify.UI.Controls.Settings
{
    /// <summary>
    /// Interaction logic for Experiments.xaml
    /// </summary>
    public partial class Experiments : UserControl
    {
        public Experiments()
        {
            InitializeComponent();
        }

        private async void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox)
            {
                var appModel = ViewModel.AppViewModel.Current;

                string feature = ((CheckBox)sender).Tag.ToString();
                var featureList = appModel.Preferences.FeatureFlags;

                if (featureList.Any(f => f == feature))
                {
                    //remove feature
                    var list = new List<string>(featureList);
                    list.Remove(feature);
                    appModel.Preferences.FeatureFlags = list.ToArray();
                }
                else
                {
                    var list = new List<string>(featureList);
                    list.Add(feature);
                    appModel.Preferences.FeatureFlags = list.ToArray();
                }

                await appModel.SavePreferences();

                appModel.RaisePropertyChangedEvent(null);
             
            }
        }
    }
}
