using Certify.Models;
using System;
using System.Windows;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class ManagedItemHealthColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return DependencyProperty.UnsetValue;

            return GetColour((ManagedItemHealth)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static System.Windows.Media.Brush GetColour(ManagedItemHealth health)
        {
            if (health == ManagedItemHealth.Unknown)
            {
                return System.Windows.Media.Brushes.SlateGray;
            }
            else if (health == ManagedItemHealth.Error)
            {
                return System.Windows.Media.Brushes.Red;
            }
            else if (health == ManagedItemHealth.Warning)
            {
                return System.Windows.Media.Brushes.OrangeRed;
            }
            else
            {
                return System.Windows.Media.Brushes.Green;
            }
        }
    }
}