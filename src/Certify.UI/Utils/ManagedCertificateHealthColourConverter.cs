using System;
using System.Windows;
using System.Windows.Data;
using Certify.Models;

namespace Certify.UI.Utils
{
    public class ManagedCertificateHealthColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return DependencyProperty.UnsetValue;

            return GetColour((ManagedCertificateHealth)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static System.Windows.Media.Brush GetColour(ManagedCertificateHealth health)
        {
            if (health == ManagedCertificateHealth.Unknown)
            {
                return System.Windows.Media.Brushes.SlateGray;
            }
            else if (health == ManagedCertificateHealth.Error)
            {
                return System.Windows.Media.Brushes.Red;
            }
            else if (health == ManagedCertificateHealth.Warning)
            {
                return System.Windows.Media.Brushes.OrangeRed;
            }
            else if (health == ManagedCertificateHealth.AwaitingUser)
            {
                return System.Windows.Media.Brushes.HotPink;
            }
            else
            {
                return System.Windows.Media.Brushes.Green;
            }
        }
    }
}
