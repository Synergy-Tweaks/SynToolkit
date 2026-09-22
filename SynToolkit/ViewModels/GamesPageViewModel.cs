#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynToolkit.Models.Games;
using SynToolkit.Services.Games;

namespace SynToolkit.ViewModels
{
    public partial class GamesPageViewModel : ObservableObject
    {
        private readonly IGameLibraryService _libraryService;
        private readonly IGameLaunchService _launchService;
        private readonly IGameArtworkService _artworkService;
        private CancellationTokenSource? _scanCts;
        private CancellationTokenSource? _artworkCts;
        private List<GameLibraryItemViewModel> _allGames = new();

        public GamesPageViewModel(
            IGameLibraryService libraryService,
            IGameLaunchService launchService,
            IGameArtworkService artworkService)
        {
            _libraryService = libraryService;
            _launchService = launchService;
            _artworkService = artworkService;
            IsGridView = _libraryService.GetViewMode() == GamesLibraryViewMode.Grid;
        }

        public ObservableCollection<GameLibraryItemViewModel> Games { get; } = new();

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SearchText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsGridView { get; set; }

        public bool IsListView => !IsGridView;
        public bool HasGames => Games.Count > 0;
        public bool ShowEmptyState => !IsBusy && Games.Count == 0;

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnIsGridViewChanged(bool value)
        {
            OnPropertyChanged(nameof(IsListView));
            _libraryService.SetViewMode(value ? GamesLibraryViewMode.Grid : GamesLibraryViewMode.List);
        }

        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;
                HasError = false;
                StatusMessage = App.GetValueFromItemList("GamesPage_Loading");
                IReadOnlyList<GameEntry> library = await _libraryService.GetLibraryAsync().ConfigureAwait(true);
                ReplaceGames(library);
                StatusMessage = library.Count == 0
                    ? App.GetValueFromItemList("GamesPage_EmptyHint")
                    : string.Format(App.GetValueFromItemList("GamesPage_LibraryCount"), library.Count);
                StartArtworkRefresh(library);
            }
            catch (Exception exception)
            {
                HasError = true;
                StatusMessage = exception.Message;
                App.logger.Warn(exception, "Failed to load game library.");
            }
            finally
            {
                IsBusy = false;
                NotifyCollectionFlags();
            }
        }

        [RelayCommand]
        private async Task ScanAsync()
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            CancellationToken token = _scanCts.Token;

            try
            {
                IsBusy = true;
                HasError = false;
                StatusMessage = App.GetValueFromItemList("GamesPage_Scanning");
                IReadOnlyList<GameEntry> library = await _libraryService.ScanAndMergeAsync(token).ConfigureAwait(true);
                ReplaceGames(library);
                StatusMessage = string.Format(App.GetValueFromItemList("GamesPage_ScanComplete"), library.Count);
                StartArtworkRefresh(library);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = App.GetValueFromItemList("GamesPage_ScanCancelled");
            }
            catch (Exception exception)
            {
                HasError = true;
                StatusMessage = exception.Message;
                App.logger.Warn(exception, "Game scan failed.");
            }
            finally
            {
                IsBusy = false;
                NotifyCollectionFlags();
            }
        }

        public async Task AddManualAsync(string name, string executablePath, string? iconPath, string? artworkPath = null)
        {
            try
            {
                IsBusy = true;
                HasError = false;
                GameEntry entry = await _libraryService
                    .AddManualGameAsync(name, executablePath, iconPath)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(artworkPath) && File.Exists(artworkPath))
                {
                    string? cached = await _artworkService
                        .ImportCustomArtworkAsync(entry.Id, artworkPath)
                        .ConfigureAwait(true);
                    if (!string.IsNullOrWhiteSpace(cached))
                    {
                        entry.ArtworkPath = cached;
                        entry.IsCustomArtwork = true;
                        await _libraryService.UpdateGameAsync(entry).ConfigureAwait(true);
                    }
                }

                await LoadAsync().ConfigureAwait(true);
                StatusMessage = App.GetValueFromItemList("GamesPage_ManualAdded");
            }
            catch (Exception exception)
            {
                HasError = true;
                StatusMessage = exception.Message;
            }
            finally
            {
                IsBusy = false;
                NotifyCollectionFlags();
            }
        }

        [RelayCommand]
        private void ShowListView() => IsGridView = false;

        [RelayCommand]
        private void ShowGridView() => IsGridView = true;

        [RelayCommand]
        private async Task PlayAsync(GameLibraryItemViewModel? item)
        {
            if (item is null)
            {
                return;
            }

            GameLaunchResult result = await _launchService.LaunchAsync(item.Entry).ConfigureAwait(true);
            if (!result.Success)
            {
                HasError = true;
                StatusMessage = result.Message ?? App.GetValueFromItemList("GamesPage_LaunchFailed");
                return;
            }

            HasError = false;
            StatusMessage = string.Format(App.GetValueFromItemList("GamesPage_Launching"), item.Name);
            item.RefreshFromEntry();
        }

        [RelayCommand]
        private async Task RemoveAsync(GameLibraryItemViewModel? item)
        {
            if (item is null)
            {
                return;
            }

            await _libraryService.RemoveGameAsync(item.Entry.Id).ConfigureAwait(true);
            _allGames.Remove(item);
            ApplyFilter();
            StatusMessage = App.GetValueFromItemList("GamesPage_Removed");
            NotifyCollectionFlags();
        }

        private void StartArtworkRefresh(IReadOnlyList<GameEntry> library)
        {
            _artworkCts?.Cancel();
            _artworkCts = new CancellationTokenSource();
            CancellationToken token = _artworkCts.Token;
            _ = RefreshArtworkAsync(library, token);
        }

        private async Task RefreshArtworkAsync(IReadOnlyList<GameEntry> library, CancellationToken token)
        {
            try
            {
                var updated = new List<GameEntry>();
                foreach (GameEntry entry in library)
                {
                    token.ThrowIfCancellationRequested();
                    bool changed = await _artworkService.EnsureArtworkAsync(entry, token).ConfigureAwait(false);
                    if (changed)
                    {
                        updated.Add(entry);
                    }
                }

                if (updated.Count == 0)
                {
                    return;
                }

                foreach (GameEntry entry in updated)
                {
                    await _libraryService.UpdateGameAsync(entry, token).ConfigureAwait(false);
                }

                await MarshalToUiAsync(() =>
                {
                    foreach (GameEntry entry in updated)
                    {
                        GameLibraryItemViewModel? item = _allGames.FirstOrDefault(g => g.Entry.Id == entry.Id);
                        if (item is null)
                        {
                            continue;
                        }

                        item.Entry.ArtworkPath = entry.ArtworkPath;
                        item.Entry.IsCustomArtwork = entry.IsCustomArtwork;
                        item.RefreshFromEntry();
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer load/scan.
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Background artwork refresh failed.");
            }
        }

        private static Task MarshalToUiAsync(Action action)
        {
            Microsoft.UI.Dispatching.DispatcherQueue? dispatcher =
                App.m_window?.DispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcher is null)
            {
                action();
                return Task.CompletedTask;
            }

            if (dispatcher.HasThreadAccess)
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource();
            if (!dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        action();
                        tcs.SetResult();
                    }
                    catch (Exception exception)
                    {
                        tcs.SetException(exception);
                    }
                }))
            {
                tcs.SetException(new InvalidOperationException("Unable to marshal artwork updates to the UI thread."));
            }

            return tcs.Task;
        }

        private void ReplaceGames(IReadOnlyList<GameEntry> library)
        {
            _allGames = library
                .Select(entry => new GameLibraryItemViewModel(entry, PlayCommand, RemoveCommand))
                .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchText?.Trim() ?? string.Empty;
            IEnumerable<GameLibraryItemViewModel> filtered = string.IsNullOrWhiteSpace(query)
                ? _allGames
                : _allGames.Where(g =>
                    g.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    g.SourceLabel.Contains(query, StringComparison.OrdinalIgnoreCase));

            Games.Clear();
            foreach (GameLibraryItemViewModel item in filtered.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            {
                Games.Add(item);
            }

            NotifyCollectionFlags();
        }

        private void NotifyCollectionFlags()
        {
            OnPropertyChanged(nameof(HasGames));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    public partial class GameLibraryItemViewModel : ObservableObject
    {
        public GameLibraryItemViewModel(
            GameEntry entry,
            IAsyncRelayCommand playCommand,
            IAsyncRelayCommand removeCommand)
        {
            Entry = entry;
            PlayCommand = playCommand;
            RemoveCommand = removeCommand;
            RefreshFromEntry();
        }

        public GameEntry Entry { get; }

        public IAsyncRelayCommand PlayCommand { get; }
        public IAsyncRelayCommand RemoveCommand { get; }

        [ObservableProperty]
        public partial string Name { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SourceLabel { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string? IconPath { get; set; }

        [ObservableProperty]
        public partial string? CoverArtPath { get; set; }

        [ObservableProperty]
        public partial bool HasCoverArt { get; set; }

        [ObservableProperty]
        public partial bool HasListIcon { get; set; }

        [ObservableProperty]
        public partial string? ListIconPath { get; set; }

        [ObservableProperty]
        public partial string PathSummary { get; set; } = string.Empty;

        public void RefreshFromEntry()
        {
            Name = Entry.Name;
            SourceLabel = Entry.Source switch
            {
                GameSource.Steam => "Steam",
                GameSource.Epic => "Epic",
                GameSource.Gog => "GOG",
                GameSource.Xbox => "Xbox",
                _ => "Manual"
            };
            IconPath = Entry.IconPath;
            PathSummary = Entry.InstallPath
                ?? Entry.ExecutablePath
                ?? Entry.LaunchUri
                ?? string.Empty;

            CoverArtPath = IsUsableImage(Entry.ArtworkPath) ? Entry.ArtworkPath : null;
            HasCoverArt = CoverArtPath is not null;

            // List view: prefer cover art, then extracted/exe icon, else generic glyph.
            if (HasCoverArt)
            {
                ListIconPath = CoverArtPath;
                HasListIcon = true;
            }
            else if (IsUsableImage(Entry.IconPath) || IsLikelyExeIcon(Entry.IconPath))
            {
                ListIconPath = Entry.IconPath;
                HasListIcon = true;
            }
            else
            {
                ListIconPath = null;
                HasListIcon = false;
            }
        }

        private static bool IsUsableImage(string? path) =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            new FileInfo(path).Length > 0 &&
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        private static bool IsLikelyExeIcon(string? path) =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(path) &&
            (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }
}
