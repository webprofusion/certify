using System;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class DeploymentModeCheckedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
        {
            if (value != null)
            {
                var enumVals = parameter.ToString().Split('.');
                return value.Equals(enumVals[enumVals.Length - 1]);
            }
            else
            {
                return Models.SupportedDeploymentModes.SingleSiteBindingsMatchingDomains; //default
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            // fixme: this is a rubbish way to convert the enum from string
            var enumVals = parameter.ToString().Split('.');
            Models.SupportedDeploymentModes enumVal = (Models.SupportedDeploymentModes)Enum.Parse(typeof(Models.SupportedDeploymentModes), enumVals[enumVals.Length - 1]);
            return value.Equals(true) ? enumVal.ToString() : Binding.DoNothing;
        }
    }
}