#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace SynToolkit.Services.Uninstaller
{
    /// <summary>
    /// Resolves per-app icons for the Uninstaller tab from the registry DisplayIcon
    /// value, falling back to InstallLocation / Start Menu shortcuts.
    /// Icons are decoded into PNG-backed BitmapImages with DecodePixel* set for the
    /// display slot. Failures return null so the row can show a neutral glyph.
    /// BitmapImage creation must run on the UI thread.
    /// </summary>
    internal sealed record ShortcutIconEntry(string ShortcutName, string? IconFile, string? TargetFile);

    internal static class InstalledAppIconHelper
    {
        private const uint RequestedThumbnailPx = 256;
        private const int DisplaySlotPx = 36;

        private static readonly ConcurrentDictionary<string, byte[]?> EncodedIconCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static async Task<ImageSource?> GetIconAsync(
            string? displayName,
            string? displayIconRaw,
            string? installLocation,
            IReadOnlyDictionary<string, List<ShortcutIconEntry>> shortcutMap,
            CancellationToken cancellationToken = default)
        {
            string? path = ResolveIconPath(displayIconRaw, installLocation)
                ?? ResolveShortcutIconPath(displayName, shortcutMap);
            if (path is null || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            try
            {
                byte[]? pngBytes = await GetOrLoadEncodedIconAsync(path, cancellationToken);
                if (pngBytes is null || pngBytes.Length == 0 || cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                return await CreateBitmapImageAsync(pngBytes, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Debug(exception, "[Uninstaller] Unable to load icon for {IconPath}.", path);
                return null;
            }
        }

        private static async Task<byte[]?> GetOrLoadEncodedIconAsync(
            string path,
            CancellationToken cancellationToken)
        {
            if (EncodedIconCache.TryGetValue(path, out byte[]? cached))
            {
                return cached;
            }

            byte[]? encoded = await Task.Run(
                async () => await LoadEncodedIconBytesAsync(path, cancellationToken),
                cancellationToken);

            EncodedIconCache[path] = encoded;
            return encoded;
        }

        private static async Task<byte[]?> LoadEncodedIconBytesAsync(
            string path,
            CancellationToken cancellationToken)
        {
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
                using StorageItemThumbnail? thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    RequestedThumbnailPx,
                    ThumbnailOptions.ResizeThumbnail | ThumbnailOptions.UseCurrentScale)
                    .AsTask(cancellationToken);

                if (thumbnail is null || thumbnail.Size == 0)
                {
                    return null;
                }

                // Copy the thumbnail into a seekable stream, then re-encode as PNG so
                // BitmapImage never holds a disposed StorageItemThumbnail handle.
                using InMemoryRandomAccessStream sourceStream = new();
                await RandomAccessStream.CopyAsync(thumbnail, sourceStream).AsTask(cancellationToken);
                sourceStream.Seek(0);

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream)
                    .AsTask(cancellationToken);
                using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied).AsTask(cancellationToken);

                using InMemoryRandomAccessStream pngStream = new();
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.PngEncoderId,
                    pngStream).AsTask(cancellationToken);
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync().AsTask(cancellationToken);

                pngStream.Seek(0);
                byte[] buffer = new byte[pngStream.Size];
                await pngStream.ReadAsync(
                    buffer.AsBuffer(),
                    (uint)buffer.Length,
                    InputStreamOptions.None).AsTask(cancellationToken);
                return buffer;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Debug(exception, "[Uninstaller] Icon extract failed for {IconPath}.", path);
                return null;
            }
        }

        private static async Task<BitmapImage> CreateBitmapImageAsync(
            byte[] pngBytes,
            CancellationToken cancellationToken)
        {
            using InMemoryRandomAccessStream stream = new();
            await stream.WriteAsync(pngBytes.AsBuffer()).AsTask(cancellationToken);
            stream.Seek(0);

            int decodePx = GetDecodePixelSize();
            BitmapImage bitmap = new()
            {
                DecodePixelWidth = decodePx,
                DecodePixelHeight = decodePx,
                DecodePixelType = DecodePixelType.Logical
            };
            await bitmap.SetSourceAsync(stream).AsTask(cancellationToken);
            return bitmap;
        }

        private static int GetDecodePixelSize()
        {
            try
            {
                // Logical slot is 36px; decode a bit larger so 125%/150% DPI stays sharp.
                double scale = Windows.Graphics.Display.DisplayInformation
                    .GetForCurrentView()
                    .RawPixelsPerViewPixel;
                int px = (int)Math.Ceiling(DisplaySlotPx * Math.Max(1.0, scale));
                return Math.Clamp(px, DisplaySlotPx, 128);
            }
            catch
            {
                return DisplaySlotPx * 2;
            }
        }

        internal static string? ResolveIconPath(string? displayIconRaw, string? installLocation)
        {
            string? fromDisplayIcon = CleanPath(StripIconIndex(displayIconRaw));
            if (File.Exists(fromDisplayIcon) && !LooksLikeSharedBrowserHost(fromDisplayIcon))
            {
                return Path.GetFullPath(fromDisplayIcon);
            }

            // non-exe DisplayIcon targets (dll/ico with index, folders) are skipped;
            // the thumbnail pipeline only handles real files with registered icons.
            string? location = CleanPath(installLocation);
            if (!string.IsNullOrWhiteSpace(location) &&
                File.Exists(location) &&
                location.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !LooksLikeSharedBrowserHost(location))
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

        /// <summary>
        /// Enumerates per-user and all-users Start Menu shortcuts once per icon pass.
        /// Key is the normalized shortcut name; values keep the shortcut's own icon
        /// file (preferred) and target executable. Runs on a background thread.
        /// </summary>
        internal static Dictionary<string, List<ShortcutIconEntry>> BuildShortcutMap()
        {
            Dictionary<string, List<ShortcutIconEntry>> map = new(StringComparer.OrdinalIgnoreCase);
            Type? shellType;
            try
            {
                shellType = Type.GetTypeFromProgID("WScript.Shell");
            }
            catch
            {
                return map;
            }

            if (shellType is null)
            {
                return map;
            }

            string[] roots =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            ];

            object? shell;
            try
            {
                shell = Activator.CreateInstance(shellType);
            }
            catch
            {
                return map;
            }

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                string[] links;
                try
                {
                    links = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
                }
                catch
                {
                    continue;
                }

                foreach (string link in links)
                {
                    try
                    {
                        object? shortcut = shellType.InvokeMember(
                            "CreateShortcut", BindingFlags.InvokeMethod, null, shell, [link]);
                        string? target = shortcut?.GetType().InvokeMember(
                            "TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                        string? iconLocation = shortcut?.GetType().InvokeMember(
                            "IconLocation", BindingFlags.GetProperty, null, shortcut, null) as string;

                        string? iconFile = CleanPath(StripIconIndex(iconLocation));
                        if (!File.Exists(iconFile))
                        {
                            iconFile = null;
                        }

                        string? targetFile = CleanPath(target);
                        if (!File.Exists(targetFile))
                        {
                            targetFile = null;
                        }

                        if (iconFile is null && targetFile is null)
                        {
                            continue;
                        }

                        string key = NormalizeName(Path.GetFileNameWithoutExtension(link));
                        if (string.IsNullOrWhiteSpace(key))
                        {
                            continue;
                        }

                        if (!map.TryGetValue(key, out List<ShortcutIconEntry>? entries))
                        {
                            entries = new List<ShortcutIconEntry>();
                            map[key] = entries;
                        }

                        entries.Add(new ShortcutIconEntry(key, iconFile, targetFile));
                    }
                    catch
                    {
                        // skip unreadable shortcuts
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// Matches an installed app to at most one shortcut icon file. Accepts exact
        /// and affix matches ("Mozilla Firefox (x64)" -> "Firefox", "Git" -> "Git Bash"
        /// only when unambiguous). Returns null on ambiguity rather than a wrong icon.
        /// </summary>
        internal static string? ResolveShortcutIconPath(
            string? displayName,
            IReadOnlyDictionary<string, List<ShortcutIconEntry>> shortcutMap)
        {
            string app = NormalizeName(displayName);
            if (string.IsNullOrWhiteSpace(app))
            {
                return null;
            }

            HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<ShortcutIconEntry>> pair in shortcutMap)
            {
                string shortcut = pair.Key;
                if (shortcut.Equals(app, StringComparison.OrdinalIgnoreCase) ||
                    shortcut.StartsWith(app + " ", StringComparison.OrdinalIgnoreCase) ||
                    app.EndsWith(" " + shortcut, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (ShortcutIconEntry entry in pair.Value)
                    {
                        string? file = entry.IconFile;
                        if (string.IsNullOrWhiteSpace(file) && !LooksLikeSharedBrowserHost(entry.TargetFile))
                        {
                            file = entry.TargetFile;
                        }

                        if (!string.IsNullOrWhiteSpace(file))
                        {
                            candidates.Add(file);
                        }
                    }
                }
            }

            // shortcut: multiple distinct icons means ambiguity (e.g. "Git" matching
            // Git Bash / Git CMD / Git GUI) - a generic icon beats a wrong one.
            return candidates.Count == 1 ? candidates.First() : null;
        }

        internal static string NormalizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            // Strip trailing parenthetical qualifiers: "Firefox (x64 en-US)" -> "Firefox".
            string normalized = name.Trim().ToLowerInvariant();
            string previous;
            do
            {
                previous = normalized;
                normalized = Regex.Replace(normalized, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
            }
            while (!normalized.Equals(previous, StringComparison.Ordinal));

            return Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        private static bool LooksLikeSharedBrowserHost(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fileName = Path.GetFileName(path);
            return fileName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("msedge_proxy.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("chrome_proxy.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
