using Certify.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Certify.UI.Controls
{
    /// <summary>
    /// Interaction logic for ManagedSites.xaml 
    /// </summary>
    public partial class ManagedSites
    {
        protected ViewModel.AppModel _appViewModel => ViewModel.AppModel.Current;
        protected ViewModel.ManagedItemModel _itemViewModel => ViewModel.ManagedItemModel.Current;

        private string _sortOrder { get; set; } = "NameAsc";

        public ManagedSites()
        {
            InitializeComponent();
            DataContext = _appViewModel;
            MainItemView.DataContext = _itemViewModel;

            SetFilter(); // start listening
            _appViewModel.PropertyChanged += (obj, args) =>
            {
                if (args.PropertyName == "ManagedSites" || args.PropertyName == "SelectedItem" &&
                    _appViewModel.ManagedSites != null)
                {
                    SetFilter(); // reset listeners when ManagedSites are reset
                    _itemViewModel.RaisePropertyChanged("SelectedItem");
                    _itemViewModel.RaisePropertyChanged("IsSelectedItemValid");
                }
            };
        }

        private void SetFilter()
        {
            CollectionViewSource.GetDefaultView(_appViewModel.ManagedSites).Filter = (item) =>
            {
                string filter = txtFilter.Text.Trim();
                return filter == "" || filter.Split(';').Where(f => f.Trim() != "").Any(f =>
                    ((Models.ManagedSite)item).Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1 ||
                    (((Models.ManagedSite)item).DomainOptions?.Any(d => d.Domain.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1) ?? false) ||
                    (((Models.ManagedSite)item).Comments ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1);
            };

            //sort by name ascending
            CollectionViewSource.GetDefaultView(_appViewModel.ManagedSites).SortDescriptions.Clear();

            if (_sortOrder == "NameAsc")
            {
                CollectionViewSource.GetDefaultView(_appViewModel.ManagedSites).SortDescriptions.Add(
                   new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending)
               );
            }

            if (_sortOrder == "ExpiryDateAsc")
            {
                CollectionViewSource.GetDefaultView(_appViewModel.ManagedSites).SortDescriptions.Add(
                   new System.ComponentModel.SortDescription("DateExpiry", System.ComponentModel.ListSortDirection.Ascending)
               );
            }
        }

        private async void ListViewItem_InteractionEvent(object sender, InputEventArgs e)
        {
            var item = (ListViewItem)sender;
            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (item != null && item.DataContext != null && item.DataContext is ManagedSite)
            {
                var site = (ManagedSite)item.DataContext;
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

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var defaultView = CollectionViewSource.GetDefaultView(lvManagedSites.ItemsSource);
            defaultView.Refresh();
            if (lvManagedSites.SelectedIndex == -1 && _appViewModel.SelectedItem != null)
            {
                // if the data model's selected item has come into view after filter box text
                // changed, select the item in the list
                if (defaultView.Filter(_appViewModel.SelectedItem))
                {
                    lvManagedSites.SelectedItem = _appViewModel.SelectedItem;
                }
            }
        }

        private async void TxtFilter_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) ResetFilter();
            if (e.Key == Key.Enter || e.Key == Key.Down)
            {
                if (lvManagedSites.Items.Count > 0)
                {
                    // get selected index of filtered list or 0
                    int index = lvManagedSites.Items.IndexOf(_appViewModel.SelectedItem);
                    var item = lvManagedSites.Items[index == -1 ? 0 : index];

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
            txtFilter.Text = "";
            txtFilter.Focus();
            if (lvManagedSites.SelectedItem != null)
            {
                lvManagedSites.ScrollIntoView(lvManagedSites.SelectedItem);
            }
        }

        private void SelectAndFocus(object obj)
        {
            _appViewModel.SelectedItem = obj as ManagedSite;
            if (lvManagedSites.Items.Count > 0 && lvManagedSites.Items.Contains(_appViewModel.SelectedItem))
            {
                lvManagedSites.UpdateLayout(); // ensure containers exist
                if (lvManagedSites.ItemContainerGenerator.ContainerFromItem(_appViewModel.SelectedItem) is ListViewItem item)
                {
                    item.Focus();
                    item.IsSelected = true;
                }
            }
        }

        private async void ListViewItem_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ResetFilter();
                return;
            }
            if (e.Key == Key.Delete && lvManagedSites.SelectedItem != null)
            {
                await _appViewModel.DeleteManagedSite(_appViewModel.SelectedItem);
                if (lvManagedSites.Items.Count > 0)
                {
                    SelectAndFocus(lvManagedSites.SelectedItem);
                }
                return;
            }
            object next = _appViewModel.SelectedItem;
            var item = ((ListViewItem)sender);
            int index = lvManagedSites.Items.IndexOf(item.DataContext);
            if (e.Key == Key.Enter)
            {
                next = item.DataContext;
            }
            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (e.Key == Key.Space)
            {
                next = _appViewModel.SelectedItem != null && ctrl ? null : item.DataContext;
            }
            if (e.Key == Key.Up)
            {
                next = lvManagedSites.Items[index - 1 > -1 ? index - 1 : 0];
            }
            if (e.Key == Key.Down)
            {
                next = lvManagedSites.Items[index + 1 < lvManagedSites.Items.Count ? index + 1 : lvManagedSites.Items.Count - 1];
            }
            if (e.Key == Key.Home)
            {
                next = lvManagedSites.Items[0];
            }
            if (e.Key == Key.End)
            {
                next = lvManagedSites.Items[lvManagedSites.Items.Count - 1];
            }
            if (e.Key == Key.PageUp)
            {
                int pagesize = (int)(lvManagedSites.ActualHeight / item.ActualHeight);
                next = lvManagedSites.Items[index - pagesize > -1 ? index - pagesize : 0];
            }
            if (e.Key == Key.PageDown)
            {
                int pagesize = (int)(lvManagedSites.ActualHeight / item.ActualHeight);
                next = lvManagedSites.Items[index + pagesize < lvManagedSites.Items.Count ? index + pagesize : lvManagedSites.Items.Count - 1];
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

        private void lvManagedSites_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_appViewModel.SelectedItem != null &&
                !_appViewModel.ManagedSites.Contains(_appViewModel.SelectedItem))
            {
                if (lvManagedSites.Items.Count == 0)
                {
                    _appViewModel.SelectedItem = null;
                    txtFilter.Focus();
                }
                else
                {
                    // selected item was deleted
                    int newIndex = lastSelectedIndex;
                    while (newIndex >= lvManagedSites.Items.Count && newIndex >= -1)
                    {
                        newIndex--;
                    }
                    SelectAndFocus(newIndex == -1 ? null : lvManagedSites.Items[newIndex]);
                }
            }
            lastSelectedIndex = lvManagedSites.SelectedIndex;
        }

        private void UserControl_OnLoaded(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) // null in XAML designer
            {
                window.KeyDown += (obj, args) =>
                {
                    if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        txtFilter.Focus();
                        txtFilter.SelectAll();
                    }
                };
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
    }
}