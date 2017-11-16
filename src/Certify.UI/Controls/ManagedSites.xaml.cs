using Certify.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        protected ViewModel.AppModel MainViewModel => ViewModel.AppModel.AppViewModel;

        public ManagedSites()
        {
            InitializeComponent();
            DataContext = MainViewModel;

            SetFilter(); // start listening
            MainViewModel.PropertyChanged += (obj, args) =>
            {
                if (args.PropertyName == "ManagedSites" || args.PropertyName == "SelectedItem" &&
                    MainViewModel.ManagedSites != null)
                {
                    SetFilter(); // reset listeners when ManagedSites are reset
                }
            };
        }

        private void SetFilter()
        {
            CollectionViewSource.GetDefaultView(MainViewModel.ManagedSites).Filter = (item) =>
            {
                string filter = txtFilter.Text.Trim();
                return filter == "" || filter.Split(';').Where(f => f.Trim() != "").Any(f =>
                    ((Models.ManagedSite)item).Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1 ||
                    (((Models.ManagedSite)item).DomainOptions?.Any(d => d.Domain.IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1) ?? false) ||
                    (((Models.ManagedSite)item).Comments ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) > -1);
            };

            //sort by name ascending
            CollectionViewSource.GetDefaultView(MainViewModel.ManagedSites).SortDescriptions.Clear();
            CollectionViewSource.GetDefaultView(MainViewModel.ManagedSites).SortDescriptions.Add(
                new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending)
            );
        }

        private async void ListViewItem_InteractionEvent(object sender, InputEventArgs e)
        {
            var item = (ListViewItem)sender;
            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            var site = (ManagedSite)item.DataContext;
            site = site == MainViewModel.SelectedItem && ctrl ? null : site;
            if (MainViewModel.SelectedItem != site)
            {
                if (await MainViewModel.ConfirmDiscardUnsavedChanges())
                {
                    SelectAndFocus(site);
                }
                e.Handled = true;
            }
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var defaultView = CollectionViewSource.GetDefaultView(lvManagedSites.ItemsSource);
            defaultView.Refresh();
            if (lvManagedSites.SelectedIndex == -1 && MainViewModel.SelectedItem != null)
            {
                // if the data model's selected item has come into view after filter box text
                // changed, select the item in the list
                if (defaultView.Filter(MainViewModel.SelectedItem))
                {
                    lvManagedSites.SelectedItem = MainViewModel.SelectedItem;
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
                    int index = lvManagedSites.Items.IndexOf(MainViewModel.SelectedItem);
                    var item = lvManagedSites.Items[index == -1 ? 0 : index];

                    // if navigating away, confirm discard
                    if (item != MainViewModel.SelectedItem &&
                        !await MainViewModel.ConfirmDiscardUnsavedChanges())
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
            MainViewModel.SelectedItem = obj as ManagedSite;
            if (lvManagedSites.Items.Count > 0 && lvManagedSites.Items.Contains(MainViewModel.SelectedItem))
            {
                lvManagedSites.UpdateLayout(); // ensure containers exist
                if (lvManagedSites.ItemContainerGenerator.ContainerFromItem(MainViewModel.SelectedItem) is ListViewItem item)
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
                await MainViewModel.DeleteManagedSite(MainViewModel.SelectedItem);
                if (lvManagedSites.Items.Count > 0)
                {
                    SelectAndFocus(lvManagedSites.SelectedItem);
                }
                return;
            }
            object next = MainViewModel.SelectedItem;
            var item = ((ListViewItem)sender);
            int index = lvManagedSites.Items.IndexOf(item.DataContext);
            if (e.Key == Key.Enter)
            {
                next = item.DataContext;
            }
            var ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (e.Key == Key.Space)
            {
                next = MainViewModel.SelectedItem != null && ctrl ? null : item.DataContext;
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
            if (next != MainViewModel.SelectedItem)
            {
                if (await MainViewModel.ConfirmDiscardUnsavedChanges())
                {
                    SelectAndFocus(next);
                }
                e.Handled = true;
            }
        }

        private int lastSelectedIndex = -1;

        private void lvManagedSites_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainViewModel.SelectedItem != null &&
                !MainViewModel.ManagedSites.Contains(MainViewModel.SelectedItem))
            {
                if (lvManagedSites.Items.Count == 0)
                {
                    MainViewModel.SelectedItem = null;
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
    }
}