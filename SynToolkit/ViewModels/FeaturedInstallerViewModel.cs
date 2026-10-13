#nullable enable

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public enum InstallerAvailabilityState
    {
        Checking,
        NotInstalled,
        Installed,
        UpdateAvailable,
        ManualSetup,
        Unavailable
    }

    /// <summary>
    /// A selectable app in the curated installer catalog.
    /// </summary>
    public partial class FeaturedInstallerViewModel : ObservableObject
    {
        public FeaturedInstallerViewModel(
            string name,
            string category,
            string description,
            string iconSource,
            string downloadUrl,
            string packageIdentifier,
            IReadOnlyList<string> installedDisplayNamePrefixes,
            bool isEssential = false,
            string? silentArgumentsOverride = null,
            bool isManualOnly = false,
            string packageSource = "winget")
        {
            Name = name;
            Category = category;
            Description = description;
            IconSource = iconSource;
            DownloadUri = new Uri(downloadUrl);
            PackageIdentifier = packageIdentifier;
            InstalledDisplayNamePrefixes = installedDisplayNamePrefixes;
            IsEssential = isEssential;
            SilentArgumentsOverride = silentArgumentsOverride;
            IsManualOnly = isManualOnly;
            PackageSource = packageSource;
            IsPortableApp = false;
            PortableAppDefinition = null;
            _availabilityState = isManualOnly
                ? InstallerAvailabilityState.ManualSetup
                : InstallerAvailabilityState.Checking;
        }

        /// <summary>
        /// Creates a portable app entry that downloads from GitHub releases.
        /// </summary>
        public static FeaturedInstallerViewModel CreatePortableApp(
            string name,
            string category,
            string description,
            string iconSource,
            string gitHubOwner,
            string gitHubRepo,
            string assetName,
            string executableName,
            bool isEssential = false,
            bool requiresElevation = false,
            string? expectedSha256 = null,
            string? warningMessage = null)
        {
            PortableAppDefinition definition = new(
                name,
                gitHubOwner,
                gitHubRepo,
                assetName,
                executableName,
                expectedSha256,
                requiresElevation);

            return new FeaturedInstallerViewModel(
                name,
                category,
                description,
                iconSource,
                $"https://github.com/{gitHubOwner}/{gitHubRepo}/releases",
                $"portable.{gitHubOwner}.{gitHubRepo}",
                Array.Empty<string>(),
                isEssential,
                null,
                false,
                "portable")
            {
                IsPortableApp = true,
                PortableAppDefinition = definition,
                WarningMessage = warningMessage
            };
        }

        public string Name { get; }

        public string Category { get; }

        public string Description { get; }

        public string IconSource { get; }

        public Uri DownloadUri { get; }

        public string PackageIdentifier { get; }

        public IReadOnlyList<string> InstalledDisplayNamePrefixes { get; }

        public bool IsEssential { get; }

        public string? SilentArgumentsOverride { get; }

        public bool IsManualOnly { get; }

        public string PackageSource { get; }

        /// <summary>
        /// Whether this is a portable app downloaded from GitHub releases.
        /// </summary>
        public bool IsPortableApp { get; private init; }

        /// <summary>
        /// Definition for portable app downloads. Only set when IsPortableApp is true.
        /// </summary>
        public PortableAppDefinition? PortableAppDefinition { get; private init; }

        /// <summary>
        /// Warning message to display for this app (e.g., unsigned executable warning).
        /// </summary>
        public string? WarningMessage { get; init; }

        public string PublisherLinkText => IsManualOnly ? "Open official setup" : IsPortableApp ? "GitHub releases" : "Website";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(QueueSelectionText))]
        public partial bool IsSelected { get; set; }

        private InstallerAvailabilityState _availabilityState = InstallerAvailabilityState.Checking;
        private string? _installedVersion;
        private string? _availableVersion;
        private bool _isUpdateCheckComplete = true;

        public InstallerAvailabilityState AvailabilityState
        {
            get => _availabilityState;
            private set
            {
                if (!SetProperty(ref _availabilityState, value))
                {
                    return;
                }

                if (!CanSelect)
                {
                    IsSelected = false;
                }

                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusDetail));
                OnPropertyChanged(nameof(StatusGlyph));
                OnPropertyChanged(nameof(IsCheckingStatus));
                OnPropertyChanged(nameof(CanSelect));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(CanManageDesktopShortcut));
                OnPropertyChanged(nameof(IsUninstallButtonEnabled));
                OnPropertyChanged(nameof(PrimaryActionText));
            }
        }

        public string? InstalledVersion
        {
            get => _installedVersion;
            private set
            {
                if (SetProperty(ref _installedVersion, value))
                {
                    OnPropertyChanged(nameof(StatusDetail));
                }
            }
        }

        public string? AvailableVersion
        {
            get => _availableVersion;
            private set
            {
                if (SetProperty(ref _availableVersion, value))
                {
                    OnPropertyChanged(nameof(StatusDetail));
                }
            }
        }

        public string StatusText => AvailabilityState switch
        {
            InstallerAvailabilityState.Checking => "Checking...",
            InstallerAvailabilityState.NotInstalled => "Not installed",
            InstallerAvailabilityState.UpdateAvailable => "Update available",
            InstallerAvailabilityState.ManualSetup => "Manual setup",
            InstallerAvailabilityState.Unavailable => "Status unavailable",
            _ => "Installed"
        };

        public string StatusDetail => AvailabilityState switch
        {
            InstallerAvailabilityState.Checking => "Reading app status",
            InstallerAvailabilityState.NotInstalled => "Ready to install",
            InstallerAvailabilityState.ManualSetup => "Review the official instructions",
            InstallerAvailabilityState.UpdateAvailable when !string.IsNullOrWhiteSpace(InstalledVersion) &&
                !string.IsNullOrWhiteSpace(AvailableVersion) => $"{InstalledVersion} → {AvailableVersion}",
            InstallerAvailabilityState.Installed when !string.IsNullOrWhiteSpace(InstalledVersion) =>
                IsUpdateCheckComplete
                    ? $"Version {InstalledVersion}"
                    : $"Version {InstalledVersion} • update check unavailable",
            InstallerAvailabilityState.UpdateAvailable => "A newer version is ready",
            InstallerAvailabilityState.Unavailable => "Refresh to try again",
            _ => "Detected on this PC"
        };

        public string StatusGlyph => AvailabilityState switch
        {
            InstallerAvailabilityState.NotInstalled => "\uE896",
            InstallerAvailabilityState.UpdateAvailable => "\uE895",
            InstallerAvailabilityState.Installed => "\uE73E",
            InstallerAvailabilityState.ManualSetup => "\uE8A7",
            InstallerAvailabilityState.Unavailable => "\uE783",
            _ => string.Empty
        };

        public bool IsCheckingStatus => AvailabilityState == InstallerAvailabilityState.Checking;

        public bool IsUpdateCheckComplete
        {
            get => _isUpdateCheckComplete;
            private set
            {
                if (SetProperty(ref _isUpdateCheckComplete, value))
                {
                    OnPropertyChanged(nameof(StatusDetail));
                }
            }
        }

        public bool CanSelect => AvailabilityState is
            InstallerAvailabilityState.NotInstalled or InstallerAvailabilityState.UpdateAvailable;

        public bool CanUninstall => AvailabilityState is
            InstallerAvailabilityState.Installed or InstallerAvailabilityState.UpdateAvailable;

        public bool CanManageDesktopShortcut => IsPortableApp && CanUninstall;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DesktopShortcutActionText))]
        public partial bool HasDesktopShortcut { get; set; }

        public string DesktopShortcutActionText =>
            HasDesktopShortcut ? "Remove desktop shortcut" : "Desktop shortcut";

        private bool _isUninstalling;

        public bool IsUninstalling
        {
            get => _isUninstalling;
            set
            {
                if (SetProperty(ref _isUninstalling, value))
                {
                    OnPropertyChanged(nameof(IsUninstallButtonEnabled));
                    OnPropertyChanged(nameof(UninstallActionText));
                }
            }
        }

        public bool IsUninstallButtonEnabled => CanUninstall && !IsUninstalling;

        public string UninstallActionText => IsUninstalling ? "Uninstalling..." : "Uninstall";

        public string PrimaryActionText => AvailabilityState == InstallerAvailabilityState.UpdateAvailable
            ? "Update"
            : "Install";

        public string QueueSelectionText => IsSelected ? "Queued" : "Queue";

        public void ApplyAvailabilityState(
            InstallerAvailabilityState state,
            string? installedVersion = null,
            string? availableVersion = null,
            bool isUpdateCheckComplete = true)
        {
            InstalledVersion = installedVersion;
            AvailableVersion = availableVersion;
            IsUpdateCheckComplete = isUpdateCheckComplete;
            AvailabilityState = state;
        }
    }
}
