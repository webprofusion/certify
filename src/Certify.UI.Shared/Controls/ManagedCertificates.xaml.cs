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

        private bool _isLoadingMore = false;
        private bool _hasMoreResults = true;
        private readonly object _loadLock = new object();

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

            // Attach scroll event handler for incremental loading
            lvManagedCertificates.Loaded += (s, e) =>
        {
            var scrollViewer = FindScrollViewer(lvManagedCertificates);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
        };
        }

        private void AppViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
 {
     if (e.PropertyName == "ManagedCertificates" || (e.PropertyName == "SelectedItem" &&
_appViewModel.ManagedCertificates != null))
     {
         SetFilter(); // reset listeners when ManagedCertificates are reset
         _itemViewModel.RaisePropertyChangedEvent("SelectedItem");
         _itemViewModel.RaisePropertyChangedEvent("IsSelectedItemValid");
     }
 });
        }

        /// <summary>
        /// Find ScrollViewer in visual tree
        /// </summary>
        private ScrollViewer FindScrollViewer(DependencyObject obj)
        {
            if (obj is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                var result = FindScrollViewer(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// Handle scroll event to load more results when near bottom
        /// </summary>
        private async void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;

            if (scrollViewer == null)
            {
                return;
            }

            // Check if we're near the bottom (within 100 pixels)
            var threshold = 100;
            var scrollPosition = scrollViewer.VerticalOffset + scrollViewer.ViewportHeight;
            var scrollHeight = scrollViewer.ExtentHeight;

            if (scrollPosition >= scrollHeight - threshold && !_isLoadingMore && _hasMoreResults)
            {
                await LoadMoreResults();
            }
        }

        /// <summary>
        /// Load the next batch of results
        /// </summary>
        private async Task LoadMoreResults()
        {
            lock (_loadLock)
            {
                if (_isLoadingMore)
                {
                    return; // Already loading
                }

                _isLoadingMore = true;
            }

            try
            {
                _appViewModel.IsLoadingMore = true;

                var previousCount = _appViewModel.ManagedCertificates?.Count ?? 0;

                // Load next page of results
                await _appViewModel.LoadNextManagedCertificatesPage();

                var newCount = _appViewModel.ManagedCertificates?.Count ?? 0;

                // If no new items were added, we've reached the end
                _hasMoreResults = newCount > previousCount;
            }
            finally
            {
                _appViewModel.IsLoadingMore = false;
                _isLoadingMore = false;
            }
        }

        private void SetFilter()
        {
            Dispatcher.Invoke(() =>
            {
                // Remove client-side filtering - all filtering is now done server-side via RefreshManagedCertificates
                // The filter keyword is set in _appViewModel.FilterKeyword and passed to the database query

                var view = CollectionViewSource.GetDefaultView(_appViewModel.ManagedCertificates);

                // Clear any existing filter
                view.Filter = null;

                //sort by name ascending
                view.SortDescriptions.Clear();

                if (_sortOrder == "NameAsc")
                {
                    view.SortDescriptions.Add(
                         new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending)
           );
                }

                if (_sortOrder == "ExpiryDateAsc")
                {
                    view.SortDescriptions.Add(
             new System.ComponentModel.SortDescription("DateExpiry", System.ComponentModel.ListSortDirection.Ascending)
                     );
                }
            });
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
            // Set filter keyword which will be used in the next database query
            var filterKeyword = txtFilter.Text.Trim();
            string filterHealth = null;
            if (filterKeyword.StartsWith("[Status="))
            {
                filterHealth = filterKeyword.Substring(filterKeyword.IndexOf("=")+1).TrimEnd(']').ToLower();
                filterKeyword = null;
            }
            
            _appViewModel.FilterKeyword = filterKeyword;
            _appViewModel.FilterHealth = filterHealth;

            // Reset for new search
            _hasMoreResults = true;

            // Refresh results from server with new filter
            await _filterDebouncer.Debounce(_appViewModel.RefreshManagedCertificates);

            // No need to refresh the view as the collection is replaced entirely from the server
        }

        private async void TxtFilter_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || string.IsNullOrWhiteSpace(txtFilter.Text))
            {
                ResetFilter();
                return;
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

        private async void ResetFilter()
        {
            _appViewModel.FilterKeyword = string.Empty;

            txtFilter.Text = "";
            txtFilter.Focus();

            // Reset for new search
            _hasMoreResults = true;

            // Reload all results from server when filter is cleared
            await _appViewModel.RefreshManagedCertificates();

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
            _hasMoreResults = true;
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
