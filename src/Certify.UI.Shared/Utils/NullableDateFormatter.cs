using System;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class NullableDateFormatter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return "<not set>";
            }

            return ((DateTimeOffset)value).ToLocalTime().ToString("yyyy-MM-dd");
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
    }

    /// <summary>
    /// Converts a DateTimeOffset (typically stored/transmitted as UTC) into a local-time DateTime so it can be
    /// formatted for display (e.g. via StringFormat) without leaving the value in UTC.
    /// </summary>
    public class LocalDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is DateTimeOffset dto)
            {
                return dto.ToLocalTime().DateTime;
            }

            // a boxed Nullable<DateTimeOffset> with a value unboxes directly to DateTimeOffset above;
            // only a null value (Nullable<DateTimeOffset> with no value) reaches here.
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
    }
}
