#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private CancellationTokenSource? _scanCts;
        private List<GameLibraryItemViewModel> _allGames = new();

        public GamesPageViewModel(IGameLibraryService libraryService, IGameLaunchService launchService)
        {
            _libraryService = libraryService;
            _launchService = launchService;
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

        public bool HasGames => Games.Count > 0;
        public bool ShowEmptyState => !IsBusy && Games.Count == 0;

        partial void OnSearchTextChanged(string value) => ApplyFilter();

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

        public async Task AddManualAsync(string name, string executablePath, string? iconPath)
        {
            try
            {
                IsBusy = true;
                HasError = false;
                await _libraryService.AddManualGameAsync(name, executablePath, iconPath).ConfigureAwait(true);
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
        }
    }
}
