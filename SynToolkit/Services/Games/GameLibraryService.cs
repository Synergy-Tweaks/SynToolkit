#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Models.Games;

namespace SynToolkit.Services.Games
{
    public sealed class GameLibraryStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly object _lock = new();
        private readonly string _libraryPath;
        private GameLibraryDocument _document;

        public GameLibraryStore()
        {
            _libraryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "game-library.json");
            _document = Load();
        }

        public IReadOnlyList<GameEntry> GetGames()
        {
            lock (_lock)
            {
                return _document.Games
                    .Select(Clone)
                    .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public GamesLibraryViewMode GetViewMode()
        {
            lock (_lock)
            {
                return string.Equals(_document.ViewMode, "grid", StringComparison.OrdinalIgnoreCase)
                    ? GamesLibraryViewMode.Grid
                    : GamesLibraryViewMode.List;
            }
        }

        public void SetViewMode(GamesLibraryViewMode mode)
        {
            lock (_lock)
            {
                string serialized = mode == GamesLibraryViewMode.Grid ? "grid" : "list";
                if (string.Equals(_document.ViewMode, serialized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _document.ViewMode = serialized;
                SaveUnlocked();
            }
        }

        public void SaveGames(IEnumerable<GameEntry> games)
        {
            lock (_lock)
            {
                _document.Games = games.Select(Clone).ToList();
                SaveUnlocked();
            }
        }

        private GameLibraryDocument Load()
        {
            try
            {
                if (!File.Exists(_libraryPath))
                {
                    return new GameLibraryDocument();
                }

                GameLibraryDocument? document = JsonSerializer.Deserialize<GameLibraryDocument>(
                    File.ReadAllText(_libraryPath),
                    SerializerOptions);
                return document ?? new GameLibraryDocument();
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to load game library. Using empty library.");
                return new GameLibraryDocument();
            }
        }

        private void SaveUnlocked()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_libraryPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(_document, SerializerOptions);
                File.WriteAllText(_libraryPath, json);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to save game library.");
            }
        }

        private static GameEntry Clone(GameEntry entry) => new()
        {
            Id = entry.Id,
            Name = entry.Name,
            Source = entry.Source,
            ExternalId = entry.ExternalId,
            InstallPath = entry.InstallPath,
            ExecutablePath = entry.ExecutablePath,
            LaunchUri = entry.LaunchUri,
            LaunchArgs = entry.LaunchArgs,
            WorkingDirectory = entry.WorkingDirectory,
            IconPath = entry.IconPath,
            ArtworkPath = entry.ArtworkPath,
            IsCustomArtwork = entry.IsCustomArtwork,
            LastPlayed = entry.LastPlayed,
            PlaytimeMinutes = entry.PlaytimeMinutes,
            IsManual = entry.IsManual
        };

        private sealed class GameLibraryDocument
        {
            public string ViewMode { get; set; } = "list";
            public List<GameEntry> Games { get; set; } = new();
        }
    }

    public sealed class GameLibraryService : IGameLibraryService
    {
        private readonly GameLibraryStore _store;
        private readonly IReadOnlyList<IGameLibraryProvider> _providers;

        public GameLibraryService(GameLibraryStore store, IEnumerable<IGameLibraryProvider> providers)
        {
            _store = store;
            _providers = providers.ToList();
        }

        public Task<IReadOnlyList<GameEntry>> GetLibraryAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_store.GetGames());
        }

        public Task SaveLibraryAsync(IEnumerable<GameEntry> games, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _store.SaveGames(games);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<GameEntry>> ScanAndMergeAsync(CancellationToken cancellationToken = default)
        {
            List<GameEntry> existing = _store.GetGames().ToList();
            var detected = new List<DetectedGame>();

            foreach (IGameLibraryProvider provider in _providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    IReadOnlyList<DetectedGame> found = await provider.ScanAsync(cancellationToken).ConfigureAwait(false);
                    detected.AddRange(found);
                    App.logger.Info($"[Games] {provider.DisplayName}: found {found.Count} installed game(s).");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    App.logger.Warn(exception, $"[Games] {provider.DisplayName} scan failed.");
                }
            }

            foreach (DetectedGame item in detected)
            {
                GameEntry? match = existing.FirstOrDefault(e =>
                    !e.IsManual &&
                    e.Source == item.Source &&
                    string.Equals(e.ExternalId, item.ExternalId, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    existing.Add(new GameEntry
                    {
                        Name = item.Name,
                        Source = item.Source,
                        ExternalId = item.ExternalId,
                        InstallPath = item.InstallPath,
                        ExecutablePath = item.ExecutablePath,
                        LaunchUri = item.LaunchUri,
                        LaunchArgs = item.LaunchArgs,
                        WorkingDirectory = item.WorkingDirectory,
                        IconPath = item.IconPath,
                        IsManual = false
                    });
                    continue;
                }

                match.Name = item.Name;
                match.InstallPath = item.InstallPath;
                match.ExecutablePath = item.ExecutablePath;
                match.LaunchUri = item.LaunchUri;
                match.LaunchArgs = item.LaunchArgs;
                match.WorkingDirectory = item.WorkingDirectory;
                if (!string.IsNullOrWhiteSpace(item.IconPath))
                {
                    match.IconPath = item.IconPath;
                }
            }

            _store.SaveGames(existing);
            return _store.GetGames();
        }

        public Task<GameEntry> AddManualGameAsync(
            string name,
            string executablePath,
            string? iconPath = null,
            string? launchArgs = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new FileNotFoundException("Executable was not found.", executablePath);
            }

            string resolvedName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(executablePath)
                : name.Trim();

            var entry = new GameEntry
            {
                Name = resolvedName,
                Source = GameSource.Manual,
                ExecutablePath = Path.GetFullPath(executablePath),
                InstallPath = Path.GetDirectoryName(Path.GetFullPath(executablePath)),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath)),
                LaunchArgs = string.IsNullOrWhiteSpace(launchArgs) ? null : launchArgs.Trim(),
                IconPath = string.IsNullOrWhiteSpace(iconPath) ? Path.GetFullPath(executablePath) : iconPath,
                IsManual = true
            };

            List<GameEntry> games = _store.GetGames().ToList();
            games.Add(entry);
            _store.SaveGames(games);
            return Task.FromResult(entry);
        }

        public GamesLibraryViewMode GetViewMode() => _store.GetViewMode();

        public void SetViewMode(GamesLibraryViewMode mode) => _store.SetViewMode(mode);

        public Task RemoveGameAsync(string gameId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<GameEntry> games = _store.GetGames().Where(g => g.Id != gameId).ToList();
            _store.SaveGames(games);
            return Task.CompletedTask;
        }

        public Task UpdateGameAsync(GameEntry entry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<GameEntry> games = _store.GetGames().ToList();
            int index = games.FindIndex(g => g.Id == entry.Id);
            if (index < 0)
            {
                games.Add(entry);
            }
            else
            {
                games[index] = entry;
            }

            _store.SaveGames(games);
            return Task.CompletedTask;
        }
    }
}
