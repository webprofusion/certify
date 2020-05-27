using System;
using System.Windows;
using System.Windows.Data;
using Certify.Locales;

namespace Certify.UI.Utils
{
    public class ExpiryDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return GetDescription((DateTime?)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static string GetDescription(DateTime? expiry)
        {
            if (expiry == null)
            {
                return SR.ExpiryDateConverter_NoCurrentCertificate;
            }

            var days = (int)(expiry - DateTime.Now).Value.TotalDays;

            if (days < 0)
            {
                return string.Format(SR.ExpiryDateConverter_CertificateExpiredNDaysAgo, -days);
            }
            else
            {
                return string.Format(SR.ExpiryDateConverter_CertificateExpiresIn, days);
            }

        }
    }

    public class ExpiryDateColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return GetColour((DateTime?)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static System.Windows.Media.Brush GetColour(DateTime? expiry)
        {
            if (expiry == null)
            {
                return System.Windows.Media.Brushes.SlateGray;
            }

            var days = (int)(expiry - DateTime.Now).Value.TotalDays;

            if (days < 0)
            {
                return System.Windows.Media.Brushes.DarkRed;
            }
            else if (days < 7)
            {
                return  System.Windows.Media.Brushes.IndianRed;
            }
            else if (days < 30)
            {
                return System.Windows.Media.Brushes.Orange;
            }
            else
            {
                return System.Windows.Media.Brushes.Green;
            }
        }
    }
}
