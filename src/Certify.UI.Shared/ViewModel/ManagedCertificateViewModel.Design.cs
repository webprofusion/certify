using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.UI
{
    /// <summary>
    /// Mock data item view model for use in the XAML designer in Visual Studio 
    /// </summary>
    public class ManagedCertificateViewModelDesign : ViewModel.ManagedCertificateViewModel
    {
        private AppViewModelDesign _appViewModel => (AppViewModelDesign)AppViewModelDesign.Current;

        public ManagedCertificateViewModelDesign()
        {
            // auto-load data if in WPF designer

            SelectedItem = _appViewModel.ManagedCertificates.FirstOrDefault().Item;

            SelectedItem.RenewalFailureCount = 3;
            SelectedItem.RenewalFailureMessage = "This is an example renewal failure message. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";
            SelectedItem.DateLastRenewalAttempt = DateTimeOffset.UtcNow.AddMinutes(-30);

            ConfigCheckResults = new System.Collections.ObjectModel.ObservableCollection<StatusMessage> {
                    new StatusMessage{
                        IsOK =true,
                        Message ="This is an example configuration test result."
                    },
                     new StatusMessage{
                        IsOK =false,
                        Message ="This is a failure message."
                    }
                };

        }

        private void _appViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_appViewModel.SelectedItem))
            {
                RaisePropertyChangedEvent(nameof(SelectedItem));
            }
        }

        protected async override Task<IEnumerable<DomainOption>> GetDomainOptionsFromSite(string siteId)
        {
            return await Task.Run(() =>
                    {
                        return Enumerable.Range(1, 50).Select(i => new DomainOption()
                        {
                            Domain = $"www{i}.domain.example.org",
                            IsPrimaryDomain = i == 1,
                            IsSelected = true
                        });
                    });
        }
    }
}
