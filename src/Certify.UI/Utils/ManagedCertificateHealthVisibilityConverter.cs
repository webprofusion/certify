using System;
using System.Windows;
using System.Windows.Data;
using Certify.Models;

namespace Certify.UI.Utils
{
    public class ManagedCertificateHealthVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return DependencyProperty.UnsetValue;
            }

            var health = (ManagedCertificateHealth)value;

            if (health == ManagedCertificateHealth.Unknown)
            {
                return Visibility.Collapsed;
            }
            else if (health == ManagedCertificateHealth.Error)
            {
                return Visibility.Visible;
            }
            else if (health == ManagedCertificateHealth.Warning)
            {
                return Visibility.Visible;
            }
            else
            {
                return Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
    }
}
