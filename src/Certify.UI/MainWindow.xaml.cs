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

namespace Certify.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_NewCertificate(object sender, RoutedEventArgs e)
        {
            //present new certificate dialog
        }

        private void Button_NewContact(object sender, RoutedEventArgs e)
        {
            //present new contact dialog
        }

        private void Button_RenewAll(object sender, RoutedEventArgs e)
        {
            //present new renew all confirmation
        }

        private void MenuItem_ShowAbout(object sender, RoutedEventArgs e)
        {
        }
    }
}