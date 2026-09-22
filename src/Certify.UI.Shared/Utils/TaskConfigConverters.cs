using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Data;
using Certify.Config;
using Certify.Models.Config;

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

    /// <summary>
    /// Converts a credential type to the list of stored credentials of that type, with a leading "(None)" option (StorageKey "(Empty)", see NullValueConverter)
    /// </summary>
    public class StoredCredentialOptionsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var options = new List<StoredCredential> { new StoredCredential { StorageKey = "(Empty)", Title = "(None)" } };

            var credentials = ViewModel.AppViewModel.Current.StoredCredentials;
            if (credentials != null)
            {
                options.AddRange(credentials.Where(c => c.ProviderType == value as string));
            }

            return options;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }
}
