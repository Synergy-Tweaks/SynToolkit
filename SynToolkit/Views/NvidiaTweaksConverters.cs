#nullable enable

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services.GpuDrivers;
using Windows.UI;

namespace SynToolkit.Views;

public sealed class NvidiaStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string text)
        {
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        return text switch
        {
            "Applied" => ResolveBrush("SystemFillColorSuccessBackgroundBrush", Color.FromArgb(255, 21, 95, 61)),
            "Restart pending" => ResolveBrush("SystemFillColorAttentionBackgroundBrush", Color.FromArgb(255, 109, 65, 0)),
            "Partial" => ResolveBrush("SystemFillColorCautionBackgroundBrush", Color.FromArgb(255, 109, 65, 0)),
            "Different" => ResolveBrush("SystemFillColorCautionBackgroundBrush", Color.FromArgb(255, 109, 65, 0)),
            _ => ResolveBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(255, 44, 44, 44))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    private static SolidColorBrush ResolveBrush(string key, Color fallback)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out object value) &&
            value is SolidColorBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}

public sealed class NvidiaRiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is NvidiaTweakRiskLevel risk
            ? risk switch
            {
                NvidiaTweakRiskLevel.Advanced => ResolveBrush("SystemFillColorCriticalBackgroundBrush", Color.FromArgb(255, 114, 47, 55)),
                NvidiaTweakRiskLevel.Caution => ResolveBrush("SystemFillColorAttentionBackgroundBrush", Color.FromArgb(255, 109, 65, 0)),
                _ => ResolveBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(255, 44, 44, 44))
            }
            : ResolveBrush("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(255, 44, 44, 44));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    private static SolidColorBrush ResolveBrush(string key, Color fallback)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out object value) &&
            value is SolidColorBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallback);
    }
}
