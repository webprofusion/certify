using System;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class CSRAlgCheckedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
        {
            if (value != null && value.ToString() == parameter.ToString())
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture) => parameter;
    }
}
