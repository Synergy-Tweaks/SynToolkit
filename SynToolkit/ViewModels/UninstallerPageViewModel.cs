#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public partial class InstalledAppItemViewModel : ObservableObject
    {
        public string DisplayName { get; }
        public string? DisplayVersion { get; }
        public string? Publisher { get; }
        public bool CanUninstall { get; }

        [ObservableProperty]
        public partial bool IsUninstalling { get; set; }

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
            CanUninstall = !string.IsNullOrWhiteSpace(info.UninstallString)
                || !string.IsNullOrWhiteSpace(info.QuietUninstallString);
        }
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

        public UninstallerPageViewModel(WingetInstallerService wingetInstallerService)
        {
            _wingetInstallerService = wingetInstallerService;
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

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
