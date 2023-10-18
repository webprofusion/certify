using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Certify.UI.Controls
{

    public delegate void OnDuplicateManagedCertificate(Certify.Models.ManagedCertificate original);  // delegate

    /// <summary>
    /// Interaction logic for ManagedCertificates.xaml 
    /// </summary>
    public partial class ManagedCertificates
    {
        protected ViewModel.AppViewModel _appViewModel => ViewModel.AppViewModel.Current;
        protected ViewModel.ManagedCertificateViewModel _itemViewModel => ViewModel.ManagedCertificateViewModel.Current;

        private string _sortOrder { get; set; } = "NameAsc";

        /// <summary>
        /// event for Duplicate option
        /// </summary>
        public event OnDuplicateManagedCertificate OnDuplicate;

        public ManagedCertificates()
        {
            InitializeComponent();
            DataContext = _appViewModel;
            MainItemView.DataContext = _itemViewModel;

            SetFilter(); // start listening

            _appViewModel.PropertyChanged -= AppViewModel_PropertyChanged;
            _appViewModel.PropertyChanged += AppViewModel_PropertyChanged;

        }

        private void AppViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "ManagedCertificates" || (e.PropertyName == "SelectedItem" &&
                    _appViewModel.ManagedCertificates != null))
            {
                SetFilter(); // reset listeners when ManagedCertificates are reset
                _itemViewModel.RaisePropertyChangedEvent("SelectedItem");
                _itemViewModel.RaisePropertyChangedEvent("IsSelectedItemValid");
            }
        }

        private void SetFilter()
        {
            CollectionViewSource.GetDefaultView(_appViewModel.ManagedCertificates).Filter = (item) =>
            {
                var filter = txtFilter.Text.Trim();
                var matchItem = item as Models.ManagedCertificate;

                return filter == "" || filter.Split(';').Where(f => f.Trim() != "").Any(f =>

                        // match on a specific status filter
                        (f == "[Status=Error]" && matchItem.Health == Models.ManagedCertificateHealth.Error) ||
                        (f == "[Status=OK]" && matchItem.Health == Models.ManagedCertificateHealth.OK) ||
                        (f == "[Status=Warning]" && matchItem.Health == Models.ManagedCertificateHealth.Warning) ||
                        (f == "[Status=AwaitingUser]" && matchItem.Health == Models.ManagedCertificateHealth.AwaitingUser) ||
                        (f == "[Status=InvalidConfig]" && matchItem.DomainOptions?.Count(d => d.IsPrimaryDomain) > 1) ||
                        (f == "[Status=NoCertificate]" && matchItem.CertificatePath == null) ||
                        // match on selected or primary domain options with domain containing keyword
                        (matchItem.DomainOptions?.Any(d => (d.IsSelected || d.IsPrimaryDomain) && d.Domain.Contains(f, StringComparison.InvariantCultureIgnoreCase)) ?? false) ||
                        // match on requestconfig primary or san containing keyword
                        (matchItem.RequestConfig.SubjectAlternativeNames?.Any(d => d.Contains(f, StringComparison.InvariantCultureIgnoreCase)) ?? false) ||
                        (matchItem.RequestConfig.PrimaryDomain?.Contains(f, StringComparison.InvariantCultureIgnoreCase) ?? false) ||
                        // match on comments containing keyword
                        (matchItem.Comments ?? "").Contains(f, StringComparison.InvariantCultureIgnoreCase) ||
                        // match on name containing keyword
                        matchItem.Name.Contains(f, StringComparison.InvariantCultureIgnoreCase) ||
                        // match on ID
                        matchItem.Id == f
                    );
            };

            //sort by name ascending
            CollectionViewSource.GetDefaultView(_appViewModel.ManagedCertificates).SortDescriptions.Clear();

            if (_sortOrder == "NameAsc")
            {
                CollectionViewSource.GetDefaultView(_appViewModel.ManagedCertificates).SortDescriptions.Add(
                   new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending)
               );
            }

            if (_sortOrder == "ExpiryDateAsc")
            {
                CollectionViewSource.GetDefaultView(_appViewModel.ManagedCertificates).SortDescriptions.Add(
                   new System.ComponentModel.SortDescription("DateExpiry", System.ComponentModel.ListSortDirection.Ascending)
               );
            }
        }

        private async void ListViewItem_InteractionEvent(object sender, InputEventArgs e)
        {
            var item = (ListViewItem)sender;
            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            if (item != null && item.DataContext != null && item.DataContext is Models.ManagedCertificate)
            {
                var site = (Models.ManagedCertificate)item.DataContext;

                site = site == _appViewModel.SelectedItem && ctrl ? null : site;

                if (_appViewModel.SelectedItem != site)
                {
                    if (await _itemViewModel.ConfirmDiscardUnsavedChanges())
                    {
                        SelectAndFocus(site);
                    }

                    e.Handled = true;
                }
            }
        }

        class Debouncer : IDisposable
        {
            private CancellationTokenSource lastCancellationTokenSource;
            private int milliseconds;

            public Debouncer(int milliseconds = 300)
            {
                this.milliseconds = milliseconds;
            }

            public async Task Debounce(Func<Task> action)
            {
                Cancel(lastCancellationTokenSource);

                var tokenSrc = lastCancellationTokenSource = new CancellationTokenSource();

                try
                {
                    await Task.Delay(new TimeSpan(milliseconds), tokenSrc.Token);
                    if (!tokenSrc.IsCancellationRequested)
                    {
                        await Task.Run(action, tokenSrc.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                }
            }

            public void Cancel(CancellationTokenSource source)
            {
                if (source != null)
                {
                    source.Cancel();
                    source.Dispose();
                }
            }

            public void Dispose()
            {
                Cancel(lastCancellationTokenSource);
            }

            ~Debouncer()
            {
                Dispose();
            }
        }

        private Debouncer _filterDebouncer = new Debouncer();

        private async void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            // refresh db results, then refresh UI view

            _appViewModel.FilterKeyword = txtFilter.Text;

            await _filterDebouncer.Debounce(_appViewModel.RefreshManagedCertificates);

            var defaultView = CollectionViewSource.GetDefaultView(lvManagedCertificates.ItemsSource);

            defaultView.Refresh();

            /* if (lvManagedCertificates.SelectedIndex == -1 && _appViewModel.SelectedItem != null)
             {
                 // if the data model's selected item has come into view after filter box text
                 // changed, select the item in the list
                 if (defaultView.Filter(_appViewModel.SelectedItem))
                 {
                     lvManagedCertificates.SelectedItem = _appViewModel.SelectedItem;
                 }
             }*/
        }

        private async void TxtFilter_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ResetFilter();
            }

            if (e.Key == Key.Enter || e.Key == Key.Down)
            {
                if (lvManagedCertificates.Items.Count > 0)
                {
                    // get selected index of filtered list or 0
                    var index = lvManagedCertificates.Items.IndexOf(_appViewModel.SelectedItem);
                    var item = lvManagedCertificates.Items[index == -1 ? 0 : index];

                    // if navigating away, confirm discard
                    if (item != _appViewModel.SelectedItem &&
                        !await _itemViewModel.ConfirmDiscardUnsavedChanges())
                    {
                        return;
                    }

                    // if confirmed, select and focus
                    e.Handled = true;
                    SelectAndFocus(item);
                }
            }
        }

        private void ResetFilter()
        {
            _appViewModel.FilterKeyword = string.Empty;

            txtFilter.Text = "";
            txtFilter.Focus();

            if (lvManagedCertificates.SelectedItem != null)
            {
                lvManagedCertificates.ScrollIntoView(lvManagedCertificates.SelectedItem);
            }
        }

        private void SelectAndFocus(object obj)
        {
            var managedCert = obj as Models.ManagedCertificate;

            lvManagedCertificates.Items.Refresh();

            if (lvManagedCertificates.Items.Count > 0 && lvManagedCertificates.Items.Contains(managedCert))
            {

                // lvManagedCertificates.UpdateLayout(); // ensure containers exist

                if (lvManagedCertificates.ItemContainerGenerator.ContainerFromItem(managedCert) is ListViewItem item)
                {
                    item.Focus();
                    item.IsSelected = true;
                }
            }

            Dispatcher.Invoke(new Action(() => { _appViewModel.SelectedItem = managedCert; }));
        }

        private async void ListViewItem_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ResetFilter();
                return;
            }

            if (e.Key == Key.Delete && lvManagedCertificates.SelectedItem != null)
            {
                var itemToDelete = lvManagedCertificates.SelectedItem as Certify.Models.ManagedCertificate;
                if (itemToDelete != null)
                {
                    await _appViewModel.DeleteManagedCertificate(itemToDelete);

                    if (lvManagedCertificates.Items.Count > 0)
                    {
                        if (lvManagedCertificates.SelectedItem != null)
                        {
                            SelectAndFocus(lvManagedCertificates.SelectedItem);
                        }
                    }
                }

                return;
            }

            object next = _appViewModel.SelectedItem;

            var item = ((ListViewItem)sender);

            var index = lvManagedCertificates.Items.IndexOf(item.DataContext);

            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            var pagesize = (int)(lvManagedCertificates.ActualHeight / item.ActualHeight);

            switch (e.Key)
            {
                case Key.Enter:
                    next = item.DataContext;
                    break;

                case Key.Space:
                    next = _appViewModel.SelectedItem != null && ctrl ? null : item.DataContext;
                    break;

                case Key.Up:
                    next = lvManagedCertificates.Items[index - 1 > -1 ? index - 1 : 0];
                    break;

                case Key.Down:
                    next = lvManagedCertificates.Items[index + 1 < lvManagedCertificates.Items.Count ? index + 1 : lvManagedCertificates.Items.Count - 1];
                    break;

                case Key.Home:
                    next = lvManagedCertificates.Items[0];
                    break;

                case Key.End:
                    next = lvManagedCertificates.Items[lvManagedCertificates.Items.Count - 1];
                    break;

                case Key.PageUp:
                    next = lvManagedCertificates.Items[index - pagesize > -1 ? index - pagesize : 0];
                    break;

                case Key.PageDown:
                    next = lvManagedCertificates.Items[index + pagesize < lvManagedCertificates.Items.Count ? index + pagesize : lvManagedCertificates.Items.Count - 1];
                    break;
            }

            if (next != _appViewModel.SelectedItem)
            {
                if (await _itemViewModel.ConfirmDiscardUnsavedChanges())
                {
                    SelectAndFocus(next);
                }

                e.Handled = true;
            }
        }

        private int lastSelectedIndex = -1;

        private void lvManagedCertificates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_appViewModel.SelectedItem != null &&
                !_appViewModel.ManagedCertificates.Contains(_appViewModel.SelectedItem))
            {
                if (lvManagedCertificates.Items.Count == 0)
                {
                    _appViewModel.SelectedItem = null;
                    txtFilter.Focus();
                }
                else
                {
                    // selected item was deleted
                    var newIndex = lastSelectedIndex;

                    while (newIndex >= lvManagedCertificates.Items.Count && newIndex >= -1)
                    {
                        newIndex--;
                    }

                    SelectAndFocus(newIndex == -1 ? null : lvManagedCertificates.Items[newIndex]);
                }
            }

            lastSelectedIndex = lvManagedCertificates.SelectedIndex;
        }

        private void UserControl_OnLoaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);

            if (window != null) // null in XAML designer
            {
                KeyEventHandler p = (obj, args) =>
                                   {
                                       if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                                       {
                                           txtFilter.Focus();
                                           txtFilter.SelectAll();
                                       }
                                   };

                window.KeyDown -= p;
                window.KeyDown += p;
            }
        }

        private void SetListSortOrder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem)
            {
                var menu = sender as MenuItem;

                _sortOrder = menu.Tag.ToString();
                SetFilter();
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await _appViewModel.RefreshManagedCertificates();
        }

        private void Duplicate_Click(object sender, RoutedEventArgs e)
        {
            if (OnDuplicate != null)
            {
                var selectedItem = lvManagedCertificates.SelectedItem;
                if (selectedItem != null && selectedItem is Certify.Models.ManagedCertificate)
                {
                    OnDuplicate.Invoke(selectedItem as Certify.Models.ManagedCertificate);
                }
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_appViewModel.IsFeatureEnabled(Models.FeatureFlags.SERVER_CONNECTIONS))
            {
                _appViewModel.ChooseConnection(this);
            }
        }

        private void GettingStarted_FilterApplied(string filter)
        {
            txtFilter.Text = filter;
        }

        private async void Prev_Click(object sender, RoutedEventArgs e)
        {
            await _appViewModel.ManagedCertificatesPrevPage();
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            await _appViewModel.ManagedCertificatesNextPage();
        }
    }

    public static class StringExtensions
    {
        // older .net doesn't have string.Contains  https://learn.microsoft.com/en-us/dotnet/api/system.string.contains?view=net-7.0

        public static bool Contains(this String str, String substring,
                                    StringComparison comp)
        {
            if (substring == null)
            {
                throw new ArgumentNullException("substring",
                                             "substring cannot be null.");
            }
            else if (!Enum.IsDefined(typeof(StringComparison), comp))
            {
                throw new ArgumentException("comp is not a member of StringComparison",
                                         "comp");
            }

            return str.IndexOf(substring, comp) >= 0;
        }
    }
}
