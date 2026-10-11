#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public partial class InstalledAppItemViewModel : ObservableObject
    {
        private static readonly BitmapImage DefaultIcon =
            new(new Uri("ms-appx:///assets/Icons/Uninstall.png"));

        public string DisplayName { get; }
        public string? DisplayVersion { get; }
        public string? Publisher { get; }
        public bool CanUninstall { get; }
        public string? DisplayIcon { get; }
        public string? InstallLocation { get; }

        [ObservableProperty]
        public partial bool IsUninstalling { get; set; }

        [ObservableProperty]
        public partial Microsoft.UI.Xaml.Media.ImageSource Icon { get; set; } = DefaultIcon;

        public string DetailsText
        {
            get
            {
                List<string> parts = new();
                if (!string.IsNullOrWhiteSpace(DisplayVersion))
                {
                    parts.Add(DisplayVersion);
                }

                if (!string.IsNullOrWhiteSpace(Publisher))
                {
                    parts.Add(Publisher);
                }

                return parts.Count == 0 ? "Version unknown" : string.Join("  •  ", parts);
            }
        }

        public InstalledAppItemViewModel(InstalledApplicationInfo info)
        {
            DisplayName = info.DisplayName;
            DisplayVersion = info.DisplayVersion;
            Publisher = info.Publisher;
            DisplayIcon = info.DisplayIcon;
            InstallLocation = info.InstallLocation;
            CanUninstall = !string.IsNullOrWhiteSpace(info.UninstallString)
                || !string.IsNullOrWhiteSpace(info.QuietUninstallString);
        }

        public void SetIcon(Microsoft.UI.Xaml.Media.ImageSource icon) => Icon = icon;
    }

    public partial class UninstallerPageViewModel : ObservableObject
    {
        private readonly WingetInstallerService _wingetInstallerService;
        private readonly List<InstalledAppItemViewModel> _allApps = new();

        [ObservableProperty]
        public partial bool IsScanning { get; set; }

        [ObservableProperty]
        public partial bool HasScanned { get; set; }

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SearchText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CountText { get; set; } = string.Empty;

        public ObservableCollection<InstalledAppItemViewModel> FilteredApps { get; } = new();

        public IReadOnlyList<InstalledAppItemViewModel> AllApps => _allApps;

        public UninstallerPageViewModel(WingetInstallerService wingetInstallerService)
        {
            _wingetInstallerService = wingetInstallerService;
        }

        partial void OnSearchTextChanged(string value) => _ = ApplyFilterDebouncedAsync(value);

        public async Task ScanAsync(CancellationToken cancellationToken = default)
        {
            IsScanning = true;
            HasError = false;
            StatusMessage = string.Empty;
            try
            {
                IReadOnlyList<InstalledApplicationInfo> applications =
                    await _wingetInstallerService.GetInstalledApplicationsAsync(cancellationToken);

                _allApps.Clear();
                foreach (InstalledApplicationInfo application in applications)
                {
                    _allApps.Add(new InstalledAppItemViewModel(application));
                }

                HasScanned = true;
                ApplyFilter();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Uninstaller] Scanning installed apps failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsScanning = false;
            }
        }

        public Task<WingetInstallResult> UninstallAsync(
            InstalledAppItemViewModel item,
            CancellationToken cancellationToken = default) =>
            _wingetInstallerService.UninstallByDisplayNameAsync(item.DisplayName, cancellationToken);

        public void RemoveApp(InstalledAppItemViewModel item)
        {
            _allApps.Remove(item);
            FilteredApps.Remove(item);
            CountText = $"{_allApps.Count} apps";
        }

        private CancellationTokenSource? _filterDebounce;

        /// <summary>
        /// Rebuilding 100+ heavy rows per keystroke stutters typing; wait for a pause.
        /// Immediate callers (scan, remove) still use <see cref="ApplyFilter"/> directly.
        /// </summary>
        private async Task ApplyFilterDebouncedAsync(string value)
        {
            _filterDebounce?.Cancel();
            _filterDebounce?.Dispose();
            _filterDebounce = new CancellationTokenSource();
            try
            {
                await Task.Delay(250, _filterDebounce.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!string.Equals(value, SearchText, StringComparison.Ordinal))
            {
                return;
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredApps.Clear();
            IEnumerable<InstalledAppItemViewModel> matches = string.IsNullOrWhiteSpace(SearchText)
                ? _allApps
                : _allApps.Where(application =>
                    application.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (application.Publisher?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true));

            foreach (InstalledAppItemViewModel match in matches)
            {
                FilteredApps.Add(match);
            }

            CountText = HasScanned ? $"{_allApps.Count} apps" : string.Empty;
        }
    }
}
