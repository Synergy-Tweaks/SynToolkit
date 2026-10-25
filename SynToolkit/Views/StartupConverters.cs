#nullable enable

using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Models.Startup;
using Windows.UI;

namespace SynToolkit.Views
{
    public sealed class RiskToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is RiskLevel risk)
            {
                return new SolidColorBrush(risk switch
                {
                    RiskLevel.Safe => Color.FromArgb(255, 34, 197, 94),
                    RiskLevel.Low => Color.FromArgb(255, 99, 210, 155),
                    RiskLevel.Medium => Color.FromArgb(255, 251, 191, 36),
                    RiskLevel.High => Color.FromArgb(255, 239, 68, 68),
                    _ => Color.FromArgb(255, 148, 163, 184)
                });
            }

            return new SolidColorBrush(Color.FromArgb(255, 148, 163, 184));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }

    public sealed class RiskToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is RiskLevel risk)
            {
                return new SolidColorBrush(risk switch
                {
                    RiskLevel.Safe => Color.FromArgb(25, 34, 197, 94),
                    RiskLevel.Low => Color.FromArgb(25, 99, 210, 155),
                    RiskLevel.Medium => Color.FromArgb(25, 251, 191, 36),
                    RiskLevel.High => Color.FromArgb(25, 239, 68, 68),
                    _ => Color.FromArgb(20, 148, 163, 184)
                });
            }

            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }

    public sealed class CategoryToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is StartupCategory category)
            {
                return category switch
                {
                    StartupCategory.Registry => "\uE713",
                    StartupCategory.StartupFolder => "\uE8B7",
                    StartupCategory.ScheduledTask => "\uE823",
                    StartupCategory.Service => "\uE945",
                    _ => "\uE897"
                };
            }

            return "\uE897";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }

    public sealed class BoolToEnabledTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool enabled && enabled
                ? App.GetValueFromItemList("Startup_Enabled")
                : App.GetValueFromItemList("Startup_Disabled");

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }

    public sealed class BoolToEnabledColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool enabled && enabled
                ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
                : new SolidColorBrush(Color.FromArgb(255, 100, 116, 139));

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
