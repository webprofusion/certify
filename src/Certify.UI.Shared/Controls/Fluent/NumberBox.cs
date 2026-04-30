using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Certify.UI.Controls.Fluent
{
    public class NumberBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double?), typeof(NumberBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.MinValue, OnLimitChanged));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.MaxValue, OnLimitChanged));

        public static readonly DependencyProperty IntervalProperty =
            DependencyProperty.Register(nameof(Interval), typeof(double), typeof(NumberBox), new PropertyMetadata(1d));

        private bool _updatingText;

        public NumberBox()
        {
            TextChanged += NumberBox_TextChanged;
            PreviewKeyDown += NumberBox_PreviewKeyDown;
        }

        public event RoutedPropertyChangedEventHandler<double?> ValueChanged;

        public double? Value
        {
            get => (double?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Interval
        {
            get => (double)GetValue(IntervalProperty);
            set => SetValue(IntervalProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumberBox numberBox)
            {
                numberBox.UpdateText();
                numberBox.ValueChanged?.Invoke(numberBox, new RoutedPropertyChangedEventArgs<double?>((double?)e.OldValue, (double?)e.NewValue));
            }
        }

        private static void OnLimitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            d.CoerceValue(ValueProperty);
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            if (d is NumberBox numberBox && baseValue is double value)
            {
                return numberBox.CoerceValueWithinBounds(value);
            }

            return baseValue;
        }

        private void NumberBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingText)
            {
                return;
            }

            if (double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
            {
                Value = value;
            }
        }

        private void NumberBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down)
            {
                return;
            }

            var currentValue = Value ?? (Minimum != double.MinValue ? Minimum : 0);
            Value = currentValue + (e.Key == Key.Up ? Interval : -Interval);
            e.Handled = true;
        }

        private double CoerceValueWithinBounds(double value)
        {
            return Math.Max(Minimum, Math.Min(Maximum, value));
        }

        private void UpdateText()
        {
            _updatingText = true;
            Text = Value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            _updatingText = false;
        }
    }
}
