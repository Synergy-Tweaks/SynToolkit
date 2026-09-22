using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SynToolkit.Views
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool boolValue && boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    public class BoolToVisibilityInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool boolValue && boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility visibility && visibility == Visibility.Collapsed;
        }
    }

    public class BoolToSeverityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool isWarning && isWarning ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    internal class FontIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string iconValue = value as string;
            if (string.IsNullOrEmpty(iconValue))
            {
                return new FontIcon { Glyph = "\uE897" };
            }

            // If it's an image path, create an ImageIcon
            if (iconValue.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                iconValue.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
            {
                return new ImageIcon { Source = ImageSourceCache.Get(iconValue) };
            }

            // Otherwise, treat it as a FontIcon glyph
            return new FontIcon { Glyph = iconValue };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    internal class ImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path && !string.IsNullOrWhiteSpace(path))
            {
                return ImageSourceCache.Get(NormalizePath(path));
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }

        internal static string NormalizePath(string path)
        {
            if (path.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("://", StringComparison.Ordinal))
            {
                return path;
            }

            try
            {
                return new Uri(path).AbsoluteUri;
            }
            catch
            {
                return path;
            }
        }
    }

    /// <summary>
    /// Like <see cref="ImageSourceConverter"/> but returns null for missing/unreadable files
    /// so the UI can fall back to a generic glyph instead of a broken image.
    /// </summary>
    internal class NullableStringToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            try
            {
                return ImageSourceCache.Get(ImageSourceConverter.NormalizePath(path));
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Builds a SettingsCard HeaderIcon from a local image path, falling back to the Games glyph.
    /// </summary>
    internal class GameHeaderIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string path &&
                !string.IsNullOrWhiteSpace(path) &&
                System.IO.File.Exists(path))
            {
                try
                {
                    ImageSource source = ImageSourceCache.Get(ImageSourceConverter.NormalizePath(path));
                    if (source is not null)
                    {
                        return new ImageIcon
                        {
                            Source = source,
                            Width = 36,
                            Height = 36
                        };
                    }
                }
                catch
                {
                    // Fall through to glyph.
                }
            }

            return new FontIcon { Glyph = "\uE7FC", FontSize = 18 };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    internal static class ImageSourceCache
    {
        private const int MaximumBundledSources = 32;
        private const int MemoryPressureSourceLimit = 8;
        private static readonly Dictionary<string, LinkedListNode<CacheEntry>> BundledSources = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<CacheEntry> SourceUsage = new();
        private static readonly object CacheLock = new();

        private sealed record CacheEntry(string Path, ImageSource Source);

        public static ImageSource Get(string path)
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out Uri uri))
            {
                return null;
            }

            if (!path.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
            {
                return Create(path, uri);
            }

            lock (CacheLock)
            {
                if (BundledSources.TryGetValue(path, out LinkedListNode<CacheEntry> cachedNode))
                {
                    SourceUsage.Remove(cachedNode);
                    SourceUsage.AddFirst(cachedNode);
                    return cachedNode.Value.Source;
                }

                ImageSource source = Create(path, uri);
                LinkedListNode<CacheEntry> node = SourceUsage.AddFirst(new CacheEntry(path, source));
                BundledSources.Add(path, node);
                if (BundledSources.Count > MaximumBundledSources && SourceUsage.Last is LinkedListNode<CacheEntry> oldestNode)
                {
                    SourceUsage.RemoveLast();
                    BundledSources.Remove(oldestNode.Value.Path);
                }

                return source;
            }
        }

        internal static void TrimForMemoryPressure()
        {
            lock (CacheLock)
            {
                while (BundledSources.Count > MemoryPressureSourceLimit &&
                    SourceUsage.Last is LinkedListNode<CacheEntry> oldestNode)
                {
                    SourceUsage.RemoveLast();
                    BundledSources.Remove(oldestNode.Value.Path);
                }
            }
        }

        private static ImageSource Create(string path, Uri uri)
        {
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource(uri)
                : new BitmapImage(uri);
        }
    }

    public class ConfigItemDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ConfigurationItem { get; set; }
        public DataTemplate MultiOptionConfigurationItem { get; set; }
        public DataTemplate ConfigurationSubMenu { get; set; }
        public DataTemplate ConfigurationButton { get; set; }
        public DataTemplate ConfiguartionLink { get; set; }
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is ConfigurationItemViewModel)
            {
                return ConfigurationItem;
            }
            if (item is MultiOptionConfigurationItemViewModel)
            {
                return MultiOptionConfigurationItem;
            }
            if (item is ConfigurationSubMenuViewModel)
            {
                return ConfigurationSubMenu;
            }
            if (item is LinksViewModel)
            {
                return ConfiguartionLink;
            }
            if (item is ConfigurationButtonViewModel)
            {
                return ConfigurationButton;
            }

            return base.SelectTemplateCore(item, container);
        }
    }

    public class FavoriteItemDataTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ConfigurationItem { get; set; }
        public DataTemplate MultiOptionConfigurationItem { get; set; }
        public DataTemplate ConfigurationButton { get; set; }
        public DataTemplate ConfiguartionLink { get; set; }
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is ConfigurationItemViewModel)
            {
                return ConfigurationItem;
            }
            if (item is MultiOptionConfigurationItemViewModel)
            {
                return MultiOptionConfigurationItem;
            }
            if (item is LinksViewModel)
            {
                return ConfiguartionLink;
            }
            if (item is ConfigurationButtonViewModel)
            {
                return ConfigurationButton;
            }

            return base.SelectTemplateCore(item, container);
        }
    }
}
