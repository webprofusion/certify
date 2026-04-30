using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Certify.UI.Controls.Fluent
{
    public class NumberBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double?), typeof(NumberBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.MinValue));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata(double.MaxValue));

        public static readonly DependencyProperty IntervalProperty =
            DependencyProperty.Register(nameof(Interval), typeof(double), typeof(NumberBox), new PropertyMetadata(1d));

        private bool _updatingText;

        public NumberBox()
        {
            TextChanged += NumberBox_TextChanged;
        }

        public event RoutedPropertyChangedEventHandler<double?> ValueChanged;

        public double? Value
        {
            get => (double?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, CoerceValue(value));
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

        private void NumberBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingText)
            {
                return;
            }

            if (double.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
            {
                Value = CoerceValue(value);
            }
        }

        private double? CoerceValue(double? value)
        {
            if (value == null)
            {
                return null;
            }

            return Math.Max(Minimum, Math.Min(Maximum, value.Value));
        }

        private void UpdateText()
        {
            _updatingText = true;
            Text = Value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            _updatingText = false;
        }
    }
}
