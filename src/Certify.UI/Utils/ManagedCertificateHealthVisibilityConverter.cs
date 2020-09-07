using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
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

    public class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null)
            {
                if (value.ToString() == RequestState.Error.ToString())
                {
                    return new SolidColorBrush(System.Windows.Media.Colors.Red);
                }
                if (value.ToString() == RequestState.Success.ToString())
                {
                    return new SolidColorBrush(System.Windows.Media.Colors.Green);
                }
            }

            return new SolidColorBrush(System.Windows.Media.Colors.DarkGray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
