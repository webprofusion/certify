using System;
using System.Windows.Data;
using Certify.Locales;
using Certify.Models;

namespace Certify.UI.Utils
{
    public class ExpiryDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return GetDescription((Lifetime)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static string GetDescription(Lifetime lifetime)
        {
            if (lifetime == null)
            {
                return SR.ExpiryDateConverter_NoCurrentCertificate;
            }

            var ageSpan = lifetime.DateEnd - DateTimeOffset.UtcNow;
            var days = (int)ageSpan.TotalDays;

            if (ageSpan.TotalHours < 0)
            {
                if (ageSpan.TotalHours < -24)
                {
                    return string.Format(SR.ExpiryDateConverter_CertificateExpiredNDaysAgo, -days);
                }
                else
                {
                    return string.Format(SR.ExpiryDateConverter_CertificateExpiredNHoursAgo, Math.Round(-ageSpan.TotalHours, 1));
                }
            }
            else
            {
                if (ageSpan.TotalHours < 24)
                {
                    // hrs
                    return string.Format(SR.ExpiryDateConverter_CertificateExpiresInNHours, Math.Round(ageSpan.TotalHours, 1));
                }
                else
                {

                    // days
                    return string.Format(SR.ExpiryDateConverter_CertificateExpiresIn, days);
                }
            }
        }
    }

    public class ExpiryDateColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return GetColour((Lifetime)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public static System.Windows.Media.Brush GetColour(Lifetime lifetime)
        {

            if (lifetime == null)
            {
                return System.Windows.Media.Brushes.SlateGray;
            }

            var percentageElapsed = lifetime?.GetPercentageElapsed(DateTimeOffset.UtcNow);

            if (percentageElapsed > LifetimeHealthThresholds.PercentageDanger)
            {
                return System.Windows.Media.Brushes.DarkRed;
            }
            else if (percentageElapsed > LifetimeHealthThresholds.PercentageWarning)
            {
                return System.Windows.Media.Brushes.Chocolate;
            }
            else
            {
                try
                {
                    return (System.Windows.Media.Brush)ViewModel.AppViewModel.Current.GetApplication().Resources["MahApps.Brushes.SystemControlForegroundBaseMediumHigh"];
                }
                catch
                {
                    //unit test may not reference MahApps
                    return System.Windows.Media.Brushes.Green;
                }
            }
        }
    }
}
