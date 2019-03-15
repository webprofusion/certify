using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Certify.Models;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for EditContactDialog.xaml 
    /// </summary>
    public partial class EditContactDialog
    {
        public ContactRegistration Item { get; set; }

        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public EditContactDialog()
        {
            InitializeComponent();

            Item = new ContactRegistration();

            DataContext = Item;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = Cursors.Arrow;
            Close();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            //add/update contact
            var isValidEmail = true;
            if (string.IsNullOrEmpty(Item.EmailAddress))
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
                MessageBox.Show(Certify.Locales.SR.New_Contact_EmailError);

                return;
            }

            if (Item.AgreedToTermsAndConditions)
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var addedOK = await MainViewModel.AddContactRegistration(Item);

                Mouse.OverrideCursor = Cursors.Arrow;

                if (addedOK)
                {
                    MainViewModel.PrimaryContactEmail = await MainViewModel.CertifyClient.GetPrimaryContact();
                    Close();
                }
                else
                {
                    // FIXME: specific error message or a general try again message
                    MessageBox.Show(Certify.Locales.SR.New_Contact_EmailError);
                }
            }
            else
            {
                MessageBox.Show(Certify.Locales.SR.New_Contact_NeedAgree);
            }
        }
    }
}
