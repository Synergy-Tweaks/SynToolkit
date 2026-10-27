#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Models.Startup;
using SynToolkit.Services.Startup;
using Windows.ApplicationModel.DataTransfer;

namespace SynToolkit.ViewModels
{
    public partial class StartupPageViewModel : ObservableObject
    {
        private readonly StartupScannerService _scanner = new();
        private readonly List<StartupEntry> _allEntries = new();
        private CancellationTokenSource _operations = new();
        private bool _hasScanned;

        public StartupPageViewModel()
        {
            IsAdministrator = _scanner.IsAdministrator;
            Categories = new[]
            {
                new StartupCategoryOption("All", Text("Startup_All")),
                new StartupCategoryOption("Registry", Text("Startup_Registry")),
                new StartupCategoryOption("StartupFolder", Text("Startup_StartupFolder")),
                new StartupCategoryOption("ScheduledTask", Text("Startup_ScheduledTask")),
                new StartupCategoryOption("Service", Text("Startup_Service"))
            };
            SelectedCategory = Categories[0];
            StatusText = Text("Startup_Ready");
        }

        public ObservableCollection<StartupEntry> Entries { get; } = new();

        public IReadOnlyList<StartupCategoryOption> Categories { get; }

        public Func<StartupEntry, Task<bool>>? ConfirmToggle { get; set; }

        public bool IsAdministrator { get; }

        public bool ShowAdminNotice => !IsAdministrator;

        public bool ShowUnknownWarning => UnknownCount > 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
        [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
        [NotifyPropertyChangedFor(nameof(ShowEmpty))]
        public partial bool IsScanning { get; set; }

        [ObservableProperty]
        public partial bool IsStatusOpen { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = "";

        [ObservableProperty]
        public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

        [ObservableProperty]
        public partial string StatusText { get; set; } = "";

        [ObservableProperty]
        public partial string SearchText { get; set; } = "";

        [ObservableProperty]
        public partial StartupCategoryOption SelectedCategory { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HighRiskButtonText))]
        public partial bool HighRiskOnly { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
        [NotifyCanExecuteChangedFor(nameof(OpenLocationCommand))]
        [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
        [NotifyPropertyChangedFor(nameof(HasSelection))]
        [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
        [NotifyPropertyChangedFor(nameof(ToggleBlockedReason))]
        [NotifyPropertyChangedFor(nameof(ShowToggleBlockedReason))]
        public partial StartupEntry? SelectedEntry { get; set; }

        [ObservableProperty]
        public partial int TotalCount { get; set; }

        [ObservableProperty]
        public partial int HighRiskCount { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowUnknownWarning))]
        public partial int UnknownCount { get; set; }

        [ObservableProperty]
        public partial int SafeCount { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowEmpty))]
        public partial int VisibleCount { get; set; }

        public bool HasSelection => SelectedEntry is not null;

        public bool ShowEmpty => !IsScanning && VisibleCount == 0;

        public string ToggleButtonText => SelectedEntry is { IsEnabled: true }
            ? Text("Startup_Disable")
            : Text("Startup_Enable");

        public string HighRiskButtonText => HighRiskOnly
            ? Text("Startup_ShowAll")
            : Text("Startup_ShowHighRisk");

        public string ToggleBlockedReason =>
            SelectedEntry is { RequiresAdministrator: true } && !IsAdministrator
                ? Text("Startup_ToggleNeedsAdmin")
                : "";

        public bool ShowToggleBlockedReason => !string.IsNullOrWhiteSpace(ToggleBlockedReason);

        public void CancelPending()
        {
            _operations.Cancel();
        }

        public Task EnsureScannedAsync()
        {
            if (_hasScanned || IsScanning)
            {
                return Task.CompletedTask;
            }

            return ScanAsync();
        }

        [RelayCommand(CanExecute = nameof(CanScan))]
        private Task ScanAsync()
        {
            ReplaceOperations();
            return ScanCoreAsync(_operations.Token);
        }

        [RelayCommand]
        private void FilterHighRisk()
        {
            if (HighRiskOnly)
            {
                HighRiskOnly = false;
                ApplyFilter();
                StatusText = string.Format(CultureInfo.CurrentCulture, Text("Startup_ShowingAll"), Entries.Count);
                return;
            }

            SearchText = "";
            SelectedCategory = Categories[0];
            HighRiskOnly = true;
            ApplyFilter();
            StatusText = string.Format(CultureInfo.CurrentCulture, Text("Startup_ShowingElevated"), Entries.Count);
        }

        [RelayCommand(CanExecute = nameof(CanToggle))]
        private async Task ToggleAsync()
        {
            if (SelectedEntry is not StartupEntry entry)
            {
                return;
            }

            if (ConfirmToggle is not null && !await ConfirmToggle(entry))
            {
                return;
            }

            IsScanning = true;
            try
            {
                StartupToggleResult result = await Task.Run(() => _scanner.ToggleEntry(entry));
                if (result.NeedsAdministrator)
                {
                    ShowStatus(Text("Startup_ToggleNeedsAdmin"), InfoBarSeverity.Warning);
                    return;
                }

                if (!result.Succeeded)
                {
                    ShowStatus(string.Format(CultureInfo.CurrentCulture, Text("Startup_ToggleFailed"), entry.Name), InfoBarSeverity.Error);
                    return;
                }

                entry.IsEnabled = result.IsEnabled;
                entry.Location = result.Location;
                entry.Command = result.Command;
                OnPropertyChanged(nameof(ToggleButtonText));
                ShowStatus(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Text("Startup_Toggled"),
                        entry.Name,
                        result.IsEnabled ? Text("Startup_Enabled") : Text("Startup_Disabled")),
                    InfoBarSeverity.Success);
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
        private void OpenLocation()
        {
            if (SelectedEntry is null)
            {
                return;
            }

            string path = SelectedEntry.FilePath;
            if (!File.Exists(path))
            {
                ShowStatus(Text("Startup_FileMissing"), InfoBarSeverity.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }

        [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
        private void CopyPath()
        {
            if (SelectedEntry is null || string.IsNullOrWhiteSpace(SelectedEntry.FilePath))
            {
                ShowStatus(Text("Startup_NoPath"), InfoBarSeverity.Warning);
                return;
            }

            DataPackage package = new();
            package.SetText(SelectedEntry.FilePath);
            Clipboard.SetContent(package);
            ShowStatus(Text("Startup_PathCopied"), InfoBarSeverity.Success);
        }

        private bool CanScan() => !IsScanning;

        private bool CanToggle() =>
            SelectedEntry is not null &&
            !IsScanning &&
            (IsAdministrator || !SelectedEntry.RequiresAdministrator);

        private bool HasSelectedEntry() => SelectedEntry is not null;

        partial void OnSearchTextChanged(string value)
        {
            _ = value;
            if (HighRiskOnly)
            {
                HighRiskOnly = false;
            }

            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(StartupCategoryOption value)
        {
            _ = value;
            if (HighRiskOnly)
            {
                HighRiskOnly = false;
            }

            ApplyFilter();
        }

        private async Task ScanCoreAsync(CancellationToken cancellationToken)
        {
            IsScanning = true;
            StatusText = Text("Startup_Scanning");
            try
            {
                StartupScanResult result = await Task.Run(() => _scanner.ScanAll(cancellationToken), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _allEntries.Clear();
                _allEntries.AddRange(result.Entries.OrderByDescending(entry => (int)entry.Risk));
                _hasScanned = true;
                ApplyFilter();
                UpdateStats();
                string status = string.Format(CultureInfo.CurrentCulture, Text("Startup_ScanComplete"), _allEntries.Count);
                if (result.Failures.Count > 0)
                {
                    status += string.Format(CultureInfo.CurrentCulture, Text("Startup_Partial"), string.Join(", ", result.Failures));
                }

                StatusText = status;
                ShowStatus(status, result.Failures.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
            catch (OperationCanceledException)
            {
                StatusText = Text("Startup_ScanCancelled");
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Startup] Scan failed.");
                StatusText = Text("Startup_ScanFailed");
                ShowStatus(Text("Startup_ScanFailed"), InfoBarSeverity.Error);
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<StartupEntry> query = _allEntries;
            if (HighRiskOnly)
            {
                query = query.Where(entry => entry.Risk == RiskLevel.High);
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText;
                query = query.Where(entry =>
                    entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    entry.FilePath.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedCategory.Id != "All")
            {
                query = query.Where(entry => CategoryId(entry.Category) == SelectedCategory.Id);
            }

            StartupEntry? selected = SelectedEntry;
            Entries.Clear();
            foreach (StartupEntry entry in query)
            {
                Entries.Add(entry);
            }

            VisibleCount = Entries.Count;
            if (selected is not null && Entries.Contains(selected))
            {
                SelectedEntry = selected;
            }
        }

        private void UpdateStats()
        {
            TotalCount = _allEntries.Count;
            HighRiskCount = _allEntries.Count(entry => entry.Risk == RiskLevel.High);
            UnknownCount = _allEntries.Count(entry => entry.Risk == RiskLevel.Unknown);
            SafeCount = _allEntries.Count(entry => entry.Risk == RiskLevel.Safe);
        }

        private void ReplaceOperations()
        {
            _operations.Cancel();
            _operations.Dispose();
            _operations = new CancellationTokenSource();
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusMessage = message;
            StatusSeverity = severity;
            IsStatusOpen = !string.IsNullOrWhiteSpace(message);
        }

        private static string CategoryId(StartupCategory category) => category switch
        {
            StartupCategory.Registry => "Registry",
            StartupCategory.StartupFolder => "StartupFolder",
            StartupCategory.ScheduledTask => "ScheduledTask",
            StartupCategory.Service => "Service",
            _ => "All"
        };

        private static string Text(string key) => App.GetValueFromItemList(key);
    }

    public sealed record StartupCategoryOption(string Id, string Label);
}
