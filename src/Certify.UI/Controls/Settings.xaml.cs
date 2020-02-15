using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Certify.Models;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : UserControl
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;


        public Settings()
        {
            InitializeComponent();

        }

    }
}
