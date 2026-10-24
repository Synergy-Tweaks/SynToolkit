using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SynToolkit.Controls
{
    public sealed partial class BusyOverlay : UserControl
    {
        public static readonly DependencyProperty ShowWhenProperty = DependencyProperty.Register(
            nameof(ShowWhen),
            typeof(bool),
            typeof(BusyOverlay),
            new PropertyMetadata(false));

        public BusyOverlay()
        {
            InitializeComponent();
        }

        public bool ShowWhen
        {
            get => (bool)GetValue(ShowWhenProperty);
            set => SetValue(ShowWhenProperty, value);
        }
    }
}
