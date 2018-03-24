using System.Windows;
using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for TabHeader.xaml 
    /// </summary>
    public partial class TabHeader : UserControl
    {
        public FontAwesome.WPF.FontAwesomeIcon IconName
        {
            get
            {
                return (FontAwesome.WPF.FontAwesomeIcon)GetValue(IconNameProperty);
            }
            set
            {
                SetValue(IconNameProperty, value);
            }
        }

        public static readonly DependencyProperty IconNameProperty =
            DependencyProperty.Register(
                "IconName",
                typeof(FontAwesome.WPF.FontAwesomeIcon),
                typeof(TabHeader),
                new PropertyMetadata(FontAwesome.WPF.FontAwesomeIcon.Cog)
                );

        public string HeaderText
        {
            get { return (string)GetValue(HeaderTextProperty); }
            set { SetValue(HeaderTextProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IconName. This enables animation,
        // styling, binding, etc...
        public static readonly DependencyProperty HeaderTextProperty =
            DependencyProperty.Register(
                "HeaderText",
                typeof(string),
                typeof(TabHeader),
                new PropertyMetadata("Tab Header")
                );

        public TabHeader()
        {
            InitializeComponent();
            this.DataContext = this;
        }
    }
}