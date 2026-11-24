using System.Windows;

namespace Roomy.Controls;

/// <summary>
/// Opens windows at their designed size, but never taller or wider than the usable screen area,
/// so nothing ends up under the taskbar on small or high-scaling screens.
/// </summary>
public static class FitToScreen
{
    const double Margin = 24;

    public static void Apply(Window w)
    {
        var area = SystemParameters.WorkArea; // device-independent pixels, taskbar excluded
        double maxH = Math.Max(300, area.Height - Margin), maxW = Math.Max(400, area.Width - Margin);
        if (w.MinHeight > maxH) w.MinHeight = maxH;
        if (w.MinWidth > maxW) w.MinWidth = maxW;
        if (w.Height > maxH) w.Height = maxH;
        if (w.Width > maxW) w.Width = maxW;
    }
}
