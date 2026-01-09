using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Certify.Models;
using Certify.Models.Config;
using Certify.UI.Shared;

namespace Certify.UI.Controls.Settings
{
    public partial class MaintenanceWindows : UserControl
    {
        public class Model : BindableBase
        {
            public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
            public Preferences Prefs => MainViewModel.Preferences;

            public bool SettingsInitialised { get; set; }
            public ObservableCollection<MaintenanceWindowViewModel> WindowsList { get; set; } = new ObservableCollection<MaintenanceWindowViewModel>();
        }

        public class MaintenanceWindowViewModel
        {
            public MaintenanceWindow Window { get; set; }
            public string Id => Window.Id;
            public string Name => Window.Name;
            public string ScheduleDescription => Window.GetScheduleDescription();
            public string StatusText => Window.IsEnabled ? "Enabled" : "Disabled";
            public string DisplayText => $"{Name} - {ScheduleDescription}";

            public MaintenanceWindowViewModel(MaintenanceWindow window)
            {
                Window = window;
            }
        }

        public Model EditModel { get; set; } = new Model();

        public MaintenanceWindows()
        {
            InitializeComponent();
            DataContext = EditModel;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!EditModel.MainViewModel.IsServiceAvailable)
            {
                return;
            }

            LoadSettings();
        }

        private void LoadSettings()
        {
            EditModel.WindowsList.Clear();

            if (EditModel.Prefs.MaintenanceWindows != null)
            {
                foreach (var window in EditModel.Prefs.MaintenanceWindows)
                {
                    EditModel.WindowsList.Add(new MaintenanceWindowViewModel(window));
                }
            }

            RefreshWindowsList();
            RefreshDefaultWindowSelector();

            EditModel.SettingsInitialised = true;
        }

        private void RefreshWindowsList()
        {
            MaintenanceWindowsList.ItemsSource = EditModel.WindowsList;

            if (EditModel.WindowsList.Count == 0)
            {
                NoWindowsMessage.Visibility = Visibility.Visible;
                MaintenanceWindowsList.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoWindowsMessage.Visibility = Visibility.Collapsed;
                MaintenanceWindowsList.Visibility = Visibility.Visible;
            }
        }

        private void RefreshDefaultWindowSelector()
        {
            var items = new List<MaintenanceWindowViewModel>
            {
                new MaintenanceWindowViewModel(new MaintenanceWindow { Id = null, Name = "(No restriction - renew anytime)" })
            };

            items.AddRange(EditModel.WindowsList.Where(w => w.Window.IsEnabled));

            DefaultMaintenanceWindow.ItemsSource = items;
            DefaultMaintenanceWindow.SelectedValue = EditModel.Prefs.DefaultMaintenanceWindowId;
        }

        private async void AddMaintenanceWindow_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new MaintenanceWindow
            {
                Name = "New Maintenance Window",
                Days = MaintenanceDays.All,
                StartTime = TimeSpan.FromHours(18),
                EndTime = TimeSpan.FromHours(21),
                IsEnabled = true
            };

            var dialog = new Windows.EditMaintenanceWindow(newWindow, true)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                if (EditModel.Prefs.MaintenanceWindows == null)
                {
                    EditModel.Prefs.MaintenanceWindows = new List<MaintenanceWindow>();
                }

                EditModel.Prefs.MaintenanceWindows.Add(newWindow);
                await EditModel.MainViewModel.SavePreferences();

                EditModel.WindowsList.Add(new MaintenanceWindowViewModel(newWindow));
                RefreshWindowsList();
                RefreshDefaultWindowSelector();
            }
        }

        private async void EditMaintenanceWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MaintenanceWindowViewModel vm)
            {
                // Create a copy to edit
                var editWindow = new MaintenanceWindow
                {
                    Id = vm.Window.Id,
                    Name = vm.Window.Name,
                    Description = vm.Window.Description,
                    Days = vm.Window.Days,
                    StartTime = vm.Window.StartTime,
                    EndTime = vm.Window.EndTime,
                    IsEnabled = vm.Window.IsEnabled,
                    TimeZoneId = vm.Window.TimeZoneId
                };

                var dialog = new Windows.EditMaintenanceWindow(editWindow, false)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    // Update the original window with edited values
                    vm.Window.Name = editWindow.Name;
                    vm.Window.Description = editWindow.Description;
                    vm.Window.Days = editWindow.Days;
                    vm.Window.StartTime = editWindow.StartTime;
                    vm.Window.EndTime = editWindow.EndTime;
                    vm.Window.IsEnabled = editWindow.IsEnabled;
                    vm.Window.TimeZoneId = editWindow.TimeZoneId;

                    await EditModel.MainViewModel.SavePreferences();

                    // Refresh the display
                    LoadSettings();
                }
            }
        }

        private async void DeleteMaintenanceWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is MaintenanceWindowViewModel vm)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the maintenance window '{vm.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    EditModel.Prefs.MaintenanceWindows?.Remove(vm.Window);
                    
                    if (EditModel.Prefs.DefaultMaintenanceWindowId == vm.Id)
                    {
                        EditModel.Prefs.DefaultMaintenanceWindowId = null;
                    }

                    await EditModel.MainViewModel.SavePreferences();

                    EditModel.WindowsList.Remove(vm);
                    RefreshWindowsList();
                    RefreshDefaultWindowSelector();
                }
            }
        }

        private async void SaveDefaultWindow_Click(object sender, RoutedEventArgs e)
        {
            await EditModel.MainViewModel.SavePreferences();
            MessageBox.Show("Default maintenance window saved.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DefaultMaintenanceWindow_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!EditModel.SettingsInitialised)
            {
                return;
            }

            if (DefaultMaintenanceWindow.SelectedItem is MaintenanceWindowViewModel vm)
            {
                EditModel.Prefs.DefaultMaintenanceWindowId = vm.Id;
            }
        }
    }
}
