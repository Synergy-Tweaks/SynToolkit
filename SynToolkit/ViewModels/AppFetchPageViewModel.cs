#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynToolkit.Services;
using SynToolkit.Services.ConfigurationServices;

namespace SynToolkit.ViewModels
{
    /// <summary>
    /// Drives the App Fetch page's search box and results list. Ported from AME.AppFetch's
    /// Handler (https://github.com/Ameliorated-LLC/appfetch, MIT License, Copyright (c)
    /// Ameliorated LLC).
    /// </summary>
    public partial class AppFetchPageViewModel : ObservableObject
    {
        private const int MaximumStoreSearchResults = 24;
        private readonly AppFetchService _service;
        private readonly WingetInstallerService _wingetInstallerService;
        private readonly IConfigurationService _xboxServicesConfigurationService;
        private readonly IReadOnlyList<FeaturedInstallerViewModel> _allFeaturedInstallers;
        private CancellationTokenSource? _installQueueCancellationTokenSource;

        public event Action<int>? AvailableInstallerUpdateCountChanged;

        public ObservableCollection<AppFetchItemViewModel> Results { get; } = new();

        public ObservableCollection<FeaturedInstallerViewModel> FeaturedInstallers { get; } = new();

        public ObservableCollection<InstallerQueueItemViewModel> InstallQueue { get; } = new();

        public IReadOnlyList<string> Categories { get; } =
            ["All", "Browser", "Communication", "Gaming", "Utility", "Media", "Creator", "Development", "Productivity", "System", "Community"];

        public IReadOnlyList<string> AvailabilityFilters { get; } =
            [
                App.GetValueFromItemList("Installer_FilterAll"),
                App.GetValueFromItemList("Installer_FilterInstalled"),
                App.GetValueFromItemList("Installer_FilterNotInstalled"),
                App.GetValueFromItemList("Installer_FilterNeedsUpdate")
            ];

        private string _catalogSearchText = string.Empty;

        public string CatalogSearchText
        {
            get => _catalogSearchText;
            set
            {
                if (SetProperty(ref _catalogSearchText, value ?? string.Empty))
                {
                    ApplyCatalogFilter();
                }
            }
        }

        public string CatalogResultsSummary =>
            $"{FeaturedInstallers.Count} of {_allFeaturedInstallers.Count} apps shown";

        public bool HasNoCatalogResults => FeaturedInstallers.Count == 0;

        public int SelectedCount => _allFeaturedInstallers.Count(item => item.IsSelected);

        public string SelectedSummary => SelectedCount == 0
            ? "Build your install queue"
            : $"{SelectedCount} app{(SelectedCount == 1 ? string.Empty : "s")} queued";

        public string InstallSelectedText => SelectedCount == 0
            ? "Install queue"
            : $"Install queue ({SelectedCount})";

        public bool CanInstallSelected => SelectedCount > 0 && !IsInstallingQueue;

        public bool CanDismissQueue => IsQueueVisible && !IsInstallingQueue;

        public bool CanRetryFailed => IsQueueVisible &&
            !IsInstallingQueue &&
            InstallQueue.Any(item => item.State == InstallerQueueState.Failed);

        public bool CanRefreshInstallerStates => !IsRefreshingInstallerStates && !IsInstallingQueue;

        [ObservableProperty]
        public partial string SelectedCategory { get; set; } = "All";

        [ObservableProperty]
        public partial string SelectedAvailabilityFilter { get; set; } = App.GetValueFromItemList("Installer_FilterAll");

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanInstallSelected))]
        [NotifyPropertyChangedFor(nameof(CanDismissQueue))]
        [NotifyPropertyChangedFor(nameof(CanRetryFailed))]
        [NotifyPropertyChangedFor(nameof(CanRefreshInstallerStates))]
        public partial bool IsInstallingQueue { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanDismissQueue))]
        public partial bool IsQueueVisible { get; set; }

        [ObservableProperty]
        public partial string QueueSummary { get; set; } = string.Empty;

        private bool _isRefreshingInstallerStates;
        private string _installerStatusSummary = "Checking installed apps...";

        public bool IsRefreshingInstallerStates
        {
            get => _isRefreshingInstallerStates;
            private set
            {
                if (SetProperty(ref _isRefreshingInstallerStates, value))
                {
                    OnPropertyChanged(nameof(CanRefreshInstallerStates));
                }
            }
        }

        public string InstallerStatusSummary
        {
            get => _installerStatusSummary;
            private set => SetProperty(ref _installerStatusSummary, value);
        }

        internal static IReadOnlyList<FeaturedInstallerViewModel> CreateFeaturedInstallers() =>
            new List<FeaturedInstallerViewModel>
            {
                // Browsers
                new("Google Chrome", "Browser", "Fast, familiar browsing from Google.", "ms-appx:///assets/Icons/Installers/google-chrome.svg", "https://www.google.com/chrome/download-chrome/", "Google.Chrome", ["Google Chrome"], isEssential: true),
                new("Mozilla Firefox", "Browser", "Private, open-source web browsing.", "ms-appx:///assets/Icons/Installers/mozilla-firefox.svg", "https://www.mozilla.org/firefox/new/", "Mozilla.Firefox", ["Mozilla Firefox"]),
                new("Brave", "Browser", "Privacy-focused browser with built-in blocking.", "ms-appx:///assets/Icons/Installers/brave.svg", "https://brave.com/download/", "Brave.Brave", ["Brave"]),
                new("Opera", "Browser", "Feature-rich browser with workspaces and sidebar tools.", "ms-appx:///assets/Icons/Installers/opera.svg", "https://www.opera.com/download", "Opera.Opera", ["Opera Stable"]),
                new("Vivaldi", "Browser", "Highly customizable browser for power users.", "ms-appx:///assets/Icons/Installers/vivaldi.svg", "https://vivaldi.com/download/", "Vivaldi.Vivaldi", ["Vivaldi"]),
                new("Thorium", "Browser", "Performance-focused Chromium browser with extra optimizations.", "ms-appx:///assets/Icons/Installers/thorium.svg", "https://thorium.rocks/", "Alex313031.Thorium", ["Thorium"]),
                new("Helium", "Browser", "Private, lightweight Chromium browsing without distractions.", "ms-appx:///assets/Icons/Installers/helium.svg", "https://helium.computer/", "ImputNet.Helium", ["Helium"]),

                // Communication
                new("Discord", "Communication", "Voice, video, and chat for communities.", "ms-appx:///assets/Icons/Installers/discord.svg", "https://discord.com/download", "Discord.Discord", ["Discord"], isEssential: true, silentArgumentsOverride: "-s"),
                new("Telegram Desktop", "Communication", "Fast messaging with synced chats and large groups.", "ms-appx:///assets/Icons/Installers/telegram.svg", "https://desktop.telegram.org/", "Telegram.TelegramDesktop", ["Telegram Desktop"]),
                new("Vesktop", "Communication", "A desktop Discord client with Vencord built in.", "ms-appx:///assets/Icons/Installers/vencord.svg", "https://vencord.dev/download/", "Vencord.Vesktop", ["Vesktop"]),

                // Gaming
                new("Steam", "Gaming", "PC games, updates, and your game library.", "ms-appx:///assets/Icons/Installers/steam.svg", "https://store.steampowered.com/about/", "Valve.Steam", ["Steam"]),
                new("Epic Games Launcher", "Gaming", "Epic Games Store library and game launcher.", "ms-appx:///assets/Icons/Installers/epic-games.svg", "https://store.epicgames.com/download", "EpicGames.EpicGamesLauncher", ["Epic Games Launcher"]),
                new("EA app", "Gaming", "Electronic Arts games, downloads, updates, and friends.", "ms-appx:///assets/Icons/Installers/ea-app.svg", "https://www.ea.com/ea-app", "ElectronicArts.EADesktop", ["EA app"]),
                new("Battle.net", "Gaming", "Blizzard games, updates, friends, and news.", "ms-appx:///assets/Icons/Installers/battle-net.svg", "https://download.battle.net/", "Blizzard.BattleNet", ["Battle.net"]),
                new("Riot Games / League", "Gaming", "Riot Client and League of Legends for the EU West region.", "ms-appx:///assets/Icons/Installers/riot-games.svg", "https://www.leagueoflegends.com/download/", "RiotGames.LeagueOfLegends.EUW", ["Riot Client", "League of Legends"]),
                new("Ubisoft Connect", "Gaming", "Ubisoft games, rewards, friends, and updates.", "ms-appx:///assets/Icons/Installers/ubisoft.svg", "https://ubisoftconnect.com/", "Ubisoft.Connect", ["Ubisoft Connect"]),
                new("GOG Galaxy", "Gaming", "DRM-free games and connected game libraries.", "ms-appx:///assets/Icons/Installers/gog-galaxy.svg", "https://www.gog.com/galaxy", "GOG.Galaxy", ["GOG GALAXY"]),
                new("Rockstar Games Launcher", "Gaming", "Rockstar games, cloud saves, updates, and store access.", "ms-appx:///assets/Icons/Installers/rockstar-games.svg", "https://socialclub.rockstargames.com/rockstar-games-launcher", "RockstarGames.Launcher", ["Rockstar Games Launcher"]),
                new("Amazon Games", "Gaming", "Claim, install, and manage games from Amazon.", "ms-appx:///assets/Icons/Installers/amazon-games.svg", "https://www.amazongames.com/", "Amazon.Games", ["Amazon Games"]),
                new("Playnite", "Gaming", "Open-source library manager for all your PC games.", "ms-appx:///assets/Icons/Installers/playnite.svg", "https://playnite.link/download.html", "Playnite.Playnite", ["Playnite"]),
                new("Prism Launcher", "Gaming", "Open-source Minecraft launcher with instance management.", "ms-appx:///assets/Icons/Installers/prism-launcher.svg", "https://prismlauncher.org/download/", "PrismLauncher.PrismLauncher", ["Prism Launcher"]),
                new("Heroic Games Launcher", "Gaming", "Open-source launcher for Epic, GOG, and Amazon libraries.", "ms-appx:///assets/Icons/Installers/heroic-games.svg", "https://heroicgameslauncher.com/downloads", "HeroicGamesLauncher.HeroicGamesLauncher", ["Heroic"]),
                new("CurseForge", "Gaming", "Discover and manage mods and add-ons for popular games.", "ms-appx:///assets/Icons/Installers/curseforge.svg", "https://www.curseforge.com/download/app", "Overwolf.CurseForge", ["CurseForge"]),

                // Utilities
                new("7-Zip", "Utility", "Lightweight file compression and extraction.", "ms-appx:///assets/Icons/Installers/7zip.svg", "https://www.7-zip.org/download.html", "7zip.7zip", ["7-Zip"], isEssential: true),
                new("WinRAR", "Utility", "Archive manager for RAR, ZIP, and other formats.", "ms-appx:///assets/Icons/Installers/winrar.svg", "https://www.win-rar.com/download.html", "RARLab.WinRAR", ["WinRAR"]),
                new("Everything", "Utility", "Instant file and folder search for Windows.", "ms-appx:///assets/Icons/Installers/everything.svg", "https://www.voidtools.com/downloads/", "voidtools.Everything", ["Everything"]),
                new("Microsoft PowerToys", "Utility", "Advanced Windows tools for productivity and customization.", "ms-appx:///assets/Icons/Installers/powertoys.svg", "https://learn.microsoft.com/windows/powertoys/install", "Microsoft.PowerToys", ["PowerToys"]),
                new("Notepad++", "Utility", "Fast text and source-code editor with plugins.", "ms-appx:///assets/Icons/Installers/notepad-plus-plus.svg", "https://notepad-plus-plus.org/downloads/", "Notepad++.Notepad++", ["Notepad++"]),
                new("qBittorrent", "Utility", "Open-source BitTorrent client without ads.", "ms-appx:///assets/Icons/Installers/qbittorrent.svg", "https://www.qbittorrent.org/download", "qBittorrent.qBittorrent", ["qBittorrent"]),

                // Media
                new("VLC media player", "Media", "Free media player with broad format support.", "ms-appx:///assets/Icons/Installers/vlc.svg", "https://www.videolan.org/vlc/", "VideoLAN.VLC", ["VLC media player"], isEssential: true),
                new("Spotify", "Media", "Music, podcasts, playlists, and offline listening.", "ms-appx:///assets/Icons/Installers/spotify.svg", "https://www.spotify.com/download/windows/", "Spotify.Spotify", ["Spotify"]),
                new("HandBrake", "Media", "Convert and compress video into modern formats.", "ms-appx:///assets/Icons/Installers/handbrake.svg", "https://handbrake.fr/downloads.php", "HandBrake.HandBrake", ["HandBrake"]),
                new("Audacity", "Media", "Record and edit multi-track audio.", "ms-appx:///assets/Icons/Installers/audacity.svg", "https://www.audacityteam.org/download/windows/", "Audacity.Audacity", ["Audacity"]),

                // Creative tools
                new("OBS Studio", "Creator", "Record and stream video from your PC.", "ms-appx:///assets/Icons/Installers/obs-studio.svg", "https://obsproject.com/download", "OBSProject.OBSStudio", ["OBS Studio"]),
                new("GIMP", "Creator", "Open-source image editing and photo retouching.", "ms-appx:///assets/Icons/Installers/gimp.svg", "https://www.gimp.org/downloads/", "GIMP.GIMP.3", ["GIMP"]),
                new("Blender", "Creator", "3D modeling, animation, rendering, and video editing.", "ms-appx:///assets/Icons/Installers/blender.svg", "https://www.blender.org/download/", "BlenderFoundation.Blender", ["Blender"]),
                new("Krita", "Creator", "Digital painting and illustration for artists.", "ms-appx:///assets/Icons/Installers/krita.svg", "https://krita.org/en/download/", "KDE.Krita", ["Krita"]),

                // Development
                new("Visual Studio Code", "Development", "Extensible editor for code, scripts, and projects.", "ms-appx:///assets/Icons/Installers/vscode.svg", "https://code.visualstudio.com/download", "Microsoft.VisualStudioCode", ["Microsoft Visual Studio Code"]),
                new("Git", "Development", "Distributed version control for source-code projects.", "ms-appx:///assets/Icons/Installers/git.svg", "https://git-scm.com/download/win", "Git.Git", ["Git"]),
                new("Node.js LTS", "Development", "Long-term-support JavaScript runtime and npm.", "ms-appx:///assets/Icons/Installers/nodejs.svg", "https://nodejs.org/en/download", "OpenJS.NodeJS.LTS", ["Node.js"]),
                new("Python 3.13", "Development", "Python interpreter, standard library, and launcher.", "ms-appx:///assets/Icons/Installers/python.svg", "https://www.python.org/downloads/windows/", "Python.Python.3.13", ["Python 3.13"]),
                new("GitHub Desktop", "Development", "Visual Git and GitHub workflow for desktop.", "ms-appx:///assets/Icons/Installers/github-desktop.svg", "https://desktop.github.com/download/", "GitHub.GitHubDesktop", ["GitHub Desktop"]),

                // Productivity
                new("LibreOffice", "Productivity", "Open-source documents, spreadsheets, and presentations.", "ms-appx:///assets/Icons/Installers/libreoffice.svg", "https://www.libreoffice.org/download/download-libreoffice/", "TheDocumentFoundation.LibreOffice", ["LibreOffice"]),
                new("Obsidian", "Productivity", "Private Markdown notes with links and plugins.", "ms-appx:///assets/Icons/Installers/obsidian.svg", "https://obsidian.md/download", "Obsidian.Obsidian", ["Obsidian"]),
                new("SumatraPDF", "Productivity", "Lightweight PDF, ebook, and document reader.", "ms-appx:///assets/Icons/Installers/sumatra-pdf.svg", "https://www.sumatrapdfreader.org/download-free-pdf-viewer", "SumatraPDF.SumatraPDF", ["SumatraPDF"]),
                new("Bitwarden", "Productivity", "Open-source password manager with secure sync.", "ms-appx:///assets/Icons/Installers/bitwarden.svg", "https://bitwarden.com/download/", "Bitwarden.Bitwarden", ["Bitwarden"]),

                // System runtimes
                new("Visual C++ Runtime", "System", "Official Microsoft runtime for desktop apps and games.", "ms-appx:///assets/Icons/Installers/cplusplus.svg", "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist?view=msvc-170", "Microsoft.VCRedist.2015+.x64", ["Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Visual C++ 2015-2019 Redistributable (x64)"], isEssential: true),
                new(".NET Desktop Runtime 8", "System", "Microsoft runtime required by many modern Windows apps.", "ms-appx:///assets/Icons/Installers/dotnet.svg", "https://dotnet.microsoft.com/download/dotnet/8.0", "Microsoft.DotNet.DesktopRuntime.8", ["Microsoft Windows Desktop Runtime - 8"]),
                new("NVIDIA App", "System", App.GetValueFromItemList("Installer_NvidiaAppDescription"), "ms-appx:///assets/Icons/Nvidia.png", "https://www.nvidia.com/en-us/software/nvidia-app/", "Manual.NvidiaApp", ["NVIDIA App"], isManualOnly: true),
                new("NVIDIA Control Panel", "System", App.GetValueFromItemList("Installer_NvidiaControlPanelDescription"), "ms-appx:///assets/Icons/Nvidia.png", "https://apps.microsoft.com/detail/9NF8H0H7WMLT", "9NF8H0H7WMLT", ["NVIDIA Control Panel"], packageSource: "msstore"),
                new("AMD Software: Adrenalin Edition", "System", App.GetValueFromItemList("Installer_AmdAdrenalinDescription"), "ms-appx:///assets/Icons/Amd.png", "https://www.amd.com/en/products/software/adrenalin.html", "Manual.AmdAdrenalin", ["AMD Software", "AMD Adrenalin Edition"], isManualOnly: true),

                // Community modifications require their official, visible setup flow.
                new("SpotX", "Community", "Community Spotify customization and patching tool.", "ms-appx:///assets/Icons/Installers/spotx.svg", "https://github.com/SpotX-Official/SpotX", "Community.SpotX", [], isManualOnly: true),
                new("Vencord", "Community", "Community client modification for the Discord desktop app.", "ms-appx:///assets/Icons/Installers/vencord.svg", "https://vencord.dev/download/", "Community.Vencord", [], isManualOnly: true)
            };

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// True when SynToolkit's Xbox-services debloat tweak is currently applied,
        /// which can prevent Xbox/Gaming-related Store apps from installing or running.
        /// Drives visibility of the disclosure banner and its revert button.
        /// </summary>
        [ObservableProperty]
        public partial bool IsXboxServicesTweakApplied { get; set; }

        [ObservableProperty]
        public partial bool IsRevertingXboxServicesTweak { get; set; }

        public bool IsRevertXboxServicesButtonEnabled => !IsRevertingXboxServicesTweak;

        partial void OnIsRevertingXboxServicesTweakChanged(bool value) =>
            OnPropertyChanged(nameof(IsRevertXboxServicesButtonEnabled));

        private Task? _installedPackagesTask;
        private CancellationTokenSource? _searchCancellationTokenSource;
        private int _searchGeneration;

        public AppFetchPageViewModel(
            AppFetchService service,
            WingetInstallerService wingetInstallerService,
            [Microsoft.Extensions.DependencyInjection.FromKeyedServices("XboxServices")] IConfigurationService xboxServicesConfigurationService)
        {
            _service = service;
            _wingetInstallerService = wingetInstallerService;
            _xboxServicesConfigurationService = xboxServicesConfigurationService;
            _allFeaturedInstallers = CreateFeaturedInstallers();

            foreach (FeaturedInstallerViewModel installer in _allFeaturedInstallers)
            {
                installer.PropertyChanged += FeaturedInstaller_PropertyChanged;
            }

            ApplyCatalogFilter();
            RefreshXboxServicesTweakState();
        }

        partial void OnSelectedCategoryChanged(string value) => ApplyCatalogFilter();

        partial void OnSelectedAvailabilityFilterChanged(string value) => ApplyCatalogFilter();

        private void ApplyCatalogFilter()
        {
            string searchTerm = CatalogSearchText.Trim();
            FeaturedInstallers.Clear();
            foreach (FeaturedInstallerViewModel installer in _allFeaturedInstallers.Where(
                installer => (SelectedCategory == "All" || installer.Category == SelectedCategory) &&
                    (SelectedAvailabilityFilter == AvailabilityFilters[0] ||
                        (SelectedAvailabilityFilter == AvailabilityFilters[1] && installer.AvailabilityState is InstallerAvailabilityState.Installed or InstallerAvailabilityState.UpdateAvailable) ||
                        (SelectedAvailabilityFilter == AvailabilityFilters[2] && installer.AvailabilityState == InstallerAvailabilityState.NotInstalled) ||
                        (SelectedAvailabilityFilter == AvailabilityFilters[3] && installer.AvailabilityState == InstallerAvailabilityState.UpdateAvailable)) &&
                    (searchTerm.Length == 0 ||
                        installer.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        installer.Category.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        installer.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        installer.PackageIdentifier.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))))
            {
                FeaturedInstallers.Add(installer);
            }

            OnPropertyChanged(nameof(CatalogResultsSummary));
            OnPropertyChanged(nameof(HasNoCatalogResults));
        }

        private void FeaturedInstaller_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(FeaturedInstallerViewModel.IsSelected))
            {
                NotifySelectionChanged();
            }
            else if (eventArgs.PropertyName == nameof(FeaturedInstallerViewModel.AvailabilityState))
            {
                ApplyCatalogFilter();
            }
        }

        private void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedSummary));
            OnPropertyChanged(nameof(InstallSelectedText));
            OnPropertyChanged(nameof(CanInstallSelected));
        }

        [RelayCommand]
        private void SelectEssentials()
        {
            foreach (FeaturedInstallerViewModel installer in _allFeaturedInstallers)
            {
                installer.IsSelected = installer.IsEssential && installer.CanSelect;
            }
        }

        [RelayCommand]
        private Task RefreshInstallerStates() => RefreshInstallerStatesAsync(CancellationToken.None);

        public async Task RefreshInstallerStatesAsync(CancellationToken cancellationToken)
        {
            if (IsRefreshingInstallerStates || IsInstallingQueue)
            {
                return;
            }

            IsRefreshingInstallerStates = true;
            InstallerStatusSummary = "Checking installed apps and available updates...";

            try
            {
                IReadOnlyList<CuratedPackageStatus> statuses =
                    await _wingetInstallerService.DetectPackageStatusesAsync(
                        _allFeaturedInstallers
                            .Where(installer => !installer.IsManualOnly)
                            .Select(installer => new CuratedPackageProbe(
                                installer.PackageIdentifier,
                                installer.InstalledDisplayNamePrefixes,
                                installer.PackageSource))
                            .ToList(),
                        cancellationToken);

                Dictionary<string, CuratedPackageStatus> statusesByIdentifier = statuses.ToDictionary(
                    status => status.PackageIdentifier,
                    StringComparer.OrdinalIgnoreCase);

                foreach (FeaturedInstallerViewModel installer in _allFeaturedInstallers)
                {
                    if (installer.IsManualOnly)
                    {
                        continue;
                    }

                    if (!statusesByIdentifier.TryGetValue(installer.PackageIdentifier, out CuratedPackageStatus? status))
                    {
                        installer.ApplyAvailabilityState(InstallerAvailabilityState.Unavailable);
                        continue;
                    }

                    InstallerAvailabilityState availabilityState = !status.IsInstalled
                        ? InstallerAvailabilityState.NotInstalled
                        : status.IsUpdateAvailable
                            ? InstallerAvailabilityState.UpdateAvailable
                            : InstallerAvailabilityState.Installed;
                    installer.ApplyAvailabilityState(
                        availabilityState,
                        status.InstalledVersion,
                        status.AvailableVersion,
                        status.IsUpdateCheckComplete);
                }

                int installedCount = _allFeaturedInstallers.Count(installer =>
                    installer.AvailabilityState is InstallerAvailabilityState.Installed or
                        InstallerAvailabilityState.UpdateAvailable);
                int updateCount = _allFeaturedInstallers.Count(installer =>
                    installer.AvailabilityState == InstallerAvailabilityState.UpdateAvailable);
                int incompleteUpdateCheckCount = _allFeaturedInstallers.Count(installer =>
                    installer.AvailabilityState == InstallerAvailabilityState.Installed &&
                    !installer.IsUpdateCheckComplete);
                AvailableInstallerUpdateCountChanged?.Invoke(updateCount);
                InstallerStatusSummary = (updateCount, incompleteUpdateCheckCount) switch
                {
                    (0, 0) => $"{installedCount} installed • Everything detected is current",
                    (0, _) => $"{installedCount} installed • {incompleteUpdateCheckCount} update check{(incompleteUpdateCheckCount == 1 ? string.Empty : "s")} incomplete",
                    (_, 0) => $"{installedCount} installed • {updateCount} update{(updateCount == 1 ? string.Empty : "s")} available",
                    _ => $"{installedCount} installed • {updateCount} update{(updateCount == 1 ? string.Empty : "s")} available • {incompleteUpdateCheckCount} check{(incompleteUpdateCheckCount == 1 ? string.Empty : "s")} incomplete"
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] Unable to refresh curated app status.");
                foreach (FeaturedInstallerViewModel installer in _allFeaturedInstallers.Where(
                    installer => !installer.IsManualOnly &&
                        installer.AvailabilityState == InstallerAvailabilityState.Checking))
                {
                    installer.ApplyAvailabilityState(InstallerAvailabilityState.Unavailable);
                }

                InstallerStatusSummary = "Some app statuses could not be checked. Select Refresh to retry.";
            }
            finally
            {
                IsRefreshingInstallerStates = false;
            }
        }

        [RelayCommand]
        private void ClearInstallerSelection()
        {
            foreach (FeaturedInstallerViewModel installer in _allFeaturedInstallers)
            {
                installer.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task InstallSelectedAsync()
        {
            if (!CanInstallSelected)
            {
                return;
            }

            List<FeaturedInstallerViewModel> selectedInstallers =
                _allFeaturedInstallers.Where(installer => installer.IsSelected).ToList();
            await InstallInstallersAsync(selectedInstallers);
        }

        public async Task InstallSingleAsync(FeaturedInstallerViewModel installer)
        {
            ArgumentNullException.ThrowIfNull(installer);
            if (IsInstallingQueue || !installer.CanSelect)
            {
                return;
            }

            installer.IsSelected = true;
            await InstallInstallersAsync([installer]);
        }

        public async Task<WingetInstallResult> UninstallSingleAsync(FeaturedInstallerViewModel installer)
        {
            ArgumentNullException.ThrowIfNull(installer);
            if (IsInstallingQueue || !installer.CanUninstall || installer.IsUninstalling)
            {
                return new WingetInstallResult(false, -1, "This app cannot be uninstalled while another package operation is running.", WingetInstallDisposition.Failed);
            }

            installer.IsUninstalling = true;
            HasError = false;
            try
            {
                WingetInstallResult result = await _wingetInstallerService.UninstallAsync(
                    installer.PackageIdentifier,
                    installer.InstalledDisplayNamePrefixes,
                    installer.PackageSource);
                if (!result.Succeeded)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Output)
                        ? $"{installer.Name} could not be uninstalled."
                        : result.Output;
                    HasError = true;
                }

                return result;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] Unable to uninstall {AppName}.", installer.Name);
                ErrorMessage = $"{installer.Name} could not be uninstalled: {exception.Message}";
                HasError = true;
                return new WingetInstallResult(false, -1, exception.Message, WingetInstallDisposition.Failed);
            }
            finally
            {
                installer.IsUninstalling = false;
                await RefreshInstallerStatesAsync(CancellationToken.None);
            }
        }

        private async Task InstallInstallersAsync(IReadOnlyList<FeaturedInstallerViewModel> installers)
        {
            if (IsInstallingQueue || installers.Count == 0)
            {
                return;
            }

            InstallQueue.Clear();
            foreach (FeaturedInstallerViewModel installer in installers)
            {
                InstallQueue.Add(new InstallerQueueItemViewModel(installer));
            }

            _installQueueCancellationTokenSource?.Dispose();
            _installQueueCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _installQueueCancellationTokenSource.Token;

            IsQueueVisible = true;
            IsInstallingQueue = true;
            QueueSummary = "Preparing installation queue...";

            try
            {
                int installedCount = 0;
                int updatedCount = 0;
                int alreadySatisfiedCount = 0;
                int failedCount = 0;

                for (int index = 0; index < InstallQueue.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    InstallerQueueItemViewModel queueItem = InstallQueue[index];
                    queueItem.State = InstallerQueueState.Installing;
                    queueItem.Detail = "Installing";
                    queueItem.DetailToolTip = string.Empty;
                    QueueSummary = $"Installing {index + 1} of {InstallQueue.Count}: {queueItem.Name}";

                    WingetInstallResult result = await _wingetInstallerService.InstallAsync(
                        queueItem.PackageIdentifier,
                        queueItem.Installer.PackageSource,
                        queueItem.Installer.SilentArgumentsOverride,
                        queueItem.Installer.AvailabilityState == InstallerAvailabilityState.UpdateAvailable,
                        new Progress<double>(value =>
                        {
                            queueItem.Progress = value;
                            queueItem.Detail = value < 50
                                ? $"Downloading {Math.Min(value * 2, 100):0}%"
                                : value < 100 ? "Installing" : "Finishing";
                        }),
                        cancellationToken);

                    if (result.Succeeded)
                    {
                        bool wasUpdate = queueItem.Installer.AvailabilityState ==
                            InstallerAvailabilityState.UpdateAvailable;
                        queueItem.State = InstallerQueueState.Completed;
                        queueItem.Detail = result.Disposition == WingetInstallDisposition.AlreadySatisfied
                            ? "Already installed"
                            : wasUpdate ? "Updated" : "Installed";
                        queueItem.DetailToolTip = FormatInstallResultToolTip(result, queueItem.Name);
                        queueItem.Installer.ApplyAvailabilityState(
                            InstallerAvailabilityState.Installed,
                            queueItem.Installer.AvailableVersion ?? queueItem.Installer.InstalledVersion,
                            queueItem.Installer.AvailableVersion);
                        queueItem.Installer.IsSelected = false;
                        if (result.Disposition == WingetInstallDisposition.AlreadySatisfied)
                        {
                            alreadySatisfiedCount++;
                        }
                        else if (wasUpdate)
                        {
                            updatedCount++;
                        }
                        else
                        {
                            installedCount++;
                        }
                    }
                    else
                    {
                        queueItem.State = InstallerQueueState.Failed;
                        queueItem.Detail = FormatInstallFailureDetail(result.ExitCode, queueItem.Name);
                        queueItem.DetailToolTip = FormatInstallResultToolTip(result, queueItem.Name);
                        failedCount++;
                        OnPropertyChanged(nameof(CanRetryFailed));
                    }
                }

                string successSummary = FormatQueueSuccessSummary(installedCount, updatedCount, alreadySatisfiedCount);
                QueueSummary = failedCount == 0
                    ? successSummary + " successfully."
                    : $"{successSummary}, {failedCount} failed. Resolve the message below, then retry.";
            }
            catch (OperationCanceledException)
            {
                foreach (InstallerQueueItemViewModel queueItem in InstallQueue.Where(
                    item => item.State is InstallerQueueState.Pending or InstallerQueueState.Installing))
                {
                    queueItem.State = InstallerQueueState.Canceled;
                    queueItem.Detail = "Canceled";
                    queueItem.DetailToolTip = "Installation canceled.";
                }

                QueueSummary = "Installation queue canceled.";
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] The installation queue failed.");
                InstallerQueueItemViewModel? activeItem = InstallQueue.FirstOrDefault(
                    item => item.State == InstallerQueueState.Installing);
                if (activeItem != null)
                {
                    activeItem.State = InstallerQueueState.Failed;
                    activeItem.Detail = "Unexpected error";
                    activeItem.DetailToolTip = exception.Message;
                }

                QueueSummary = "The installation queue stopped because of an unexpected error.";
            }
            finally
            {
                IsInstallingQueue = false;
                OnPropertyChanged(nameof(CanRetryFailed));
                await RefreshInstallerStatesAsync(CancellationToken.None);
            }
        }

        private static string FormatInstallFailureDetail(int exitCode, string appName) =>
            unchecked((uint)exitCode) switch
            {
                0x8A150056 => "This installer must run without administrator privileges",
                0x8A15005F => "This package requires an explicit install location",
                0x8A150061 or 0x8A15002B => "Already installed",
                0x8A150101 or 0x8A150103 or 0x8A150111 =>
                    $"Close {appName} and related apps, then retry",
                0x8A150102 => "Another installation is running; wait, then retry",
                0x8A150104 or 0x8A150110 => "A required dependency could not be installed",
                0x8A150105 => "Not enough free disk space",
                0x8A150106 => "Not enough available memory",
                0x8A150107 => "Internet connection required",
                0x8A150109 or 0x8A15010A => "Restart Windows, then retry",
                0x8A15010C => "Installation canceled",
                0x8A15010F => "Installation blocked by system policy",
                0x8A150113 => "This package does not support this system",
                0x8A150114 => "Automatic upgrade is not supported; use Website",
                _ => $"Failed (0x{unchecked((uint)exitCode):X8})"
            };

        private static string FormatInstallResultToolTip(WingetInstallResult result, string appName)
        {
            string symbol = GetWingetErrorSymbol(result.ExitCode);
            string explanation = result.Disposition switch
            {
                WingetInstallDisposition.AlreadySatisfied => "This package was already installed or did not need an update.",
                _ when result.Succeeded => "The package operation completed successfully.",
                _ => FormatInstallFailureDetail(result.ExitCode, appName)
            };

            List<string> parts = new();
            if (result.ExitCode != 0)
            {
                parts.Add($"0x{unchecked((uint)result.ExitCode):X8} • {symbol}");
            }

            parts.Add(explanation);

            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                parts.Add(result.Output.Trim());
            }

            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        private static string GetWingetErrorSymbol(int exitCode) =>
            unchecked((uint)exitCode) switch
            {
                0x8A15002B => "APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE",
                0x8A150056 => "APPINSTALLER_CLI_ERROR_INSTALLER_PROHIBITS_ELEVATION",
                0x8A15005F => "APPINSTALLER_CLI_ERROR_INSTALL_LOCATION_REQUIRED",
                0x8A150061 => "APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED",
                0x8A150101 => "APPINSTALLER_CLI_ERROR_INSTALL_PACKAGE_IN_USE",
                0x8A150102 => "APPINSTALLER_CLI_ERROR_INSTALL_INSTALL_IN_PROGRESS",
                0x8A150103 => "APPINSTALLER_CLI_ERROR_INSTALL_FILE_IN_USE",
                0x8A150104 => "APPINSTALLER_CLI_ERROR_INSTALL_MISSING_DEPENDENCY",
                0x8A150105 => "APPINSTALLER_CLI_ERROR_INSTALL_DISK_FULL",
                0x8A150106 => "APPINSTALLER_CLI_ERROR_INSTALL_INSUFFICIENT_MEMORY",
                0x8A150107 => "APPINSTALLER_CLI_ERROR_INSTALL_NETWORK_FAILURE",
                0x8A150109 => "APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_TO_FINISH",
                0x8A15010A => "APPINSTALLER_CLI_ERROR_INSTALL_REBOOT_REQUIRED_FOR_INSTALL",
                0x8A15010C => "APPINSTALLER_CLI_ERROR_INSTALL_CANCELLED",
                0x8A15010F => "APPINSTALLER_CLI_ERROR_INSTALL_SYSTEM_NOT_SUPPORTED",
                0x8A150110 => "APPINSTALLER_CLI_ERROR_INSTALL_UPGRADE_NOT_SUPPORTED",
                0x8A150111 => "APPINSTALLER_CLI_ERROR_INSTALL_BLOCKED_BY_POLICY",
                0x8A150113 => "APPINSTALLER_CLI_ERROR_INSTALL_PLATFORM_UNSUPPORTED",
                0x8A150114 => "APPINSTALLER_CLI_ERROR_INSTALLER_HASH_MISMATCH_OR_MANUAL_UPGRADE_REQUIRED",
                _ => "UNKNOWN_WINGET_ERROR"
            };

        private static string FormatQueueSuccessSummary(int installedCount, int updatedCount, int alreadySatisfiedCount)
        {
            List<string> parts = new();
            if (installedCount > 0)
            {
                parts.Add($"{installedCount} installed");
            }

            if (updatedCount > 0)
            {
                parts.Add($"{updatedCount} updated");
            }

            if (alreadySatisfiedCount > 0)
            {
                parts.Add($"{alreadySatisfiedCount} already installed");
            }

            return parts.Count == 0 ? "No apps completed" : string.Join(", ", parts);
        }

        [RelayCommand]
        private void CancelInstallQueue() => _installQueueCancellationTokenSource?.Cancel();

        [RelayCommand]
        private async Task RetryFailedAsync()
        {
            if (!CanRetryFailed)
            {
                return;
            }

            List<FeaturedInstallerViewModel> failedInstallers = InstallQueue
                .Where(item => item.State == InstallerQueueState.Failed)
                .Select(item => item.Installer)
                .ToList();
            await InstallInstallersAsync(failedInstallers);
        }

        [RelayCommand]
        private void DismissInstallQueue()
        {
            if (!IsInstallingQueue)
            {
                IsQueueVisible = false;
                InstallQueue.Clear();
                OnPropertyChanged(nameof(CanRetryFailed));
            }
        }

        private void RefreshXboxServicesTweakState()
        {
            try
            {
                IsXboxServicesTweakApplied = !_xboxServicesConfigurationService.IsEnabled();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[AppFetch] Unable to read the Xbox-services tweak state.");
                IsXboxServicesTweakApplied = false;
            }
        }

        [RelayCommand]
        private async Task RevertXboxServicesTweakAsync()
        {
            IsRevertingXboxServicesTweak = true;
            try
            {
                await Task.Run(() => _xboxServicesConfigurationService.Enable());
                RefreshXboxServicesTweakState();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Unable to revert the Xbox-services tweak.");
                ErrorMessage = $"Unable to revert the Xbox services tweak: {exception.Message}";
                HasError = true;
            }
            finally
            {
                IsRevertingXboxServicesTweak = false;
            }
        }

        public async Task SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            int searchGeneration = Interlocked.Increment(ref _searchGeneration);
            _searchCancellationTokenSource?.Cancel();
            _searchCancellationTokenSource?.Dispose();
            CancellationTokenSource searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _searchCancellationTokenSource = searchCancellation;

            ClearResults();
            IsLoading = true;
            HasError = false;

            _installedPackagesTask ??= _service.PrepareDataAsync();

            try
            {
                List<AppFetchService.StoreProductListDto> list = await _service.SearchProductsAsync(
                    searchTerm,
                    searchCancellation.Token);
                searchCancellation.Token.ThrowIfCancellationRequested();

                try
                {
                    await _installedPackagesTask;
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[AppFetch] Unable to load installed-package state.");
                }

                if (searchGeneration != Volatile.Read(ref _searchGeneration))
                {
                    return;
                }

                foreach (AppFetchService.StoreProductListDto result in list.Take(MaximumStoreSearchResults))
                {
                    AppFetchItemViewModel item = new(_service, result);
                    item.OperationFailed += Item_OperationFailed;
                    Results.Add(item);
                    _ = item.RefineInstalledStateAsync();
                }
            }
            catch (OperationCanceledException) when (searchCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Search failed for term \"{SearchTerm}\".", searchTerm);
                ErrorMessage = "Network request failed. Ensure you have a stable internet connection.";
                HasError = true;
            }
            finally
            {
                if (searchGeneration == Volatile.Read(ref _searchGeneration))
                {
                    IsLoading = false;
                    if (ReferenceEquals(_searchCancellationTokenSource, searchCancellation))
                    {
                        _searchCancellationTokenSource = null;
                    }
                }

                searchCancellation.Dispose();
            }
        }

        public void CancelPendingSearch()
        {
            Interlocked.Increment(ref _searchGeneration);
            _searchCancellationTokenSource?.Cancel();
            ClearResults();
            IsLoading = false;
        }

        private void ClearResults()
        {
            foreach (AppFetchItemViewModel item in Results)
            {
                item.OperationFailed -= Item_OperationFailed;
            }

            Results.Clear();
        }

        private void Item_OperationFailed(object? sender, string message)
        {
            ErrorMessage = message;
            HasError = true;
        }
    }
}
