#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SynToolkit.Services.Uninstaller;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.ViewModels;

public enum UninstallItemState
{
    Pending,
    Uninstalling,
    Removed,
    Failed,
    Skipped,
    Cancelled
}

public sealed record UninstallFailureDetail(string AppName, string Reason);

public sealed record UninstallBatchResult(
    int Removed,
    int Failed,
    int Skipped,
    int Cancelled,
    IReadOnlyList<UninstallFailureDetail> Failures);

public partial class InstalledAppItemViewModel : ObservableObject
{
    private static readonly BitmapImage DefaultIcon =
        new(new Uri("ms-appx:///assets/Icons/Uninstall.png"));

    public UninstallableAppInfo AppInfo { get; }
    public string DisplayName { get; }
    public string? DisplayVersion { get; }
    public string? Publisher { get; }
    public string? DisplayIcon { get; }
    public string? InstallLocation { get; }
    public bool CanUninstall => true;
    public bool IsRisky => AppInfo.IsRisky;
    public string KindLabel => AppInfo.Kind == InstalledAppKind.Appx ? "Store/Appx" : "Win32";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsProcessing { get; set; }

    [ObservableProperty]
    public partial UninstallItemState Status { get; set; } = UninstallItemState.Pending;

    [ObservableProperty]
    public partial string StatusDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource Icon { get; set; } = DefaultIcon;

    [ObservableProperty]
    public partial bool HasCustomIcon { get; set; }

    public bool ShowProgress => Status == UninstallItemState.Uninstalling;

    public bool ShowStatusText =>
        Status != UninstallItemState.Pending && Status != UninstallItemState.Uninstalling;

    public string StatusText => Status switch
    {
        UninstallItemState.Removed => "Removed",
        UninstallItemState.Failed => string.IsNullOrWhiteSpace(StatusDetail)
            ? "Failed"
            : $"Failed: {StatusDetail}",
        UninstallItemState.Skipped => string.IsNullOrWhiteSpace(StatusDetail)
            ? "Skipped"
            : $"Skipped: {StatusDetail}",
        UninstallItemState.Cancelled => string.IsNullOrWhiteSpace(StatusDetail)
            ? "Cancelled"
            : $"Cancelled: {StatusDetail}",
        _ => string.Empty
    };

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

            parts.Add(KindLabel);
            return string.Join("  |  ", parts);
        }
    }

    public InstalledAppItemViewModel(UninstallableAppInfo appInfo)
    {
        AppInfo = appInfo;
        DisplayName = appInfo.DisplayName;
        DisplayVersion = appInfo.DisplayVersion;
        Publisher = appInfo.Publisher;
        DisplayIcon = appInfo.DisplayIcon;
        InstallLocation = appInfo.InstallLocation;
    }

    partial void OnStatusChanged(UninstallItemState value)
    {
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(ShowStatusText));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnStatusDetailChanged(string value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    public void SetIcon(ImageSource icon)
    {
        Icon = icon;
        HasCustomIcon = true;
    }
}

/// <summary>
/// ObservableCollection that can swap its whole contents with a single Reset.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        List<T> snapshot = items.ToList();
        CheckReentrancy();
        Items.Clear();
        foreach (T item in snapshot)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public partial class UninstallerPageViewModel : ObservableObject
{
    private readonly IUninstallerWorkflowService _workflowService;
    private readonly List<InstalledAppItemViewModel> _allApps = new();
    private CancellationTokenSource? _filterDebounce;
    private bool _suppressSelectAllStateSync;

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

    [ObservableProperty]
    public partial string SelectedCountText { get; set; } = "0 selected";

    [ObservableProperty]
    public partial bool IsUninstallQueueRunning { get; set; }

    [ObservableProperty]
    public partial bool IsCancelRequested { get; set; }

    [ObservableProperty]
    public partial string QueueStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double QueueProgressValue { get; set; }

    [ObservableProperty]
    public partial double QueueProgressMaximum { get; set; } = 1;

    [ObservableProperty]
    public partial bool HasQueueSummary { get; set; }

    [ObservableProperty]
    public partial bool QueueSummaryHasFailures { get; set; }

    [ObservableProperty]
    public partial string QueueSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool? SelectAllFilteredState { get; set; } = false;

    private bool _isBatchUpdatingSelection;

    public BulkObservableCollection<InstalledAppItemViewModel> FilteredApps { get; } = new();
    public IReadOnlyList<InstalledAppItemViewModel> AllApps => _allApps;
    public bool IsProgressVisible => IsUninstallQueueRunning;
    public bool CanEditSelection => !IsUninstallQueueRunning && !IsScanning;
    public bool CanUninstallSelected => SelectedCount > 0 && !IsUninstallQueueRunning;
    public int SelectedCount => _allApps.Count(app => app.IsSelected);

    public UninstallerPageViewModel(IUninstallerWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    partial void OnSearchTextChanged(string value) => _ = ApplyFilterDebouncedAsync(value);

    partial void OnIsScanningChanged(bool value) => RefreshComputedState();

    partial void OnIsUninstallQueueRunningChanged(bool value) => RefreshComputedState();

    partial void OnSelectAllFilteredStateChanged(bool? value)
    {
        if (_suppressSelectAllStateSync || !value.HasValue || IsUninstallQueueRunning)
        {
            return;
        }

        // Each IsSelected change used to recount every app and re-sync the
        // header checkbox, which made Select all O(n^2). Batch it instead.
        _isBatchUpdatingSelection = true;
        try
        {
            foreach (InstalledAppItemViewModel app in FilteredApps)
            {
                app.IsSelected = value.Value;
            }
        }
        finally
        {
            _isBatchUpdatingSelection = false;
        }

        RefreshSelectionState();
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        IsScanning = true;
        HasError = false;
        StatusMessage = string.Empty;
        HasQueueSummary = false;
        QueueSummaryText = string.Empty;
        try
        {
            IReadOnlyList<UninstallableAppInfo> applications =
                await _workflowService.GetInstalledAppsAsync(cancellationToken);

            foreach (InstalledAppItemViewModel app in _allApps)
            {
                app.PropertyChanged -= App_PropertyChanged;
            }

            _allApps.Clear();
            foreach (UninstallableAppInfo application in applications)
            {
                var vm = new InstalledAppItemViewModel(application);
                vm.PropertyChanged += App_PropertyChanged;
                _allApps.Add(vm);
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

    public IReadOnlyList<InstalledAppItemViewModel> GetSelectedApps() =>
        _allApps.Where(app => app.IsSelected).ToArray();

    public void RequestCancelQueue()
    {
        if (IsUninstallQueueRunning)
        {
            IsCancelRequested = true;
        }
    }

    public async Task<UninstallBatchResult> RunUninstallQueueAsync(
        IReadOnlyList<InstalledAppItemViewModel> selectedApps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedApps);
        if (selectedApps.Count == 0)
        {
            return new UninstallBatchResult(0, 0, 0, 0, Array.Empty<UninstallFailureDetail>());
        }

        if (IsUninstallQueueRunning)
        {
            throw new InvalidOperationException("Uninstall queue is already running.");
        }

        List<InstalledAppItemViewModel> queue = selectedApps.ToList();
        foreach (InstalledAppItemViewModel app in queue)
        {
            app.Status = UninstallItemState.Pending;
            app.StatusDetail = string.Empty;
            app.IsProcessing = false;
        }

        IsUninstallQueueRunning = true;
        IsCancelRequested = false;
        HasQueueSummary = false;
        QueueStatusText = string.Empty;
        QueueProgressMaximum = Math.Max(1, queue.Count);
        QueueProgressValue = 0;

        int removed = 0;
        int failed = 0;
        int skipped = 0;
        int cancelled = 0;
        List<UninstallFailureDetail> failures = new();
        List<InstalledAppItemViewModel> successfullyRemoved = new();

        try
        {
            QueueRunResult<InstalledAppItemViewModel> queueResult = await UninstallQueueEngine.RunAsync(
                queue,
                async (app, token) =>
                {
                    app.IsProcessing = true;
                    app.Status = UninstallItemState.Uninstalling;
                    try
                    {
                        UninstallExecutionResult result = await _workflowService.UninstallAsync(app.AppInfo, token);
                        return new QueueStepOutcome(result.Succeeded, BuildFailureReason(result));
                    }
                    finally
                    {
                        app.IsProcessing = false;
                    }
                },
                () => IsCancelRequested,
                onStepStarting: (index, total, app) =>
                {
                    QueueStatusText = $"Uninstalling {index + 1} of {total}: {app.DisplayName}";
                },
                cancellationToken);

            removed = queueResult.Removed;
            failed = queueResult.Failed;
            skipped = queueResult.Skipped;
            cancelled = queueResult.Cancelled;

            _isBatchUpdatingSelection = true;
            foreach (QueueStepResult<InstalledAppItemViewModel> step in queueResult.Steps)
            {
                QueueProgressValue = Math.Min(QueueProgressMaximum, QueueProgressValue + 1);
                InstalledAppItemViewModel app = step.Item;
                switch (step.State)
                {
                    case QueueStepState.Removed:
                        app.Status = UninstallItemState.Removed;
                        app.StatusDetail = string.Empty;
                        app.IsSelected = false;
                        successfullyRemoved.Add(app);
                        break;
                    case QueueStepState.Failed:
                        string reason = string.IsNullOrWhiteSpace(step.Message) ? "Unknown error." : step.Message;
                        app.Status = UninstallItemState.Failed;
                        app.StatusDetail = reason;
                        app.IsSelected = true;
                        failures.Add(new UninstallFailureDetail(app.DisplayName, reason));
                        break;
                    case QueueStepState.Cancelled:
                        app.Status = UninstallItemState.Cancelled;
                        app.StatusDetail = step.Message;
                        app.IsSelected = true;
                        break;
                    default:
                        app.Status = UninstallItemState.Skipped;
                        app.StatusDetail = step.Message;
                        app.IsSelected = true;
                        break;
                }
            }
        }
        finally
        {
            _isBatchUpdatingSelection = false;
            foreach (InstalledAppItemViewModel removedApp in successfullyRemoved)
            {
                removedApp.PropertyChanged -= App_PropertyChanged;
                _allApps.Remove(removedApp);
            }

            if (successfullyRemoved.Count > 0)
            {
                HashSet<InstalledAppItemViewModel> gone = new(successfullyRemoved);
                FilteredApps.ReplaceAll(FilteredApps.Where(app => !gone.Contains(app)).ToList());
                CountText = $"{_allApps.Count} apps";
            }

            QueueStatusText = IsCancelRequested
                ? "Uninstall queue cancelled."
                : "Uninstall queue finished.";
            QueueSummaryText = $"{removed} removed, {failed} failed, {skipped} skipped";
            QueueSummaryHasFailures = failed > 0;
            HasQueueSummary = true;
            IsUninstallQueueRunning = false;
            IsCancelRequested = false;
            RefreshSelectionState();
            RefreshComputedState();
        }

        return new UninstallBatchResult(removed, failed, skipped, cancelled, failures);
    }

    private static string BuildFailureReason(UninstallExecutionResult result)
    {
        string output = string.Join(
            " ",
            new[] { result.Message, result.StdErr, result.StdOut }
            .Where(text => !string.IsNullOrWhiteSpace(text)));
        return output.Length > 800 ? output[..800] + "..." : output;
    }

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledAppItemViewModel.IsSelected) && !_isBatchUpdatingSelection)
        {
            RefreshSelectionState();
        }
    }

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

    public void ToggleSelection(InstalledAppItemViewModel app)
    {
        if (!CanEditSelection)
        {
            return;
        }

        app.IsSelected = !app.IsSelected;
    }

    private void ApplyFilter()
    {
        string search = SearchText?.Trim() ?? string.Empty;
        IEnumerable<InstalledAppItemViewModel> matches = search.Length == 0
            ? _allApps
            : _allApps.Where(application =>
                application.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (application.Publisher?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));

        // One Reset notification instead of Clear + N Adds: the ItemsRepeater
        // used to re-layout once per app on every scan and every keystroke.
        FilteredApps.ReplaceAll(matches);

        CountText = HasScanned ? $"{_allApps.Count} apps" : string.Empty;
        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        SelectedCountText = $"{SelectedCount} selected";

        int visibleCount = FilteredApps.Count;
        int visibleSelected = FilteredApps.Count(app => app.IsSelected);

        _suppressSelectAllStateSync = true;
        SelectAllFilteredState = visibleCount == 0
            ? false
            : visibleSelected == 0
                ? false
                : visibleSelected == visibleCount
                    ? true
                    : null;
        _suppressSelectAllStateSync = false;

        RefreshComputedState();
    }

    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanUninstallSelected));
        OnPropertyChanged(nameof(CanEditSelection));
        OnPropertyChanged(nameof(IsProgressVisible));
    }
}
