using System;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class CSRAlgCheckedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
        {
            if (value != null)
            {
                var enumVals = parameter.ToString().Split('.');
                return value.Equals(enumVals[enumVals.Length-1]);
            }
            else
            {
                return Models.SupportedCSRKeyAlgs.RS256; //default
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            // fixme: this is a rubbish way to convert the enum from string
            var enumVals = parameter.ToString().Split('.');
            Models.SupportedCSRKeyAlgs enumVal = (Models.SupportedCSRKeyAlgs)Enum.Parse(typeof(Models.SupportedCSRKeyAlgs), enumVals[enumVals.Length - 1]);
            return value.Equals(true) ? enumVal.ToString() : Binding.DoNothing;
        }
    }
}