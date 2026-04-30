using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MahApps.Metro.Controls
{
    public class MetroWindow : Window
    {
        public static readonly DependencyProperty TitleCharacterCasingProperty =
            DependencyProperty.Register(nameof(TitleCharacterCasing), typeof(string), typeof(MetroWindow));

        public static readonly DependencyProperty WindowButtonCommandsOverlayBehaviorProperty =
            DependencyProperty.Register(nameof(WindowButtonCommandsOverlayBehavior), typeof(string), typeof(MetroWindow));

        public static readonly DependencyProperty WindowTransitionsEnabledProperty =
            DependencyProperty.Register(nameof(WindowTransitionsEnabled), typeof(bool), typeof(MetroWindow), new PropertyMetadata(false));

        public static readonly DependencyProperty GlowBrushProperty =
            DependencyProperty.Register(nameof(GlowBrush), typeof(Brush), typeof(MetroWindow));

        public static readonly DependencyProperty ShowMaxRestoreButtonProperty =
            DependencyProperty.Register(nameof(ShowMaxRestoreButton), typeof(bool), typeof(MetroWindow), new PropertyMetadata(true));

        public static readonly DependencyProperty ShowMinButtonProperty =
            DependencyProperty.Register(nameof(ShowMinButton), typeof(bool), typeof(MetroWindow), new PropertyMetadata(true));

        public static readonly DependencyProperty FlyoutsProperty =
            DependencyProperty.Register(nameof(Flyouts), typeof(object), typeof(MetroWindow), new PropertyMetadata(null, OnFlyoutsChanged));

        public string TitleCharacterCasing
        {
            get => (string)GetValue(TitleCharacterCasingProperty);
            set => SetValue(TitleCharacterCasingProperty, value);
        }

        public string WindowButtonCommandsOverlayBehavior
        {
            get => (string)GetValue(WindowButtonCommandsOverlayBehaviorProperty);
            set => SetValue(WindowButtonCommandsOverlayBehaviorProperty, value);
        }

        public bool WindowTransitionsEnabled
        {
            get => (bool)GetValue(WindowTransitionsEnabledProperty);
            set => SetValue(WindowTransitionsEnabledProperty, value);
        }

        public Brush GlowBrush
        {
            get => (Brush)GetValue(GlowBrushProperty);
            set => SetValue(GlowBrushProperty, value);
        }

        public bool ShowMaxRestoreButton
        {
            get => (bool)GetValue(ShowMaxRestoreButtonProperty);
            set => SetValue(ShowMaxRestoreButtonProperty, value);
        }

        public bool ShowMinButton
        {
            get => (bool)GetValue(ShowMinButtonProperty);
            set => SetValue(ShowMinButtonProperty, value);
        }

        public object Flyouts
        {
            get => GetValue(FlyoutsProperty);
            set => SetValue(FlyoutsProperty, value);
        }

        private static void OnFlyoutsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MetroWindow window && e.NewValue is UIElement flyouts && window.Content is Panel panel && !panel.Children.Contains(flyouts))
            {
                if (flyouts is FrameworkElement frameworkElement)
                {
                    frameworkElement.HorizontalAlignment = HorizontalAlignment.Stretch;
                    frameworkElement.VerticalAlignment = VerticalAlignment.Stretch;
                }

                panel.Children.Add(flyouts);
            }
        }
    }

    public class FlyoutsControl : Grid
    {
    }

    public class Flyout : GroupBox
    {
        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(Flyout), new PropertyMetadata(false, OnIsOpenChanged));

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(nameof(Position), typeof(string), typeof(Flyout));

        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.Register(nameof(Theme), typeof(string), typeof(Flyout));

        public Flyout()
        {
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Stretch;
            Margin = new Thickness(8);
            Padding = new Thickness(8);
            Visibility = Visibility.Collapsed;
        }

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public string Position
        {
            get => (string)GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        public string Theme
        {
            get => (string)GetValue(ThemeProperty);
            set => SetValue(ThemeProperty, value);
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Flyout flyout)
            {
                flyout.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    public class NumericUpDown : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double?), typeof(NumericUpDown), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericUpDown), new PropertyMetadata(double.MinValue));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericUpDown), new PropertyMetadata(double.MaxValue));

        public static readonly DependencyProperty IntervalProperty =
            DependencyProperty.Register(nameof(Interval), typeof(double), typeof(NumericUpDown), new PropertyMetadata(1d));

        public static readonly DependencyProperty NumericInputModeProperty =
            DependencyProperty.Register(nameof(NumericInputMode), typeof(string), typeof(NumericUpDown));

        private bool _updatingText;

        public NumericUpDown()
        {
            TextChanged += NumericUpDown_TextChanged;
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

        public string NumericInputMode
        {
            get => (string)GetValue(NumericInputModeProperty);
            set => SetValue(NumericInputModeProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDown numeric)
            {
                numeric.UpdateText();
                numeric.ValueChanged?.Invoke(numeric, new RoutedPropertyChangedEventArgs<double?>((double?)e.OldValue, (double?)e.NewValue));
            }
        }

        private void NumericUpDown_TextChanged(object sender, TextChangedEventArgs e)
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

    public class ToggleSwitch : CheckBox
    {
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.Register(nameof(IsOn), typeof(bool?), typeof(ToggleSwitch), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOnChanged));

        public ToggleSwitch()
        {
            Checked += (_, _) =>
            {
                if (IsOn != true)
                {
                    IsOn = true;
                }
            };
            Unchecked += (_, _) =>
            {
                if (IsOn != false)
                {
                    IsOn = false;
                }
            };
        }

        public bool? IsOn
        {
            get => (bool?)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ToggleSwitch toggleSwitch)
            {
                var isOn = (bool?)e.NewValue;
                if (toggleSwitch.IsChecked != isOn)
                {
                    toggleSwitch.IsChecked = isOn;
                }
            }
        }
    }

    public class MetroProgressBar : ProgressBar
    {
    }

    public class MetroTabControl : TabControl
    {
    }

    public class FlipView : TabControl
    {
        public static readonly DependencyProperty IsBannerEnabledProperty =
            DependencyProperty.Register(nameof(IsBannerEnabled), typeof(bool), typeof(FlipView), new PropertyMetadata(true));

        public static readonly DependencyProperty MouseHoverBorderEnabledProperty =
            DependencyProperty.Register(nameof(MouseHoverBorderEnabled), typeof(bool), typeof(FlipView), new PropertyMetadata(true));

        public bool IsBannerEnabled
        {
            get => (bool)GetValue(IsBannerEnabledProperty);
            set => SetValue(IsBannerEnabledProperty, value);
        }

        public bool MouseHoverBorderEnabled
        {
            get => (bool)GetValue(MouseHoverBorderEnabledProperty);
            set => SetValue(MouseHoverBorderEnabledProperty, value);
        }
    }

    public class FlipViewItem : TabItem
    {
    }

    public static class TextBoxHelper
    {
        public static readonly DependencyProperty WatermarkProperty =
            DependencyProperty.RegisterAttached("Watermark", typeof(object), typeof(TextBoxHelper));

        public static readonly DependencyProperty ClearTextButtonProperty =
            DependencyProperty.RegisterAttached("ClearTextButton", typeof(bool), typeof(TextBoxHelper));

        public static object GetWatermark(DependencyObject obj) => obj.GetValue(WatermarkProperty);
        public static void SetWatermark(DependencyObject obj, object value) => obj.SetValue(WatermarkProperty, value);
        public static bool GetClearTextButton(DependencyObject obj) => (bool)obj.GetValue(ClearTextButtonProperty);
        public static void SetClearTextButton(DependencyObject obj, bool value) => obj.SetValue(ClearTextButtonProperty, value);
    }

    public static class ControlsHelper
    {
        public static readonly DependencyProperty ContentCharacterCasingProperty =
            DependencyProperty.RegisterAttached("ContentCharacterCasing", typeof(string), typeof(ControlsHelper));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(ControlsHelper));

        public static string GetContentCharacterCasing(DependencyObject obj) => (string)obj.GetValue(ContentCharacterCasingProperty);
        public static void SetContentCharacterCasing(DependencyObject obj, string value) => obj.SetValue(ContentCharacterCasingProperty, value);
        public static CornerRadius GetCornerRadius(DependencyObject obj) => (CornerRadius)obj.GetValue(CornerRadiusProperty);
        public static void SetCornerRadius(DependencyObject obj, CornerRadius value) => obj.SetValue(CornerRadiusProperty, value);
    }

    public static class TabControlHelper
    {
        public static readonly DependencyProperty UnderlineBrushProperty =
            DependencyProperty.RegisterAttached("UnderlineBrush", typeof(Brush), typeof(TabControlHelper));

        public static readonly DependencyProperty UnderlinedProperty =
            DependencyProperty.RegisterAttached("Underlined", typeof(string), typeof(TabControlHelper));

        public static Brush GetUnderlineBrush(DependencyObject obj) => (Brush)obj.GetValue(UnderlineBrushProperty);
        public static void SetUnderlineBrush(DependencyObject obj, Brush value) => obj.SetValue(UnderlineBrushProperty, value);
        public static string GetUnderlined(DependencyObject obj) => (string)obj.GetValue(UnderlinedProperty);
        public static void SetUnderlined(DependencyObject obj, string value) => obj.SetValue(UnderlinedProperty, value);
    }

    public static class HeaderedControlHelper
    {
        public static readonly DependencyProperty HeaderFontSizeProperty =
            DependencyProperty.RegisterAttached("HeaderFontSize", typeof(double), typeof(HeaderedControlHelper));

        public static readonly DependencyProperty HeaderBackgroundProperty =
            DependencyProperty.RegisterAttached("HeaderBackground", typeof(Brush), typeof(HeaderedControlHelper));

        public static double GetHeaderFontSize(DependencyObject obj) => (double)obj.GetValue(HeaderFontSizeProperty);
        public static void SetHeaderFontSize(DependencyObject obj, double value) => obj.SetValue(HeaderFontSizeProperty, value);
        public static Brush GetHeaderBackground(DependencyObject obj) => (Brush)obj.GetValue(HeaderBackgroundProperty);
        public static void SetHeaderBackground(DependencyObject obj, Brush value) => obj.SetValue(HeaderBackgroundProperty, value);
    }

    public static class ScrollViewerHelper
    {
        public static readonly DependencyProperty IsHorizontalScrollWheelEnabledProperty =
            DependencyProperty.RegisterAttached("IsHorizontalScrollWheelEnabled", typeof(bool), typeof(ScrollViewerHelper));

        public static bool GetIsHorizontalScrollWheelEnabled(DependencyObject obj) => (bool)obj.GetValue(IsHorizontalScrollWheelEnabledProperty);
        public static void SetIsHorizontalScrollWheelEnabled(DependencyObject obj, bool value) => obj.SetValue(IsHorizontalScrollWheelEnabledProperty, value);
    }
}
