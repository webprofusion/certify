using Certify.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for ProgressMonitor.xaml
    /// </summary>
    public partial class ProgressMonitor : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return UI.ViewModel.AppModel.AppViewModel;
            }
        }

        public ProgressMonitor()
        {
            InitializeComponent();
            this.DataContext = MainViewModel;
        }
    }

    internal class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value.ToString() == RequestState.Error.ToString())
            {
                return new SolidColorBrush(System.Windows.Media.Colors.Red);
            }
            if (value.ToString() == RequestState.Success.ToString())
            {
                return new SolidColorBrush(System.Windows.Media.Colors.Green);
            }

            return new SolidColorBrush(System.Windows.Media.Colors.DarkGray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}