#nullable enable

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;

namespace SynToolkit.Services.Uninstaller
{
    /// <summary>
    /// Extracts per-app icons for the Uninstaller tab from the registry DisplayIcon
    /// value, falling back to InstallLocation when it points at an executable.
    /// Runs on a background thread; the caller converts the PNG bytes to a
    /// BitmapImage on the UI thread. Failures return null (row keeps its default icon).
    /// </summary>
    internal static class InstalledAppIconHelper
    {
        private static readonly ConcurrentDictionary<string, byte[]?> PngCache = new(StringComparer.OrdinalIgnoreCase);

        public static byte[]? TryExtractIconPng(string? displayIconRaw, string? installLocation)
        {
            string? path = ResolveIconPath(displayIconRaw, installLocation);
            if (path is null)
            {
                return null;
            }

            return PngCache.GetOrAdd(path, ExtractPng);
        }

        internal static string? ResolveIconPath(string? displayIconRaw, string? installLocation)
        {
            string? fromDisplayIcon = CleanPath(StripIconIndex(displayIconRaw));
            if (File.Exists(fromDisplayIcon))
            {
                return Path.GetFullPath(fromDisplayIcon);
            }

            // shortcut: non-exe DisplayIcon targets (dll/ico with index, folders) are skipped;
            // ExtractAssociatedIcon only handles the default icon of a real file.
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
                cleaned.EndsWith('"') == false &&
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

        private static byte[]? ExtractPng(string path)
        {
            try
            {
                using Icon? icon = Icon.ExtractAssociatedIcon(path);
                if (icon is null)
                {
                    return null;
                }

                using Bitmap bitmap = icon.ToBitmap();
                using MemoryStream stream = new();
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
