using System;
using System.Windows;
using System.Windows.Data;
using Certify.Models;

namespace Certify.UI.Utils
{
    public class ManagedCertificateHealthColourConverter : IValueConverter
    {
        public virtual object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return DependencyProperty.UnsetValue;
            }

            return GetColour((ManagedCertificateHealth)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;

        public static System.Windows.Media.Brush GetColour(ManagedCertificateHealth health, string mode = "standard")
        {
            if (health == ManagedCertificateHealth.Unknown)
            {
                return System.Windows.Media.Brushes.SlateGray;
            }
            else if (health == ManagedCertificateHealth.Error)
            {
                return System.Windows.Media.Brushes.IndianRed;
            }
            else if (health == ManagedCertificateHealth.Warning)
            {
                return System.Windows.Media.Brushes.DarkOrange;
            }
            else if (health == ManagedCertificateHealth.AwaitingUser)
            {
                return System.Windows.Media.Brushes.HotPink;
            }
            else
            {
                if (mode == "standard")
                {
                    return (System.Windows.Media.Brush)App.Current.Resources["MahApps.Brushes.SystemControlForegroundBaseHigh"];
                }
                else
                {
                    return System.Windows.Media.Brushes.Green;
                }
            }
        }
    }

    public class ManagedCertificateHealthColourConverterEx : ManagedCertificateHealthColourConverter
    {
        public override object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return DependencyProperty.UnsetValue;
            }

            return GetColour((ManagedCertificateHealth)value, "ex");
        }
    }

}
