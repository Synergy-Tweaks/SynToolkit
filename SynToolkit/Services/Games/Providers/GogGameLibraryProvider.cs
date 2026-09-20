#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games.Providers
{
    /// <summary>
    /// Port of PlayniteExtensions GogLibrary installed-entry / play-task detection.
    /// Source: https://github.com/JosefNemec/PlayniteExtensions (MIT)
    /// </summary>
    public sealed class GogGameLibraryProvider : IGameLibraryProvider
    {
        private static readonly Regex GogUninstallKeyRegex = new(@"^(\d+)_is1$", RegexOptions.Compiled);

        public GameSource Source => GameSource.Gog;
        public string DisplayName => "GOG";

        public Task<IReadOnlyList<DetectedGame>> ScanAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ScanInstalled(cancellationToken), cancellationToken);
        }

        private static IReadOnlyList<DetectedGame> ScanInstalled(CancellationToken cancellationToken)
        {
            var games = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

            foreach (UninstallProgramsHelper.UninstallProgram program in UninstallProgramsHelper.GetUninstallPrograms())
            {
                cancellationToken.ThrowIfCancellationRequested();

                Match match = GogUninstallKeyRegex.Match(program.RegistryKeyName);
                if (!match.Success ||
                    !string.Equals(program.Publisher, "GOG.com", StringComparison.OrdinalIgnoreCase) ||
                    program.RegistryKeyName.StartsWith("GOGPACK", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(program.InstallLocation) ||
                    !Directory.Exists(program.InstallLocation))
                {
                    continue;
                }

                string gameId = match.Groups[1].Value;
                string installDir = Path.GetFullPath(program.InstallLocation);
                GogGameActionInfo? info = GetGogGameInfo(gameId, installDir);
                PlayTask? primary = info?.PlayTasks?.FirstOrDefault(t => t.IsPrimary);
                if (primary is null)
                {
                    // Empty play task = DLC
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(info?.RootGameId) &&
                    !string.Equals(info.RootGameId, gameId, StringComparison.Ordinal))
                {
                    continue; // DLC
                }

                string? executablePath = null;
                string? launchUri = null;
                string? launchArgs = primary.Arguments;
                string? workingDir = null;

                // Playnite ActionType: FileTask = 0, URLTask = 1
                bool isUrlTask = primary.Type == 1;

                if (!isUrlTask)
                {
                    if (!string.IsNullOrWhiteSpace(primary.Path))
                    {
                        executablePath = Path.IsPathRooted(primary.Path)
                            ? primary.Path
                            : Path.Combine(installDir, FixSeparators(primary.Path!));
                    }

                    if (!string.IsNullOrWhiteSpace(primary.WorkingDir))
                    {
                        workingDir = Path.Combine(installDir, FixSeparators(primary.WorkingDir!));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(primary.Link))
                {
                    launchUri = primary.Link;
                }

                string name = NormalizeName(program.DisplayName ?? info?.Name ?? gameId);
                games[gameId] = new DetectedGame
                {
                    ExternalId = gameId,
                    Name = name,
                    Source = GameSource.Gog,
                    InstallPath = installDir,
                    ExecutablePath = executablePath is not null && File.Exists(executablePath) ? executablePath : null,
                    LaunchUri = launchUri,
                    LaunchArgs = launchArgs,
                    WorkingDirectory = workingDir,
                    IconPath = ResolveIcon(program.DisplayIcon, executablePath)
                };
            }

            return games.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static GogGameActionInfo? GetGogGameInfo(string gameId, string installDir)
        {
            string manifestFile = Path.Combine(installDir, $"goggame-{gameId}.info");
            if (!File.Exists(manifestFile))
            {
                return null;
            }

            try
            {
                string content = File.ReadAllText(manifestFile);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<GogGameActionInfo>(content);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, $"Failed to read GOG game manifest: {manifestFile}");
                return null;
            }
        }

        private static string? ResolveIcon(string? displayIcon, string? executablePath)
        {
            if (!string.IsNullOrWhiteSpace(displayIcon))
            {
                string icon = displayIcon.Split(',')[0].Trim().Trim('"');
                if (File.Exists(icon))
                {
                    return icon;
                }
            }

            return executablePath is not null && File.Exists(executablePath) ? executablePath : null;
        }

        private static string FixSeparators(string path) =>
            path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        private static string NormalizeName(string name) =>
            name.Replace("™", string.Empty, StringComparison.Ordinal)
                .Replace("®", string.Empty, StringComparison.Ordinal)
                .Replace("©", string.Empty, StringComparison.Ordinal)
                .Trim();

        private sealed class GogGameActionInfo
        {
            [JsonProperty("rootGameId")]
            public string? RootGameId { get; set; }

            [JsonProperty("name")]
            public string? Name { get; set; }

            [JsonProperty("playTasks")]
            public List<PlayTask>? PlayTasks { get; set; }
        }

        private sealed class PlayTask
        {
            [JsonProperty("isPrimary")]
            public bool IsPrimary { get; set; }

            [JsonProperty("type")]
            public int Type { get; set; }

            [JsonProperty("path")]
            public string? Path { get; set; }

            [JsonProperty("workingDir")]
            public string? WorkingDir { get; set; }

            [JsonProperty("arguments")]
            public string? Arguments { get; set; }

            [JsonProperty("link")]
            public string? Link { get; set; }
        }
    }
}
