using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.UI.Windows
{
    public class EditMaintenanceWindowViewModel : BindableBase
    {
        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
        public MaintenanceWindow EditWindow { get; set; }
        public bool IsValid => !string.IsNullOrWhiteSpace(EditWindow?.Name) && EditWindow?.Days != MaintenanceDays.None;
    }

    public class TimeZoneOption
    {
        public TimeZoneInfo TimeZone { get; set; }
        public string DisplayName => TimeZone?.DisplayName ?? "(System Default)";
        public string Id => TimeZone?.Id;
    }

    public partial class EditMaintenanceWindow
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;
        protected EditMaintenanceWindowViewModel EditViewModel = new EditMaintenanceWindowViewModel();
        private bool _isNew;

        public MaintenanceWindow Window => EditViewModel.EditWindow;

        public EditMaintenanceWindow(MaintenanceWindow window, bool isNew)
        {
            EditViewModel.EditWindow = window;
            _isNew = isNew;

            InitializeComponent();
            Width *= MainViewModel.UIScaleFactor;
            Height *= MainViewModel.UIScaleFactor;

            DataContext = EditViewModel;

            Title = isNew ? "Add Maintenance Window" : "Edit Maintenance Window";

            LoadSettings();
        }

        private void LoadSettings()
        {
            WindowName.Text = EditViewModel.EditWindow.Name;
            WindowDescription.Text = EditViewModel.EditWindow.Description;

            UpdateDayButtons();

            // Set time values
            StartHour.Value = EditViewModel.EditWindow.StartTime.Hours;
            StartMinute.Value = EditViewModel.EditWindow.StartTime.Minutes;
            EndHour.Value = EditViewModel.EditWindow.EndTime.Hours;
            EndMinute.Value = EditViewModel.EditWindow.EndTime.Minutes;

            // Load timezones with wrapper for proper display
            var timeZoneOptions = new List<TimeZoneOption>
            {
                new TimeZoneOption { TimeZone = null } // System Default option
            };

            timeZoneOptions.AddRange(TimeZoneInfo.GetSystemTimeZones()
                .Select(tz => new TimeZoneOption { TimeZone = tz }));

            TimeZoneSelector.ItemsSource = timeZoneOptions;

            // Select the appropriate timezone
            if (!string.IsNullOrEmpty(EditViewModel.EditWindow.TimeZoneId))
            {
                try
                {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(EditViewModel.EditWindow.TimeZoneId);
                    TimeZoneSelector.SelectedItem = timeZoneOptions.FirstOrDefault(opt => opt.TimeZone?.Id == tz.Id);
                }
                catch
                {
                    TimeZoneSelector.SelectedIndex = 0; // Default to System Default
                }
            }
            else
            {
                TimeZoneSelector.SelectedIndex = 0; // System Default
            }

            IsEnabled.IsChecked = EditViewModel.EditWindow.IsEnabled;

            UpdateSchedulePreview();
            UpdateOvernightWarning();
        }

        private void UpdateDayButtons()
        {
            DaySunday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Sunday);
            DayMonday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Monday);
            DayTuesday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Tuesday);
            DayWednesday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Wednesday);
            DayThursday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Thursday);
            DayFriday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Friday);
            DaySaturday.IsChecked = EditViewModel.EditWindow.Days.HasFlag(MaintenanceDays.Saturday);
        }

        private void DayToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton button && button.Tag is string dayName)
            {
                var day = (MaintenanceDays)Enum.Parse(typeof(MaintenanceDays), dayName);

                if (button.IsChecked == true)
                {
                    EditViewModel.EditWindow.Days |= day;
                }
                else
                {
                    EditViewModel.EditWindow.Days &= ~day;
                }

                UpdateSchedulePreview();
            }
        }

        private void SetAllDays_Click(object sender, RoutedEventArgs e)
        {
            EditViewModel.EditWindow.Days = MaintenanceDays.All;
            UpdateDayButtons();
            UpdateSchedulePreview();
        }

        private void SetWeekdays_Click(object sender, RoutedEventArgs e)
        {
            EditViewModel.EditWindow.Days = MaintenanceDays.Weekdays;
            UpdateDayButtons();
            UpdateSchedulePreview();
        }

        private void SetWeekends_Click(object sender, RoutedEventArgs e)
        {
            EditViewModel.EditWindow.Days = MaintenanceDays.Weekends;
            UpdateDayButtons();
            UpdateSchedulePreview();
        }

        private void SetNoDays_Click(object sender, RoutedEventArgs e)
        {
            EditViewModel.EditWindow.Days = MaintenanceDays.None;
            UpdateDayButtons();
            UpdateSchedulePreview();
        }

        private void Time_ValueChanged(object sender, Wpf.Ui.Controls.NumberBoxValueChangedEventArgs e)
        {
            if (StartHour?.Value == null || StartMinute?.Value == null || EndHour?.Value == null || EndMinute?.Value == null)
            {
                return;
            }

            EditViewModel.EditWindow.StartTime = new TimeSpan((int)StartHour.Value, (int)StartMinute.Value, 0);
            EditViewModel.EditWindow.EndTime = new TimeSpan((int)EndHour.Value, (int)EndMinute.Value, 0);

            UpdateSchedulePreview();
            UpdateOvernightWarning();
        }

        private void TimeZoneSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (TimeZoneSelector == null || EditViewModel?.EditWindow == null)
            {
                return;
            }

            if (TimeZoneSelector.SelectedItem is TimeZoneOption option)
            {
                // Set timezone ID (null for System Default)
                EditViewModel.EditWindow.TimeZoneId = option.TimeZone?.Id;
            }
            else
            {
                // Clear timezone when nothing is selected
                EditViewModel.EditWindow.TimeZoneId = null;
            }

            UpdateSchedulePreview();
        }

        private void UpdateSchedulePreview()
        {
            if (SchedulePreview != null && EditViewModel.EditWindow != null)
            {
                SchedulePreview.Text = EditViewModel.EditWindow.GetScheduleDescription();
            }
        }

        private void UpdateOvernightWarning()
        {
            if (OvernightWarning != null && EditViewModel.EditWindow != null)
            {
                if (EditViewModel.EditWindow.EndTime < EditViewModel.EditWindow.StartTime)
                {
                    OvernightWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    OvernightWarning.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (EditViewModel.IsValid)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(
                    "Please provide a name and select at least one day.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
