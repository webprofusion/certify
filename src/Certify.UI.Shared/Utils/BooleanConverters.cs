using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Certify.UI.Utils
{
    public class BooleanConverter<T> : IValueConverter
    {
        public BooleanConverter(T trueValue, T falseValue)
        {
            True = trueValue;
            False = falseValue;
        }

        public T True { get; set; }
        public T False { get; set; }

        public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool && ((bool)value) ? True : False;

        public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is T && EqualityComparer<T>.Default.Equals((T)value, True);
    }

    public class InverseBooleanConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
        {
            if (!(value is bool))
            {
                throw new InvalidOperationException("The target must be a boolean");
            }

            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter,
            System.Globalization.CultureInfo culture) => Convert(value, targetType, parameter, culture);

        #endregion IValueConverter Members
    }

    /// <summary>
    /// http://stackoverflow.com/questions/534575/how-do-i-invert-booleantovisibilityconverter
    /// </summary>
    public sealed class OptionalBooleanToVisibilityConverter : BooleanConverter<Visibility>
    {
        public OptionalBooleanToVisibilityConverter() :
            base(Visibility.Visible, Visibility.Collapsed)
        { }
    }

    public class NullVisibilityConverter : IValueConverter
    {
        public Visibility Null { get; set; } = Visibility.Collapsed;
        public Visibility NotNull { get; set; } = Visibility.Visible;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value == null || string.IsNullOrEmpty(value?.ToString())) ? Null : NotNull;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }


    /// <summary>
    /// If given feature (string) is enabled return the required Visibility
    /// </summary>
    public class FeatureVisibilityConverter : IValueConverter
    {
        public Visibility WhenEnabled { get; set; } = Visibility.Visible;
        public Visibility WhenNotEnabled { get; set; } = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter != null)
            {
                var featureFlag = parameter.ToString();
                if (UI.ViewModel.AppViewModel.Current.IsFeatureEnabled(featureFlag))
                {
                    return WhenEnabled;
                }
                else
                {
                    return WhenNotEnabled;
                }
            }
            else
            {
                return WhenNotEnabled;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }


    /// <summary>
    /// If given feature (string) is enabled return true
    /// </summary>
    public class FeatureBooleanConverter : IValueConverter
    {
        public bool WhenEnabled { get; set; } = true;
        public bool WhenNotEnabled { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter != null)
            {
                var featureFlag = parameter.ToString();
                if (UI.ViewModel.AppViewModel.Current.IsFeatureEnabled(featureFlag))
                {
                    return WhenEnabled;
                }
                else
                {
                    return WhenNotEnabled;
                }
            }
            else
            {
                return WhenNotEnabled;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
