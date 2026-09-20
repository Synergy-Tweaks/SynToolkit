#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games.Providers
{
    /// <summary>
    /// Port of PlayniteExtensions EpicLibrary EpicLauncher / EpicLibrary installed-game detection.
    /// Source: https://github.com/JosefNemec/PlayniteExtensions (MIT)
    /// </summary>
    public sealed class EpicGameLibraryProvider : IGameLibraryProvider
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public GameSource Source => GameSource.Epic;
        public string DisplayName => "Epic";

        public Task<IReadOnlyList<DetectedGame>> ScanAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ScanInstalled(cancellationToken), cancellationToken);
        }

        private static IReadOnlyList<DetectedGame> ScanInstalled(CancellationToken cancellationToken)
        {
            var games = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
            List<InstalledApp> appList = GetInstalledAppList();
            List<InstalledManifest> manifests = GetInstalledManifests();

            foreach (InstalledManifest manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (manifest.AppCategories?.Contains("addons") == true &&
                    manifest.AppCategories?.Any(a => a == "addons/launchable") == false)
                {
                    continue;
                }

                if (manifest.AppCategories?.Any(a => a is "plugins" or "plugins/engine") == true ||
                    manifest.CompatibleApps?.Any(a => a.StartsWith("UE_", StringComparison.Ordinal)) == true ||
                    manifest.TechnicalType?.Contains("plugins/engine", StringComparison.OrdinalIgnoreCase) == true)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(manifest.AppName))
                {
                    continue;
                }

                string gameName = !string.IsNullOrWhiteSpace(manifest.DisplayName)
                    ? manifest.DisplayName!
                    : Path.GetFileName(manifest.InstallLocation ?? string.Empty);

                gameName = NormalizeName(gameName);
                InstalledApp? app = appList.FirstOrDefault(a =>
                    string.Equals(a.AppName, manifest.AppName, StringComparison.OrdinalIgnoreCase));

                string? installLocation = manifest.InstallLocation;
                if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
                {
                    if (!string.IsNullOrWhiteSpace(app?.InstallLocation))
                    {
                        installLocation = app.InstallLocation;
                    }
                }

                if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
                {
                    continue;
                }

                installLocation = FixSeparators(installLocation);
                string? executablePath = null;
                if (!string.IsNullOrWhiteSpace(manifest.LaunchExecutable))
                {
                    executablePath = Path.Combine(installLocation, FixSeparators(manifest.LaunchExecutable));
                    if (!File.Exists(executablePath))
                    {
                        executablePath = null;
                    }
                }

                string? launchUri = null;
                if (!string.IsNullOrWhiteSpace(manifest.CatalogNamespace) &&
                    !string.IsNullOrWhiteSpace(manifest.CatalogItemId) &&
                    !string.IsNullOrWhiteSpace(manifest.AppName))
                {
                    launchUri =
                        $"com.epicgames.launcher://apps/{manifest.CatalogNamespace}%3A{manifest.CatalogItemId}%3A{manifest.AppName}?action=launch&silent=true";
                }

                var detected = new DetectedGame
                {
                    ExternalId = manifest.AppName!,
                    Name = gameName,
                    Source = GameSource.Epic,
                    InstallPath = installLocation,
                    ExecutablePath = executablePath,
                    LaunchUri = launchUri,
                    LaunchArgs = string.IsNullOrWhiteSpace(manifest.LaunchCommand) ? null : manifest.LaunchCommand
                };

                if (games.ContainsKey(detected.ExternalId))
                {
                    if (app?.InstallLocation is not null &&
                        string.Equals(app.InstallLocation, detected.InstallPath, StringComparison.OrdinalIgnoreCase))
                    {
                        games[detected.ExternalId] = detected;
                    }
                }
                else
                {
                    games.Add(detected.ExternalId, detected);
                }
            }

            return games.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<InstalledApp> GetInstalledAppList()
        {
            string installListPath = Path.Combine(
                Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"),
                "Epic",
                "UnrealEngineLauncher",
                "LauncherInstalled.dat");

            if (!File.Exists(installListPath))
            {
                return new List<InstalledApp>();
            }

            try
            {
                string json = File.ReadAllText(installListPath);
                LauncherInstalled? list = JsonSerializer.Deserialize<LauncherInstalled>(json, JsonOptions);
                return list?.InstallationList ?? new List<InstalledApp>();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Failed to parse Epic LauncherInstalled.dat.");
                return new List<InstalledApp>();
            }
        }

        private static List<InstalledManifest> GetInstalledManifests()
        {
            var manifests = new List<InstalledManifest>();
            string installListPath = Path.Combine(
                Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"),
                "Epic",
                "EpicGamesLauncher",
                "Data",
                "Manifests");

            if (!Directory.Exists(installListPath))
            {
                return manifests;
            }

            foreach (string manFile in Directory.GetFiles(installListPath, "*.item"))
            {
                try
                {
                    string json = File.ReadAllText(manFile);
                    InstalledManifest? manifest = JsonSerializer.Deserialize<InstalledManifest>(json, JsonOptions);
                    if (manifest is not null)
                    {
                        manifests.Add(manifest);
                    }
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, $"Failed to parse Epic installed game manifest: {manFile}");
                }
            }

            return manifests;
        }

        private static string FixSeparators(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        private static string NormalizeName(string name) =>
            name.Replace("™", string.Empty, StringComparison.Ordinal)
                .Replace("®", string.Empty, StringComparison.Ordinal)
                .Replace("©", string.Empty, StringComparison.Ordinal)
                .Trim();

        private sealed class LauncherInstalled
        {
            public List<InstalledApp>? InstallationList { get; set; }
        }

        private sealed class InstalledApp
        {
            public string? InstallLocation { get; set; }
            public string? AppName { get; set; }
        }

        private sealed class InstalledManifest
        {
            public string? LaunchCommand { get; set; }
            public string? LaunchExecutable { get; set; }
            public string? AppName { get; set; }
            public string? CatalogNamespace { get; set; }
            public string? CatalogItemId { get; set; }
            public List<string>? AppCategories { get; set; }
            public List<string>? CompatibleApps { get; set; }
            public string? DisplayName { get; set; }
            public string? InstallLocation { get; set; }
            public string? TechnicalType { get; set; }
        }
    }
}
