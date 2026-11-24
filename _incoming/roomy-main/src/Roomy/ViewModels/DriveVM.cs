using System.Windows.Media;
using Roomy.Core;
using Roomy.Localization;

namespace Roomy.ViewModels;

public sealed class DriveVM : ObservableObject
{
    public DriveVM(string root, string label, long total, long free)
    {
        Root = root;
        Label = label;
        Update(total, free);
    }

    public string Root { get; }
    public string Label { get; }
    public string Letter => Root.TrimEnd('\\');
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? S.LocalDisk : Label;
    public string Title => $"{DisplayName} ({Letter})";

    public long Total { get; private set; }
    public long Free { get; private set; }
    public long Used => Math.Max(0, Total - Free);
    public double UsedFraction => Total > 0 ? (double)Used / Total : 0;

    public string Subtitle => Loc.Format("DriveLabel", Format.Bytes(Total));
    public string FreeNumber => Format.Bytes(Free);
    public string FreeRest => Loc.Format("FreeOf", Format.Bytes(Total));
    public string FreeShort => Loc.Format("FreeBytes", Format.Bytes(Free));
    public string UsedText => Loc.Format("UsedAmount", Format.Bytes(Used));
    public string PercentText => Loc.Format("PercentFull", $"{UsedFraction * 100:0}");
    public string StatusText => UsedFraction >= .9 ? S.AlmostFull : UsedFraction >= .75 ? S.GettingFull : S.LookingGood;

    string Tone => UsedFraction >= .9 ? "Danger" : UsedFraction >= .75 ? "Warn" : "Accent";
    public Brush StatusBrush => Res.Brush(Tone + "Brush");
    public Brush StatusBg => Res.Brush(Tone + "SoftBrush");
    public Brush BarBrush => Res.Brush(Tone + "Gradient");

    bool _spaceSelected;
    public bool IsSpaceSelected { get => _spaceSelected; set => Set(ref _spaceSelected, value); }

    public void Update(long total, long free)
    {
        Total = total;
        Free = free;
        RaiseAll();
    }
}
