using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for EditContactDialog.xaml
    /// </summary>
    public partial class EditContactDialog
    {
        public ContactRegistration Item { get; set; }

        protected Certify.UI.ViewModel.AppModel MainViewModel
        {
            get
            {
                return ViewModel.AppModel.AppViewModel;
            }
        }

        public EditContactDialog()
        {
            InitializeComponent();

            Item = new ContactRegistration();

            this.DataContext = Item;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
            this.Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            //add/update contact
            bool isValidEmail = true;
            if (String.IsNullOrEmpty(Item.EmailAddress))
            {
                isValidEmail = false;
            }
            else
            {
                if (!Regex.IsMatch(Item.EmailAddress,
                            @"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                            @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-\w]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$",
                            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)))
                {
                    isValidEmail = false;
                }
            }

            if (!isValidEmail)
            {
                MessageBox.Show("Ooops, you forgot to provide a valid email address.");

                return;
            }

            if (Item.AgreedToTermsAndConditions)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (MainViewModel.AddContactCommand.CanExecute((Item)))
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(
                        () =>
                        {
                            MainViewModel.AddContactCommand.Execute(Item);
                        }));

                    Mouse.OverrideCursor = Cursors.Arrow;
                    this.Close();
                }
            }
            else
            {
                MessageBox.Show("You need to agree to the latest LetsEncrypt.org Subscriber Agreement.");
            }
        }
    }
}