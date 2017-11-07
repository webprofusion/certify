using Certify.Locales;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class ExpiryDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return DependencyProperty.UnsetValue;

            return GetDescription((DateTime?)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static string GetDescription(DateTime? expiry)
        {
            if (expiry == null) return SR.ExpiryDateConverter_NoCurrentCertificate;

            var days = (int)Math.Abs((DateTime.Now - expiry).Value.TotalDays);
            return String.Format(SR.ExpiryDateConverter_CertificateExpiresIn, days);
        }
    }

    public class ExpiryDateColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return DependencyProperty.UnsetValue;

            return GetColour((DateTime?)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static System.Windows.Media.Brush GetColour(DateTime? expiry)
        {
            if (expiry == null) return System.Windows.Media.Brushes.SlateGray;

            var days = (int)Math.Abs((DateTime.Now - expiry).Value.TotalDays);

            if (days < 7)
            {
                return System.Windows.Media.Brushes.Red;
            }
            else if (days < 14)
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