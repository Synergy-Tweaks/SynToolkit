using System.Windows.Controls;
using System.Windows.Media;

namespace Roomy.Views;

public sealed record ShareData(
    string FreedText, string Subtitle, double Before, double After,
    string BeforeText, string AfterText, Brush BeforeBrush);

public partial class ShareCard : UserControl
{
    public const int W = 1200, H = 630;
    public ShareCard() => InitializeComponent();
}
