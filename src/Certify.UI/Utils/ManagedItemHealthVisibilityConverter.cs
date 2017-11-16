using Certify.Models;
using System;
using System.Windows;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class ManagedItemHealthVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return DependencyProperty.UnsetValue;

            var health = (ManagedItemHealth)value;

            if (health == ManagedItemHealth.Unknown)
            {
                return Visibility.Collapsed;
            }
            else if (health == ManagedItemHealth.Error)
            {
                return Visibility.Visible;
            }
            else if (health == ManagedItemHealth.Warning)
            {
                return Visibility.Visible;
            }
            else
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }
}