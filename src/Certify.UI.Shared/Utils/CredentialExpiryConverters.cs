using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Certify.UI.Utils
{
    /// <summary>
    /// Converts a stored credential expiry date to a brush, highlighting expired items as an error colour and soon to expire items as a warning colour
    /// </summary>
    public class CredentialExpiryColourConverter : IValueConverter
    {
        /// <summary>
        /// Number of days before expiry that a credential is considered to be expiring soon
        /// </summary>
        public int WarningDays { get; set; } = 30;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => GetColour(value as DateTimeOffset?, WarningDays);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;

        public static Brush GetColour(DateTimeOffset? dateExpiry, int warningDays)
        {
            if (dateExpiry == null)
            {
                return GetDefaultBrush();
            }

            var timeRemaining = dateExpiry.Value - DateTimeOffset.UtcNow;

            if (timeRemaining.TotalSeconds <= 0)
            {
                return Brushes.DarkRed;
            }
            else if (timeRemaining.TotalDays <= warningDays)
            {
                return Brushes.Chocolate;
            }
            else
            {
                return GetDefaultBrush();
            }
        }

        private static Brush GetDefaultBrush()
        {
            try
            {
                return (Brush)ViewModel.AppViewModel.Current.GetApplication().Resources["MahApps.Brushes.SystemControlForegroundBaseMediumHigh"];
            }
            catch
            {
                //unit test may not reference MahApps
                return Brushes.Gray;
            }
        }
    }
}
