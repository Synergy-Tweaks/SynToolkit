#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games.Providers
{
    /// <summary>
    /// Port of PlayniteExtensions SteamLibrary SteamLocalService / Steam.cs installed-game detection.
    /// Source: https://github.com/JosefNemec/PlayniteExtensions (MIT)
    /// </summary>
    public sealed class SteamGameLibraryProvider : IGameLibraryProvider
    {
        private const string RedistributableAppId = "228980";

        [Flags]
        private enum AppStateFlags
        {
            FullyInstalled = 4
        }

        public GameSource Source => GameSource.Steam;
        public string DisplayName => "Steam";

        public Task<IReadOnlyList<DetectedGame>> ScanAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ScanInstalled(cancellationToken), cancellationToken);
        }

        private static IReadOnlyList<DetectedGame> ScanInstalled(CancellationToken cancellationToken)
        {
            var games = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
            string? steamPath = GetSteamInstallationPath();
            if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
            {
                return Array.Empty<DetectedGame>();
            }

            foreach (string libraryRoot in GetLibraryFolders(steamPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string steamApps = Path.Combine(libraryRoot, "steamapps");
                if (!Directory.Exists(steamApps))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(steamApps, "appmanifest*"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (file.EndsWith("tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        DetectedGame? game = ReadAppManifest(file);
                        if (game is null ||
                            string.IsNullOrWhiteSpace(game.InstallPath) ||
                            game.ExternalId == RedistributableAppId ||
                            game.InstallPath.Contains(@"steamapps\music", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        games.TryAdd(game.ExternalId, game);
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, $"Failed to parse Steam appmanifest: {file}");
                    }
                }
            }

            return games.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static DetectedGame? ReadAppManifest(string path)
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            VdfKeyValue kv = VdfKeyValue.ReadAsText(fs);

            string? stateFlagsValue = kv["StateFlags"].Value;
            if (string.IsNullOrEmpty(stateFlagsValue) ||
                !Enum.TryParse(stateFlagsValue, out AppStateFlags appState) ||
                !appState.HasFlag(AppStateFlags.FullyInstalled))
            {
                return null;
            }

            string name = kv["name"].Value ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                name = kv["UserConfig"]["name"].Value ?? string.Empty;
            }

            name = NormalizeName(name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string appId = kv["appID"].AsUnsignedInteger().ToString();
            if (string.IsNullOrEmpty(appId) || appId == "0")
            {
                return null;
            }

            string installDirName = kv["installDir"].Value ?? string.Empty;
            string steamAppsDir = new FileInfo(path).Directory!.FullName;
            string installDir = Path.Combine(steamAppsDir, "common", installDirName);
            if (!Directory.Exists(installDir))
            {
                installDir = Path.Combine(steamAppsDir, "music", installDirName);
                if (!Directory.Exists(installDir))
                {
                    installDir = string.Empty;
                }
            }

            return new DetectedGame
            {
                ExternalId = appId,
                Name = name,
                Source = GameSource.Steam,
                InstallPath = string.IsNullOrEmpty(installDir) ? null : installDir,
                LaunchUri = $"steam://rungameid/{appId}",
                IconPath = TryFindSteamIcon(steamAppsDir, appId)
            };
        }

        private static string? TryFindSteamIcon(string steamAppsDir, string appId)
        {
            try
            {
                string? libraryRoot = Directory.GetParent(steamAppsDir)?.FullName;
                if (libraryRoot is null)
                {
                    return null;
                }

                string icon = Path.Combine(libraryRoot, "steam", "games", appId, "icon.ico");
                return File.Exists(icon) ? icon : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetSteamInstallationPath()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                string? path = key?.GetValue("SteamPath")?.ToString()?.Replace('/', '\\');
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch
            {
                return null;
            }
        }

        private static HashSet<string> GetLibraryFolders(string steamPath)
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
            string configPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(configPath))
            {
                return folders;
            }

            try
            {
                using FileStream fs = new(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                VdfKeyValue kv = VdfKeyValue.ReadAsText(fs);
                foreach (VdfKeyValue child in kv.Children)
                {
                    if (!int.TryParse(child.Name, out _))
                    {
                        continue;
                    }

                    string? path = child.Value;
                    if (string.IsNullOrWhiteSpace(path) && child.Children.Count > 0)
                    {
                        path = child.Children
                            .FirstOrDefault(c => string.Equals(c.Name, "path", StringComparison.OrdinalIgnoreCase))
                            ?.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        path = path.Replace('/', '\\');
                        if (Directory.Exists(path))
                        {
                            folders.Add(path);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Failed to read Steam libraryfolders.vdf.");
            }

            return folders;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return name
                .Replace("™", string.Empty, StringComparison.Ordinal)
                .Replace("®", string.Empty, StringComparison.Ordinal)
                .Replace("©", string.Empty, StringComparison.Ordinal)
                .Trim();
        }
    }
}
