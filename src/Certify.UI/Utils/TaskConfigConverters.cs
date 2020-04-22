using System;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Certify.Config;
using Certify.Locales;

namespace Certify.UI.Utils
{
    public class TaskTriggerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }
            
            return DeploymentTaskTypes.TriggerTypes.FirstOrDefault(t => t.Key == (TaskTriggerType)value).Value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }

    public class TaskTargetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }

            return DeploymentTaskTypes.TargetTypes.FirstOrDefault(t => t.Key == (string)value).Value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
   
    }
}
