#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SynToolkit.Services.Uninstaller
{
    /// <summary>
    /// Resolves per-app icons for the Uninstaller tab from the registry DisplayIcon
    /// value, falling back to InstallLocation when it points at an executable.
    /// Uses the WinRT thumbnail pipeline (same as CleanerPage drives) so no GDI
    /// dependency is needed. Failures return null (row keeps its default icon).
    /// Must be called on the UI thread: BitmapImage creation is not free-threaded.
    /// </summary>
    internal static class InstalledAppIconHelper
    {
        public static async Task<ImageSource?> GetIconAsync(
            string? displayIconRaw,
            string? installLocation,
            CancellationToken cancellationToken = default)
        {
            string? path = ResolveIconPath(displayIconRaw, installLocation);
            if (path is null || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
                using StorageItemThumbnail? thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    64,
                    ThumbnailOptions.UseCurrentScale).AsTask(cancellationToken);
                if (thumbnail is null || thumbnail.Size == 0 || cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                BitmapImage bitmap = new();
                await bitmap.SetSourceAsync(thumbnail).AsTask(cancellationToken);
                return bitmap;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Debug(exception, "[Uninstaller] Unable to load icon for {IconPath}.", path);
                return null;
            }
        }

        internal static string? ResolveIconPath(string? displayIconRaw, string? installLocation)
        {
            string? fromDisplayIcon = CleanPath(StripIconIndex(displayIconRaw));
            if (File.Exists(fromDisplayIcon))
            {
                return Path.GetFullPath(fromDisplayIcon);
            }

            // shortcut: non-exe DisplayIcon targets (dll/ico with index, folders) are skipped;
            // the thumbnail pipeline only handles real files with registered icons.
            string? location = CleanPath(installLocation);
            if (!string.IsNullOrWhiteSpace(location) &&
                File.Exists(location) &&
                location.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(location);
            }

            return null;
        }

        private static string? StripIconIndex(string? raw)
        {
            string? cleaned = CleanPath(raw);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            // DisplayIcon is commonly "C:\path\app.exe,0".
            int comma = cleaned.LastIndexOf(',');
            if (comma > 0 &&
                !cleaned.EndsWith('"') &&
                int.TryParse(cleaned.AsSpan(comma + 1).Trim(), out _))
            {
                cleaned = cleaned.Substring(0, comma).Trim().Trim('"');
            }

            return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
        }

        private static string? CleanPath(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string expanded = Environment.ExpandEnvironmentVariables(raw.Trim()).Trim('"').Trim();
            return string.IsNullOrWhiteSpace(expanded) ? null : expanded;
        }
    }
}
