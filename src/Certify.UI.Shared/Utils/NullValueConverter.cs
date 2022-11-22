using System;
using System.Globalization;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    // https://stackoverflow.com/questions/518579/why-cant-i-select-a-null-value-in-a-combobox
    // used because combobox bindings can't be null even if null is an option in the itemsource
    public class NullValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // for null or empty strings replace the binding value with our chosen substitue param value
            if (value == null || value.ToString() == "")
            {
                return parameter;
            }
            else
            {
                return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value.ToString() == "")
            {
                return null;
            }

            return value.Equals(parameter) ? null : value;
        }
    }
}
