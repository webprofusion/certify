using Certify.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Certify.UI
{
    /// <summary>
    /// Mock data item view model for use in the XAML designer in Visual Studio 
    /// </summary>
    public class DesignItemViewModel : ViewModel.ManagedItemModel
    {
        private DesignViewModel _appViewModel => (DesignViewModel)DesignViewModel.Current;

        public DesignItemViewModel()
        {
            // auto-load data if in WPF designer
            bool inDesignMode = !(Application.Current is App);
            if (inDesignMode)
            {
                SelectedItem = _appViewModel.ManagedSites.First();
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