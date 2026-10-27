#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Models.Display;
using SynToolkit.Services.Display;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace SynToolkit.ViewModels;

public sealed partial class DisplayPageViewModel : ObservableObject
{
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private int _modeVersion;
    private int _snapshotEpoch;
    private bool _hasLoaded;
    private bool _suppressUi;
    private bool _makePrimaryPending;
    private bool _isSettling;
    private int _tickCycleId;
    private bool _countdownLowAnnounced;
    private bool _temporaryApplied;
    private DisplayNoticeKind _lastStatusKind = DisplayNoticeKind.Neutral;
    private DisplaySnapshot? _appliedReadBack;
    private DisplaySnapshot? _countdownCanvas;
    private DisplayCountdownGuardOutcome _countdownGuardOutcome = DisplayCountdownGuardOutcome.Matches;
    private string? _modesSignature;
    private readonly DisplayChainPolicy _chain = new();
    private IReadOnlyList<DisplayGraphicsMode> _modes = Array.Empty<DisplayGraphicsMode>();
    private DispatcherQueueTimer? _countdownTimer;
    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueue? _dispatcher;
    private bool _pollInFlight;
    private int _busyDepth;
    private int _busyOverlayVersion;
    private int _applyFlagVersion;
    private int _scaleVersion;
    private bool _scaleRevertArmed;
    private DisplaySnapshot? _lastValidCanvas;
    private DisplayNative.DisplayConfigPathSourceInfo _scaleSource;
    private int _scalePreviousStep;
    private int _scaleRequestedStep;
    private string _scaleMonitorKey = string.Empty;
    private DisplayHdrTarget? _hdrTarget;
    private int _hdrVersion;
    private bool _hdrRevertArmed;
    private bool _hdrPreviousEnabled;
    private bool _hdrRequestedEnabled;
    private string _hdrMonitorKey = string.Empty;
    private bool _arrangementOnlyCycle;
    private DisplayPickedMode? _refusedMode;
    private readonly DisplayScaleLearnedMap _scaleLearned = new();

    private void AcquireBusy()
    {
        if (_busyDepth == 0)
        {
            IsBusy = true;
            ScheduleBusyOverlayRaise();
        }
        _busyDepth++;
    }

    private void ReleaseBusy()
    {
        _busyDepth--;
        if (_busyDepth == 0)
            IsBusy = false;
        if (_busyDepth != 0)
            return;
        Interlocked.Increment(ref _busyOverlayVersion);
        SetBusyOverlayVisible(false);
    }

    private void ScheduleBusyOverlayRaise()
    {
        var scheduled = Interlocked.Increment(ref _busyOverlayVersion);
        var started = DateTimeOffset.UtcNow;
        _ = Task.Delay(TimeSpan.FromMilliseconds(DisplayChainPolicy.BusyOverlayDelayMs)).ContinueWith(_ =>
        {
            if (!DisplayChainPolicy.ShouldRaiseBusyOverlay(
                    scheduled,
                    Volatile.Read(ref _busyOverlayVersion),
                    Volatile.Read(ref _busyDepth),
                    TimeSpan.FromMilliseconds(DisplayChainPolicy.BusyOverlayDelayMs)))
                return;
            _dispatcher?.TryEnqueue(() =>
            {
                if (!DisplayChainPolicy.ShouldRaiseBusyOverlay(
                        scheduled,
                        _busyOverlayVersion,
                        _busyDepth,
                        DateTimeOffset.UtcNow - started))
                    return;
                IsBusyOverlayVisible = true;
            });
        }, TaskScheduler.Default);
    }

    private void SetBusyOverlayVisible(bool visible)
    {
        if (_dispatcher is not null)
        {
            if (_dispatcher.TryEnqueue(() => ApplyBusyOverlayVisible(visible)))
                return;
        }
        ApplyBusyOverlayVisible(visible);
    }

    private void ApplyBusyOverlayVisible(bool visible)
    {
        if (visible && _busyDepth <= 0)
            return;
        IsBusyOverlayVisible = visible;
    }

    private void SetArrangementOnlyCycle(bool value)
    {
        if (_arrangementOnlyCycle == value)
            return;

        _arrangementOnlyCycle = value;
        OnPropertyChanged(nameof(IsBarVisible));
    }

    private void SetApplyingFlag()
    {
        var version = ++_applyFlagVersion;
        var dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            IsApplying = true;
            return;
        }
        try
        {
            if (dispatcher.TryEnqueue(() =>
            {
                if (DisplayChainPolicy.ShouldSetPostedApplyingFlag(version, _applyFlagVersion))
                    IsApplying = true;
            }))
                return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        IsApplying = true;
    }

    public DisplayPageViewModel()
    {
        BusyText = "Detecting displays...";
        ErrorTitle = string.Empty;
        ErrorMessage = string.Empty;
        ErrorLog = string.Empty;
        StatusMessage = string.Empty;
        StatusSeverity = InfoBarSeverity.Informational;
        BarMessage = string.Empty;
        AnnouncerMessage = string.Empty;
        ScaleEmptyMessage = string.Empty;
        HdrStatusText = string.Empty;
    }

    public event EventHandler? CanvasInvalidated;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(KeepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStagedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardStagedCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(AreControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(DisabledControlsTip))]
    [NotifyPropertyChangedFor(nameof(MakePrimaryDisabledTip))]
    [NotifyPropertyChangedFor(nameof(KeepDisabledTip))]
    [NotifyPropertyChangedFor(nameof(IsMakePrimaryEnabled))]
    [NotifyPropertyChangedFor(nameof(IsStageActionsVisible))]
    [NotifyPropertyChangedFor(nameof(IsHdrToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(HdrToggleTip))]
    [NotifyPropertyChangedFor(nameof(AreModeControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(ModeControlsDisabledTip))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(DisabledControlsTip))]
    [NotifyPropertyChangedFor(nameof(MakePrimaryDisabledTip))]
    [NotifyPropertyChangedFor(nameof(KeepDisabledTip))]
    [NotifyPropertyChangedFor(nameof(IsBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsMakePrimaryEnabled))]
    [NotifyPropertyChangedFor(nameof(IsStageActionsVisible))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStagedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardStagedCommand))]
    [NotifyPropertyChangedFor(nameof(IsHdrToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(HdrToggleTip))]
    [NotifyPropertyChangedFor(nameof(AreModeControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(ModeControlsDisabledTip))]
    public partial bool IsApplying { get; set; }

    [ObservableProperty]
    public partial bool IsBusyOverlayVisible { get; set; }

    [ObservableProperty]
    public partial string BusyText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorTitle { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogPreview))]
    [NotifyCanExecuteChangedFor(nameof(CopyErrorLogCommand))]
    public partial string ErrorLog { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedMonitorName))]
    [NotifyPropertyChangedFor(nameof(CanMakePrimary))]
    [NotifyPropertyChangedFor(nameof(IsMakePrimaryEnabled))]
    [NotifyPropertyChangedFor(nameof(MakePrimaryDisabledTip))]
    [NotifyCanExecuteChangedFor(nameof(MakePrimaryCommand))]
    public partial DisplayMonitor? SelectedMonitor { get; set; }

    public string SelectedMonitorName => SelectedMonitor?.DisplayLabel ?? string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string BarMessage { get; set; }

    [ObservableProperty]
    public partial string AnnouncerMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRevertAvailable))]
    [NotifyPropertyChangedFor(nameof(AreControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(DisabledControlsTip))]
    [NotifyPropertyChangedFor(nameof(MakePrimaryDisabledTip))]
    [NotifyPropertyChangedFor(nameof(KeepDisabledTip))]
    [NotifyPropertyChangedFor(nameof(IsBarVisible))]
    [NotifyPropertyChangedFor(nameof(IsMakePrimaryEnabled))]
    [NotifyPropertyChangedFor(nameof(IsStageActionsVisible))]
    [NotifyCanExecuteChangedFor(nameof(KeepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyStagedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardStagedCommand))]
    [NotifyPropertyChangedFor(nameof(IsHdrToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(HdrToggleTip))]
    [NotifyPropertyChangedFor(nameof(AreModeControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(ModeControlsDisabledTip))]
    public partial bool IsCountdownActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRevertAvailable))]
    [NotifyPropertyChangedFor(nameof(KeepDisabledTip))]
    [NotifyPropertyChangedFor(nameof(IsBarVisible))]
    [NotifyCanExecuteChangedFor(nameof(RevertNowCommand))]
    [NotifyPropertyChangedFor(nameof(IsHdrToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(HdrToggleTip))]
    [NotifyPropertyChangedFor(nameof(AreModeControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(ModeControlsDisabledTip))]
    public partial bool HasRevertFailed { get; set; }

    public bool IsRevertAvailable => IsCountdownActive || HasRevertFailed;

    public bool IsSettling
    {
        get => _isSettling;
        private set
        {
            if (_isSettling == value)
                return;

            _isSettling = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AreControlsEnabled));
            OnPropertyChanged(nameof(DisabledControlsTip));
            OnPropertyChanged(nameof(MakePrimaryDisabledTip));
            OnPropertyChanged(nameof(KeepDisabledTip));
            OnPropertyChanged(nameof(IsBarVisible));
            OnPropertyChanged(nameof(IsMakePrimaryEnabled));
            OnPropertyChanged(nameof(IsStageActionsVisible));
            OnPropertyChanged(nameof(IsHdrToggleEnabled));
            OnPropertyChanged(nameof(HdrToggleTip));
            OnPropertyChanged(nameof(AreModeControlsEnabled));
            OnPropertyChanged(nameof(ModeControlsDisabledTip));
            LoadCommand.NotifyCanExecuteChanged();
            KeepCommand.NotifyCanExecuteChanged();
            RevertNowCommand.NotifyCanExecuteChanged();
            ApplyStagedCommand.NotifyCanExecuteChanged();
            DiscardStagedCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsBarVisible => DisplayChainPolicy.ShouldShowApplyBar(IsApplying, _arrangementOnlyCycle, IsCountdownActive, IsSettling, HasRevertFailed);

    public bool AreControlsEnabled => !IsApplying && !IsCountdownActive && !IsSettling && !IsBusy;

    public bool IsMakePrimaryEnabled => AreControlsEnabled && CanMakePrimary;

    public string? DisabledControlsTip => AreControlsEnabled
        ? null
        : DisplayChainPolicy.DisabledControlsMessage(IsBusy, IsApplying, IsCountdownActive, IsSettling);

    public bool AreModeControlsEnabled => DisplayChainPolicy.AreModeControlsEnabled(IsBusy, IsApplying, IsCountdownActive, IsSettling, HasPendingArrangement);

    public string? ModeControlsDisabledTip => AreModeControlsEnabled
        ? null
        : DisplayChainPolicy.ModeControlsDisabledReason(IsBusy, IsApplying, IsCountdownActive, IsSettling, HasPendingArrangement);

    public string? MakePrimaryDisabledTip => IsMakePrimaryEnabled
        ? null
        : DisplayChainPolicy.MakePrimaryDisabledReason(IsBusy, IsApplying, IsCountdownActive, IsSettling, SelectedMonitor is not null, SelectedMonitor?.IsPrimary == true, _makePrimaryPending);

    public string? KeepDisabledTip => CanKeep()
        ? null
        : DisplayChainPolicy.KeepDisabledReason(IsBusy, IsApplying, IsCountdownActive, IsSettling, HasRevertFailed);

    [ObservableProperty]
    public partial DisplayScaleOption? SelectedScaleOption { get; set; }

    [ObservableProperty]
    public partial bool IsHdrOn { get; set; }

    [ObservableProperty]
    public partial string HdrStatusText { get; set; }

    [ObservableProperty]
    public partial DisplayHdrState? HdrState { get; set; }

    public bool IsHdrToggleEnabled => AreModeControlsEnabled && HdrState is { Supported: true, ForceDisabled: false };

    public string? HdrToggleTip => IsHdrToggleEnabled
        ? null
        : ModeControlsDisabledTip ?? DisplayHdrService.ToggleDisabledReason(HdrState);

    partial void OnIsHdrOnChanged(bool value)
    {
        if (_suppressUi)
            return;

        _ = RequestHdrApplyAsync(value);
    }

    partial void OnHdrStateChanged(DisplayHdrState? value)
    {
        OnPropertyChanged(nameof(IsHdrToggleEnabled));
        OnPropertyChanged(nameof(HdrToggleTip));
    }

    [ObservableProperty]
    public partial DisplayPickedMode SelectedMode { get; set; } = DisplayPickedMode.Empty;

    public DisplayResolutionOption? SelectedResolution
    {
        get => SelectedMode.Resolution;
        set => OnSelectedResolutionChanged(value);
    }

    public DisplayRefreshOption? SelectedRefresh
    {
        get => SelectedMode.Refresh;
        set => OnSelectedRefreshChanged(value);
    }

    public DisplayOrientationOption? SelectedOrientation
    {
        get => SelectedMode.Orientation;
        set => OnSelectedOrientationChanged(value);
    }

    private void SetSelectedMode(DisplayPickedMode mode)
    {
        var previous = SelectedMode;
        if (previous == mode)
            return;

        SelectedMode = mode;
        if (!Equals(previous.Resolution, mode.Resolution))
            OnPropertyChanged(nameof(SelectedResolution));
        if (!Equals(previous.Refresh, mode.Refresh))
            OnPropertyChanged(nameof(SelectedRefresh));
        if (!Equals(previous.Orientation, mode.Orientation))
            OnPropertyChanged(nameof(SelectedOrientation));
    }

    [ObservableProperty]
    public partial bool IsScaleEmpty { get; set; }

    [ObservableProperty]
    public partial string ScaleEmptyMessage { get; set; }

    public DisplaySnapshot? Snapshot { get; private set; }

    public ObservableCollection<DisplayMonitor> Monitors { get; } = [];

    public ObservableCollection<DisplayResolutionOption> Resolutions { get; } = [];

    public ObservableCollection<DisplayRefreshOption> RefreshRates { get; } = [];

    public ObservableCollection<DisplayScaleOption> ScaleOptions { get; } = [];

    public ObservableCollection<DisplayOrientationOption> OrientationOptions { get; } = [];

    public Dictionary<string, (int X, int Y)> PendingPositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasError => !string.IsNullOrEmpty(ErrorTitle);

    public bool HasLoaded => _hasLoaded;

    public bool IsEmpty => _hasLoaded && !IsBusy && Monitors.Count == 0;

    public bool HasSelection => SelectedMonitor is not null;

    public bool CanMakePrimary => SelectedMonitor is not null && !SelectedMonitor.IsPrimary && !_makePrimaryPending;

    public bool HasPendingArrangement => PendingPositions.Count > 0 || _makePrimaryPending;

    public bool IsStageActionsVisible => DisplayChainPolicy.IsStageActionAllowed(HasPendingArrangement, IsApplying, IsCountdownActive, IsSettling, IsBusy);

    public bool IsChainActive => _chain.IsChainActive;

    public string LogPreview => string.IsNullOrEmpty(ErrorLog)
        ? string.Empty
        : string.Join(Environment.NewLine, ErrorLog.Replace("\r\n", "\n").Split('\n').Take(60));

    public void AttachDispatcher(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    public void StartRefreshWatch()
    {
        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        _pollTimer?.Stop();
        _pollTimer = _dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(5);
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    public void StopRefreshWatch()
    {
        if (_pollTimer is null)
            return;

        _pollTimer.Tick -= OnPollTick;
        _pollTimer.Stop();
        _pollTimer = null;
    }

    private void OnPollTick(DispatcherQueueTimer sender, object args) =>
        _ = PollForExternalChangesAsync();

    private async Task PollForExternalChangesAsync()
    {
        if (_pollInFlight || DisplayChainPolicy.ShouldDeferBackgroundRefresh(HasPendingArrangement, _chain.IsChainActive, IsBusy, IsApplying, _hasLoaded))
            return;

        _pollInFlight = true;
        var epoch = _snapshotEpoch;
        try
        {
            var snapshot = await DisplayService.LoadAsync().ConfigureAwait(true);
            if (epoch != _snapshotEpoch)
                return;

            if (DisplayArrangement.AreEquivalent(Snapshot, snapshot))
                return;

            ApplySnapshot(snapshot, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _pollInFlight = false;
        }
    }

    public DisplaySnapshot GetCanvasSnapshot()
    {
        if (_countdownCanvas is not null && IsCountdownActive)
            return _countdownCanvas;

        return BuildCanvasSnapshot(includeStage: true, validateDisplay: true);
    }

    public DisplaySnapshot GetCanvasSnapshotWithoutStage() => BuildCanvasSnapshot(includeStage: false, validateDisplay: false);

    private DisplaySnapshot BuildCanvasSnapshot(bool includeStage, bool validateDisplay)
    {
        if (Snapshot is null)
            return DisplaySnapshot.Empty;

        var reanchored = _makePrimaryPending && SelectedMonitor is not null
            ? DisplayArrangement.ReanchorToOrigin(Snapshot, SelectedMonitor.GdiDeviceName)
            : null;
        var picked = SelectedMode;
        var monitors = Snapshot.Monitors.Select(monitor =>
        {
            var working = monitor;
            var displayKey = monitor.GdiDeviceName;
            if (includeStage && PendingPositions.TryGetValue(monitor.GdiDeviceName, out var position))
                working = working.WithPosition(position.X, position.Y);

            if (SelectedMonitor is not null &&
                string.Equals(monitor.GdiDeviceName, SelectedMonitor.GdiDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                working = DisplayApplyService.ComposeSelectedMonitorMode(working, picked);
            }

            if (picked is not null && !_makePrimaryPending &&
                !PendingPositions.ContainsKey(monitor.GdiDeviceName) &&
                displayKey is not null &&
                string.Equals(monitor.GdiDeviceName, displayKey, StringComparison.OrdinalIgnoreCase) &&
                Snapshot is not null)
            {
                var neighbours = new DisplaySnapshot(Snapshot.Monitors
                    .Where(other => !string.Equals(other.GdiDeviceName, displayKey, StringComparison.OrdinalIgnoreCase))
                    .ToArray());
                working = DisplayGeometry.KeepSharedEdge(monitor, working, neighbours.Monitors);
            }

            if (reanchored is not null)
            {
                var placed = reanchored.Monitors.First(candidate =>
                    string.Equals(candidate.GdiDeviceName, working.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
                working = working.WithPosition(placed.PositionX, placed.PositionY);
            }

            return working;
        }).ToArray();

        var composed = new DisplaySnapshot(monitors);
        if (!validateDisplay)
            return composed;

        var (resolved, refusal) = DisplayChainPolicy.ResolveCanvasSnapshot(
            Snapshot,
            composed,
            _lastValidCanvas,
            hasStage: PendingPositions.Count > 0 || _makePrimaryPending);
        if (refusal is null)
        {
            _lastValidCanvas = resolved;
            return resolved;
        }

        ShowStatus(refusal, DisplayNoticeKind.Refusal);
        Announce(refusal);
        return resolved;
    }

    public bool StageArrangement(DisplaySnapshot arrangement)
    {
        if (Snapshot is null || arrangement is null)
            return false;

        if (!DisplayArrangementComposer.TryReplaceStage(GetCanvasSnapshotWithoutStage(), arrangement, PendingPositions))
            return false;

        RaiseStageChanged();
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void DiscardStagedArrangement()
    {
        if (_chain.IsChainActive || IsApplying || IsSettling)
        {
            ShowStatus("A change is being applied right now. Wait for it to finish.", DisplayNoticeKind.Refusal);
            return;
        }

        PendingPositions.Clear();
        if (_makePrimaryPending)
        {
            _makePrimaryPending = false;
            OnPropertyChanged(nameof(CanMakePrimary));
            OnPropertyChanged(nameof(IsMakePrimaryEnabled));
            OnPropertyChanged(nameof(MakePrimaryDisabledTip));
            MakePrimaryCommand.NotifyCanExecuteChanged();
        }

        RaiseStageChanged();
        ShowStatus("Staged arrangement discarded. Nothing changed.", DisplayNoticeKind.Neutral);
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ConsumePositionStage()
    {
        if (PendingPositions.Count == 0)
            return;

        PendingPositions.Clear();
        RaiseStageChanged();
    }

    private void RaiseStageChanged()
    {
        OnPropertyChanged(nameof(HasPendingArrangement));
        OnPropertyChanged(nameof(IsStageActionsVisible));
        OnPropertyChanged(nameof(AreModeControlsEnabled));
        OnPropertyChanged(nameof(ModeControlsDisabledTip));
        OnPropertyChanged(nameof(IsHdrToggleEnabled));
        OnPropertyChanged(nameof(HdrToggleTip));
        ApplyStagedCommand.NotifyCanExecuteChanged();
        DiscardStagedCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMonitorChanged(DisplayMonitor? value)
    {
        if (_suppressUi)
            return;

        ErrorTitle = string.Empty;
        ErrorMessage = string.Empty;
        if (value?.IsPrimary == true && MakePrimaryDisabledTip is string reason)
            Announce(reason);
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        _ = LoadModesForSelectionAsync();
    }

    partial void OnSelectedScaleOptionChanged(DisplayScaleOption? value)
    {
        if (_suppressUi || value is null)
            return;

        _ = RequestScaleApplyAsync(value.Step);
    }

    private void OnSelectedResolutionChanged(DisplayResolutionOption? value)
    {
        if (_suppressUi || value is null)
        {
            SetSelectedMode(SelectedMode with { Resolution = value });
            return;
        }

        if (PendingPositions.Count > 0 || _makePrimaryPending)
        {
            RefuseModePickerChangeWhileStaged();
            return;
        }

        SetSelectedMode(DisplayModeCatalog.WithResolution(SelectedMode, value));
        RebuildRefreshRates(
            value.Width,
            value.Height,
            preferCurrent: true,
            preferredHz: SelectedMonitor?.RefreshHz ?? 0);
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        _ = RequestApplyAsync();
    }

    private void OnSelectedRefreshChanged(DisplayRefreshOption? value)
    {
        if (_suppressUi || value is null)
        {
            SetSelectedMode(SelectedMode with { Refresh = value });
            return;
        }

        if (PendingPositions.Count > 0 || _makePrimaryPending)
        {
            RefuseModePickerChangeWhileStaged();
            return;
        }

        if (value.ChangesResolution)
        {
            var parts = value.ResolutionKey.Split('x');
            if (parts.Length == 2 &&
                uint.TryParse(parts[0], out var width) &&
                uint.TryParse(parts[1], out var height))
            {
                var resolution = Resolutions.FirstOrDefault(option => option.Width == width && option.Height == height);
                if (resolution is not null && !ReferenceEquals(SelectedMode.Resolution, resolution))
                {
                    var prevSuppress = _suppressUi;
                    _suppressUi = true;
                    try
                    {
                        SetSelectedMode(SelectedMode with { Resolution = resolution });
                    }
                    finally
                    {
                        _suppressUi = prevSuppress;
                    }

                    RebuildRefreshRates(width, height, preferCurrent: true, preferredHz: value.RefreshHz);
                }
            }
        }

        SetSelectedMode(SelectedMode with { Refresh = value });
        _ = RequestApplyAsync();
    }

    private bool _orientationChangeActive;

    private void OnSelectedOrientationChanged(DisplayOrientationOption? value)
    {
        if (_orientationChangeActive)
            return;

        if (_suppressUi || value is null)
        {
            SetSelectedMode(SelectedMode with { Orientation = value });
            return;
        }

        if (PendingPositions.Count > 0 || _makePrimaryPending)
        {
            RefuseModePickerChangeWhileStaged();
            return;
        }

        _orientationChangeActive = true;
        try
        {
            SetSelectedMode(DisplayModeCatalog.WithOrientation(SelectedMode, value));
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
            _ = RequestApplyAsync();
        }
        finally
        {
            _orientationChangeActive = false;
        }
    }

    private void RefuseModePickerChangeWhileStaged()
    {
        _refusedMode = SelectedMode;
        var warning = DisplayChainPolicy.ModePickerStagedConflictMessage();
        ShowStatus(warning, DisplayNoticeKind.Refusal);
        Announce(warning);
        ResetModePickersToLive();
    }

    private void ResetModePickersToLive()
    {
        var live = SelectedMonitor;
        var prevSuppress = _suppressUi;
        _suppressUi = true;
        try
        {
            if (live is null)
            {
                SelectedResolution = null;
                SelectedRefresh = null;
                SelectedOrientation = null;
            }
            else
            {
                SelectedResolution = DisplayModeCatalog.SelectResolution(Resolutions, live.PixelWidth, live.PixelHeight, live.Rotation);
                SelectedRefresh = DisplayModeCatalog.SelectRefreshRate(RefreshRates, live.RefreshHz);
                SelectedOrientation = OrientationOptions.FirstOrDefault(option => option.CcdRotation == live.Rotation)
                    ?? OrientationOptions.FirstOrDefault();
            }
        }
        finally
        {
            _suppressUi = prevSuppress;
        }

        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private bool CanLoad() => !IsBusy && !IsCountdownActive && !IsApplying && !IsSettling;

    private bool CanCopyErrorLog() => !string.IsNullOrEmpty(ErrorLog);

    private bool CanKeep() => IsCountdownActive && !IsBusy && !IsSettling;

    private bool CanRevertNow() => IsRevertAvailable && !IsBusy && !IsSettling;

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync() => await LoadCoreAsync().ConfigureAwait(true);

    private async Task LoadCoreAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var version = Interlocked.Increment(ref _loadVersion);
        var raise = !IsApplying && !IsCountdownActive && !IsSettling;

        ClearError();

        try
        {
            if (raise)
            {
                BusyText = "Detecting displays...";
                AcquireBusy();
            }

            var snapshot = await DisplayService.LoadAsync().ConfigureAwait(true);
            if (version != _loadVersion || cts.IsCancellationRequested)
                return;

            ApplySnapshot(snapshot, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
            _hasLoaded = true;
            OnPropertyChanged(nameof(HasLoaded));
            OnPropertyChanged(nameof(IsEmpty));
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            if (version == _loadVersion && !cts.IsCancellationRequested)
            {
                ErrorTitle = "Couldn't read display configuration";
                ErrorMessage = "Couldn't read the display configuration. Nothing changed.";
                ErrorLog = ex.ToString();
            }
        }
        finally
        {
            if (raise)
                ReleaseBusy();
        }
    }


    [RelayCommand(CanExecute = nameof(CanMakePrimary))]
    private void MakePrimary()
    {
        if (SelectedMonitor is null || SelectedMonitor.IsPrimary)
            return;

        if (PendingPositions.Count > 0)
        {
            ShowStatus("Apply or cancel the staged arrangement first. Making this display the main display now would also apply those staged moves.", DisplayNoticeKind.Refusal);
            Announce("Apply or cancel the staged arrangement first. Making this display the main display now would also apply those staged moves.");
            return;
        }

        _makePrimaryPending = true;
        OnPropertyChanged(nameof(CanMakePrimary));
        OnPropertyChanged(nameof(IsMakePrimaryEnabled));
        OnPropertyChanged(nameof(MakePrimaryDisabledTip));
        MakePrimaryCommand.NotifyCanExecuteChanged();
        RaiseStageChanged();
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        _ = RequestApplyAsync();
    }

    private bool CanApplyStaged() => DisplayChainPolicy.IsStageActionAllowed(HasPendingArrangement, IsApplying, IsCountdownActive, IsSettling, IsBusy);

    [RelayCommand(CanExecute = nameof(CanApplyStaged))]
    private async Task ApplyStagedAsync()
    {
        if (!DisplayChainPolicy.IsStageActionAllowed(HasPendingArrangement, IsApplying, IsCountdownActive, IsSettling, IsBusy))
            return;

        await RequestApplyAsync().ConfigureAwait(true);
    }

    private bool CanDiscardStaged() => DisplayChainPolicy.IsStageActionAllowed(HasPendingArrangement, IsApplying, IsCountdownActive, IsSettling, IsBusy);

    [RelayCommand(CanExecute = nameof(CanDiscardStaged))]
    private void DiscardStaged()
    {
        if (!DisplayChainPolicy.IsStageActionAllowed(HasPendingArrangement, IsApplying, IsCountdownActive, IsSettling, IsBusy))
            return;

        DiscardStagedArrangement();
    }

    public async Task RequestApplyAsync()
    {
        if (SelectedMonitor is null || Snapshot is null || !_hasLoaded)
            return;

        if (DisplayChainPolicy.ShouldDropApplyRequest(_chain.IsChainActive, _chain.IsCountdownActive, _scaleRevertArmed, _hdrRevertArmed))
            return;

        var arrangementInitiated = PendingPositions.Count > 0 || _makePrimaryPending;
        var plan = BuildPlan();
        if (_chain.IsNoOp(plan, Snapshot))
        {
            if (DisplayChainPolicy.ShouldConsumeMatchingStage(plan, Snapshot, PendingPositions.Count > 0, _makePrimaryPending))
            {
                PendingPositions.Clear();
                if (_makePrimaryPending)
                {
                    _makePrimaryPending = false;
                    OnPropertyChanged(nameof(CanMakePrimary));
                    OnPropertyChanged(nameof(IsMakePrimaryEnabled));
                    OnPropertyChanged(nameof(MakePrimaryDisabledTip));
                    MakePrimaryCommand.NotifyCanExecuteChanged();
                }

                RaiseStageChanged();
                var consumed = DisplayChainPolicy.StageMatchesLiveMessage();
                ShowStatus(consumed, DisplayNoticeKind.Neutral);
                Announce(consumed);
                CanvasInvalidated?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_chain.IsRefusedReapply(plan))
        {
            var displayText = string.Join(", ", plan.TargetMonitors.Select(target => target.DisplayLabel));
            var modeText = string.Join(", ", plan.TargetMonitors.Select(target =>
                $"{target.PixelWidth} × {target.PixelHeight}@{target.RefreshHz:0.#} Hz"));
            var refused = DisplayApplyService.ValidateFailureMessage(displayText, modeText);
            ShowStatus(refused, DisplayNoticeKind.Failure);
            Announce(refused);
            return;
        }

        if (!_chain.TryBeginApply(plan, Snapshot, DateTimeOffset.UtcNow, out var startedNewChain))
            return;

        if (startedNewChain)


            if (startedNewChain)
            {
                ClearScaleRevert();
                _temporaryApplied = false;
            }

        ConsumePositionStage();

        if (IsCountdownActive)
            StopCountdown();

        HasRevertFailed = false;
        SetArrangementOnlyCycle(arrangementInitiated);
        SetApplyingFlag();
        BarMessage = "Applying…";
        Announce("Applying display changes.");
        try
        {
            while (true)
            {
                await RunSingleCycleAsync(arrangementInitiated).ConfigureAwait(true);
                if (_chain.TakeCoalescedPlan() is null)
                    return;

                var next = BuildPlan();
                if (_chain.IsNoOp(next, Snapshot))
                    return;

                if (!_chain.TryBeginApply(next, _chain.ChainBaseline ?? Snapshot, DateTimeOffset.UtcNow, out var supersededNewChain))
                    return;

                if (supersededNewChain)
                    ClearScaleRevert();

                if (IsCountdownActive)
                    StopCountdown();

                SetApplyingFlag();
                BarMessage = "Applying…";
                Announce("Applying display changes.");
            }
        }
        catch (Exception ex)
        {
            var failedPlan = _chain.StageRestorePlan ?? _chain.LatestPlan;
            var restoreBaseline = _chain.ChainBaseline ?? Snapshot;
            var wasMakePrimary = _makePrimaryPending;
            ErrorTitle = "Couldn't apply display settings";
            ErrorLog = ex.ToString();
            var liveMatchesBaseline = _temporaryApplied || await LiveMatchesChainBaselineAsync(restoreBaseline).ConfigureAwait(true);
            if (!DisplayChainPolicy.ShouldRevertAfterApplyFault(_temporaryApplied, liveMatchesBaseline))
            {
                ErrorMessage = "Couldn't apply those display settings. Nothing changed.";
                StopCountdown();
                _chain.EndChain();
                RestoreStageAfterFailure(failedPlan, restoreBaseline, wasMakePrimary);
                return;
            }

            ErrorMessage = "Couldn't apply those display settings. Reverting to your previous setup…";
            var revert = await RevertInternalAsync().ConfigureAwait(true);
            StopCountdown();
            _chain.EndChain();
            RestoreStageAfterFailure(failedPlan, restoreBaseline, wasMakePrimary);
            var message = revert.Success && !DisplayApplyService.IsPostRevertNotice(revert.Message)
                ? DisplayChainPolicy.ApplyCatchRevertedMessage()
                : $"Couldn't apply those display settings. {revert.Message}";
            ShowStatus(message, DisplayNoticeKind.Failure);
            Announce(message);
            BarMessage = revert.Success && !DisplayApplyService.IsPostRevertNotice(revert.Message) ? string.Empty : revert.Message;
        }
        finally
        {
            IsApplying = false;
            _applyFlagVersion++;
            SetArrangementOnlyCycle(false);
        }
    }

    private static async Task<bool> LiveMatchesChainBaselineAsync(DisplaySnapshot? baseline)
    {
        try
        {
            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            return DisplayChainPolicy.LiveMatchesAppliedState(live, baseline);
        }
        catch
        {
            return false;
        }
    }

    private async Task RunSingleCycleAsync(bool arrangementInitiated)
    {
        var plan = _chain.LatestPlan ?? BuildPlan();
        var restoreBaseline = _chain.ChainBaseline ?? Snapshot;
        var wasMakePrimary = _makePrimaryPending;
        string? repairAnnouncement = null;
        var repair = DisplayChainPolicy.RepairModePlan(restoreBaseline, plan);
        if (repair.Kind == DisplayModeRepairKind.Unrepairable && repair.RefusalMessage is not null)
        {
            ShowStatus(repair.RefusalMessage, DisplayNoticeKind.Refusal);
            Announce(repair.RefusalMessage);
            await FailCycle(restoreBaseline, wasMakePrimary, reassertPick: false).ConfigureAwait(true);
            return;
        }

        if (repair.Kind == DisplayModeRepairKind.Repaired && repair.Plan is not null)
        {
            _chain.ReplaceLatestPlan(repair.Plan, plan);
            plan = repair.Plan;
            repairAnnouncement = string.IsNullOrEmpty(repair.Announcement) ? null : repair.Announcement;
        }

        var geometryRefusal = DisplayChainPolicy.GeometryRefusalForPlan(plan);
        if (geometryRefusal is not null)
        {
            if (wasMakePrimary)
            {
                ShowStatus("Making this display the primary would overlap the other displays or leave no display at the origin. Fix the arrangement first.", DisplayNoticeKind.Refusal);
            }
            else
            {
                ShowStatus(geometryRefusal, DisplayNoticeKind.Refusal);
                Announce(geometryRefusal);
            }

            await FailCycle(restoreBaseline, wasMakePrimary, reassertPick: false).ConfigureAwait(true);
            return;
        }


        var validation = await DisplayApplyService.ValidateAsync(plan).ConfigureAwait(true);
        if (!validation.Success)
        {
            await FailCycle(restoreBaseline, wasMakePrimary).ConfigureAwait(true);
            ShowStatus(validation.Message, DisplayNoticeKind.Failure);
            Announce(validation.Message);
            return;
        }

        var temporary = await DisplayApplyService.ApplyTemporaryAsync(plan).ConfigureAwait(true);
        if (!temporary.Success)
        {
            await FailCycle(restoreBaseline, wasMakePrimary).ConfigureAwait(true);
            ShowStatus(temporary.Message, DisplayNoticeKind.Failure);
            Announce(temporary.Message);
            return;
        }

        _temporaryApplied = true;

        var (isTopology, mismatchDetail) = DisplayChainPolicy.DescribeMismatch(plan, temporary.ReadBack);
        if (isTopology)
        {
            var topologyRevert = await RevertInternalAsync().ConfigureAwait(true);
            var stageRestored = await FailCycle(restoreBaseline, wasMakePrimary).ConfigureAwait(true);
            if (!topologyRevert.Success && !IsCountdownActive)
                BarMessage = topologyRevert.Message;
            var topologyMessage = DisplayChainPolicy.TopologyMessage(mismatchDetail, topologyRevert.Success, stageRestored);
            ShowStatus($"{topologyMessage} {topologyRevert.Message}", DisplayNoticeKind.Failure);
            Announce($"{topologyMessage} {topologyRevert.Message}");
            return;
        }

        if (mismatchDetail is not null && !arrangementInitiated && DisplayChainPolicy.PlanCarriesModeChange(plan, _chain.ChainBaseline))
        {
            var adjustment = DisplayChainPolicy.MismatchAdjustmentMessage(mismatchDetail);
            ShowStatus(adjustment, DisplayNoticeKind.Neutral);
            Announce(adjustment);
        }

        if (arrangementInitiated || !DisplayChainPolicy.PlanCarriesModeChange(plan, _chain.ChainBaseline))
        {
            await PersistArrangementOnlyAsync(plan).ConfigureAwait(true);
            return;
        }

        _chain.CompleteCycle(DateTimeOffset.UtcNow, success: true, out _);
        _appliedReadBack = temporary.ReadBack;
        _countdownCanvas = DisplayChainPolicy.ResolveCountdownCanvas(plan, _chain.ChainBaseline, temporary.ReadBack, mismatchDetail);
        BeginCountdownUI(_chain.CurrentCycleId);
        IsApplying = false;
        if (repairAnnouncement is not null && repair.Plan is not null)
        {
            var movedConfirmed = DisplayChainPolicy.ReadBackConfirmsMovedPositions(repair.Plan, repair.MovedMonitorKeys, temporary.ReadBack);
            var status = DisplayChainPolicy.RepairedApplyStatus(mismatchDetail, repairAnnouncement, movedConfirmed);
            ShowStatus(status, DisplayNoticeKind.Neutral);
            Announce($"Keep these settings? {status}");
        }
    }

    private async Task PersistArrangementOnlyAsync(DisplayApplyPlan plan)
    {
        DisplayApplyResult saved;
        _temporaryApplied = true;
        saved = await DisplayApplyService.PersistAsync(plan).ConfigureAwait(true);
        if (!saved.Success)
        {
            var restoreBaseline = _chain.ChainBaseline ?? Snapshot;
            var wasMakePrimary = _makePrimaryPending;
            var revert = await RevertInternalAsync().ConfigureAwait(true);
            RestoreStageAfterFailure(_chain.StageRestorePlan ?? plan, restoreBaseline, wasMakePrimary);
            ShowStatus($"{saved.Message} {revert.Message}", DisplayNoticeKind.Failure);
            Announce($"{saved.Message} {revert.Message}");
            BarMessage = revert.Success && !DisplayApplyService.IsPostRevertNotice(revert.Message) ? string.Empty : revert.Message;
            _chain.EndChain();
            IsApplying = false;
            return;
        }

        var (keepTopology, keepDetail) = DisplayChainPolicy.DescribeMismatch(plan, saved.ReadBack);
        ClearChainState();
        if (saved.ReadBack is not null)
            ApplySnapshot(saved.ReadBack, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);

        string message = DisplayChainPolicy.ArrangementKeepMessage(keepTopology, keepDetail);
        ShowStatus(message, keepDetail is null && !keepTopology ? DisplayNoticeKind.Saved : DisplayNoticeKind.Failure);
        Announce(message);
        CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        _chain.EndChain();
        IsApplying = false;
        if (!IsBarVisible)
            BarMessage = string.Empty;
    }

    private async Task<bool> FailCycle(DisplaySnapshot? restoreBaseline, bool excludePrimarySwap, bool reassertPick = true)
    {
        var failedPlan = _chain.StageRestorePlan ?? _chain.LatestPlan;
        _chain.CompleteCycle(DateTimeOffset.UtcNow, success: false, out var resumeCountdown);
        IsApplying = false;
        if (resumeCountdown)
        {
            _countdownCanvas = null;
            BeginCountdownUI(_chain.CurrentCycleId);
            return false;
        }
        else
        {
            _temporaryApplied = false;
            if (!IsBarVisible)
                BarMessage = string.Empty;
            var stageRestored = RestoreStageAfterFailure(failedPlan, restoreBaseline, excludePrimarySwap);
            if (Snapshot is not null)
                ApplySnapshot(Snapshot, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
            if (reassertPick)
                ReassertSelections();
            _ = ReloadScaleOptionsAsync();
            return stageRestored;
        }
    }

    private bool RestoreStageAfterFailure(DisplayApplyPlan? plan, DisplaySnapshot? baseline, bool excludePrimarySwap)
    {
        if (excludePrimarySwap)
        {
            PendingPositions.Clear();
            if (_makePrimaryPending)
            {
                _makePrimaryPending = false;
            }

            OnPropertyChanged(nameof(CanMakePrimary));
            OnPropertyChanged(nameof(IsMakePrimaryEnabled));
            OnPropertyChanged(nameof(MakePrimaryDisabledTip));
            MakePrimaryCommand.NotifyCanExecuteChanged();
            RaiseStageChanged();
            return false;
        }

        var stageRestored = DisplayArrangementComposer.RestoreStageExcludingSwap(plan?.TargetMonitors, baseline, PendingPositions, null, null, false);
        OnPropertyChanged(nameof(CanMakePrimary));
        OnPropertyChanged(nameof(IsMakePrimaryEnabled));
        OnPropertyChanged(nameof(MakePrimaryDisabledTip));
        MakePrimaryCommand.NotifyCanExecuteChanged();
        RaiseStageChanged();
        return stageRestored;
    }

    private async Task FailScaleCycleAsync()
    {
        _chain.CompleteCycle(DateTimeOffset.UtcNow, success: false, out _);
        IsApplying = false;
        ClearScaleRevert();
        if (!IsBarVisible)
            BarMessage = string.Empty;
        ClearChainState();
        try
        {
            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(live, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
        }
        catch
        {
            if (Snapshot is not null)
                ApplySnapshot(Snapshot, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
        }

        ReassertSelections();
        await ReloadScaleOptionsAsync().ConfigureAwait(true);
    }

    private void ClearScaleRevert()
    {
        _scaleRevertArmed = false;
        _scalePreviousStep = 0;
        _scaleRequestedStep = 0;
        _scaleMonitorKey = string.Empty;
        _scaleSource = default;
    }

    private void ClearHdrRevert()
    {
        _hdrRevertArmed = false;
        _hdrPreviousEnabled = false;
        _hdrRequestedEnabled = false;
        _hdrMonitorKey = string.Empty;
        _hdrTarget = null;
    }

    private async Task ReloadHdrStateAsync()
    {
        var version = Interlocked.Increment(ref _hdrVersion);
        var monitor = SelectedMonitor;
        if (monitor is null || !_hasLoaded)
        {
            var prevClear = _suppressUi;
            _suppressUi = true;
            try
            {
                HdrState = null;
                IsHdrOn = false;
                HdrStatusText = DisplayHdrService.SupportText(null);
            }
            finally
            {
                _suppressUi = prevClear;
            }

            return;
        }

        DisplayHdrRead read;
        try
        {
            var key = monitor.GdiDeviceName;
            read = await Task.Run(() => DisplayHdrService.ReadStateFor(key)).ConfigureAwait(true);
        }
        catch
        {
            read = new DisplayHdrRead(false, null, -1);
        }

        if (version != _hdrVersion)
            return;

        var prevSuppress = _suppressUi;
        _suppressUi = true;
        try
        {
            HdrState = read.State;
            IsHdrOn = read.State?.Enabled == true;
            HdrStatusText = DisplayHdrService.SupportText(read.State);
        }
        finally
        {
            _suppressUi = prevSuppress;
        }
    }

    private async Task RequestHdrApplyAsync(bool requestedState)
    {
        if (SelectedMonitor is null || Snapshot is null || !_hasLoaded)
        {
            _ = ReloadHdrStateAsync();
            return;
        }

        if (PendingPositions.Count > 0 || _makePrimaryPending)
        {
            var staged = DisplayHdrService.StagedConflictMessage();
            ShowStatus(staged, DisplayNoticeKind.Refusal);
            Announce(staged);
            _ = ReloadHdrStateAsync();
            return;
        }

        if (_chain.IsChainActive || IsApplying || IsBusy || IsSettling || _scaleRevertArmed)
        {
            var busy = DisplayChainPolicy.ScaleBusyMessage();
            ShowStatus(busy, DisplayNoticeKind.Refusal);
            Announce(busy);
            _ = ReloadHdrStateAsync();
            return;
        }

        var monitorKey = SelectedMonitor.GdiDeviceName;
        if (!DisplayHdrService.TryGetTargetForMonitor(monitorKey, out var target))
        {
            ShowStatus("Couldn't find that display's HDR control. Nothing changed.", DisplayNoticeKind.Failure);
            Announce("Couldn't find that display's HDR control. Nothing changed.");
            _ = ReloadHdrStateAsync();
            return;
        }

        DisplayHdrRead before;
        try
        {
            before = await Task.Run(() => DisplayHdrService.ReadState(target)).ConfigureAwait(true);
        }
        catch
        {
            before = new DisplayHdrRead(false, null, -1);
        }

        if (!before.Success || before.State is null)
        {
            ShowStatus(DisplayHdrService.ReadFailureMessage(), DisplayNoticeKind.Failure);
            Announce(DisplayHdrService.ReadFailureMessage());
            _ = ReloadHdrStateAsync();
            return;
        }

        if (before.State.Enabled == requestedState)
        {
            ShowStatus(DisplayHdrService.AlreadyActiveMessage, DisplayNoticeKind.Neutral);
            Announce(DisplayHdrService.AlreadyActiveMessage);
            _ = ReloadHdrStateAsync();
            return;
        }

        if (!before.State.Supported || (before.State.ForceDisabled && requestedState))
        {
            var reason = DisplayHdrService.ToggleDisabledReason(before.State) ?? "This display doesn't support HDR.";
            ShowStatus($"{reason} Nothing changed.", DisplayNoticeKind.Refusal);
            Announce($"{reason} Nothing changed.");
            _ = ReloadHdrStateAsync();
            return;
        }

        if (!_chain.TryBeginScaleChain(Snapshot, DateTimeOffset.UtcNow, out _))
        {
            var refusal = DisplayChainPolicy.ScaleBusyMessage();
            ShowStatus(refusal, DisplayNoticeKind.Refusal);
            Announce(refusal);
            _ = ReloadHdrStateAsync();
            return;
        }

        HasRevertFailed = false;
        IsApplying = true;
        BarMessage = "Applying…";
        Announce("Applying HDR change.");
        ClearScaleRevert();
        _hdrTarget = target;
        _hdrPreviousEnabled = before.State.Enabled;
        _hdrRequestedEnabled = requestedState;
        _hdrMonitorKey = monitorKey;
        _hdrRevertArmed = true;
        try
        {

            var result = await DisplayHdrService.SetEnabledAsync(target, requestedState, _hdrPreviousEnabled).ConfigureAwait(true);
            if (!result.Success)
            {
                ShowStatus(result.Message, DisplayNoticeKind.Failure);
                Announce(result.Message);
                await FailHdrCycleAsync().ConfigureAwait(true);
                return;
            }

            _ = ReloadHdrStateAsync();
            _chain.CompleteCycle(DateTimeOffset.UtcNow, success: true, out _);
            BeginCountdownUI(_chain.CurrentCycleId);
            IsApplying = false;
        }
        catch (Exception ex)
        {
            ErrorTitle = "Couldn't apply the HDR change";
            ErrorLog = ex.ToString();
            var confirmed = false;
            try
            {
                var current = await Task.Run(() => DisplayHdrService.ReadState(target)).ConfigureAwait(true);
                confirmed = current.Success && current.State is not null && current.State.Enabled == requestedState;
            }
            catch (Exception inner)
            {
                Debug.WriteLine(inner);
            }

            if (confirmed)
            {
                ErrorMessage = DisplayHdrService.ApplyReadFailureMessage(confirmedApplied: true);
                ShowStatus(ErrorMessage, DisplayNoticeKind.Failure);
                Announce(ErrorMessage);
                _ = ReloadHdrStateAsync();
                _chain.CompleteCycle(DateTimeOffset.UtcNow, success: true, out _);
                BeginCountdownUI(_chain.CurrentCycleId);
            }
            else
            {
                ErrorMessage = DisplayHdrService.ApplyReadFailureMessage(confirmedApplied: false);
                StopCountdown();
                ClearHdrRevert();
                _temporaryApplied = false;
                _chain.EndChain();
                _ = ReloadHdrStateAsync();
            }
        }
        finally
        {
            IsApplying = false;
        }
    }

    private async Task FailHdrCycleAsync()
    {
        _chain.CompleteCycle(DateTimeOffset.UtcNow, success: false, out _);
        IsApplying = false;
        ClearHdrRevert();
        if (!IsBarVisible)
            BarMessage = string.Empty;
        ClearChainState();
        _ = ReloadHdrStateAsync();
    }

    private async Task KeepHdrAsync()
    {
        var requested = _hdrRequestedEnabled;
        var target = _hdrTarget;
        ClearHdrRevert();
        _temporaryApplied = false;
        IsSettling = true;
        if (target is null)
        {
            ShowStatus("Couldn't confirm the HDR change. Change it again to try.", DisplayNoticeKind.Failure);
            Announce("Couldn't confirm the HDR change. Change it again to try.");
            IsSettling = false;
            return;
        }
        try
        {
            var current = await Task.Run(() => DisplayHdrService.ReadState(target)).ConfigureAwait(true);
            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(live, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
            var readable = current.Success && current.State is not null;
            var matches = readable && current.State!.Enabled == requested;
            var message = DisplayHdrService.KeepMessage(readable, matches);
            ShowStatus(message, matches ? DisplayNoticeKind.Saved : DisplayNoticeKind.Failure);
            Announce(message);
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorTitle = "Couldn't confirm the HDR change";
            ErrorMessage = "Couldn't confirm the HDR change. Change it again to try.";
            ErrorLog = ex.ToString();
            ShowStatus("Couldn't confirm the HDR change. Change it again to try.", DisplayNoticeKind.Failure);
            Announce("Couldn't confirm the HDR change. Change it again to try.");
        }
        finally
        {
            IsSettling = false;
            _chain.EndChain();
        }

        if (_chain.TakeCoalescedPlan() is not null)
            _ = RequestApplyAsync();
    }

    private async Task RevertHdrAsync()
    {
        IsSettling = true;
        BarMessage = "Reverting…";
        Announce("Reverting HDR change.");
        try
        {
            if (_hdrTarget is not { } target)
            {
                HasRevertFailed = true;
                _temporaryApplied = false;
                _hdrRevertArmed = false;
                BarMessage = DisplayHdrService.RevertReadFailureMessage();
                Announce(BarMessage);
                return;
            }

            var current = await Task.Run(() => DisplayHdrService.ReadState(target)).ConfigureAwait(true);
            if (!current.Success || current.State is null)
            {
                HasRevertFailed = true;
                _temporaryApplied = false;
                _hdrRevertArmed = false;
                BarMessage = DisplayHdrService.RevertReadFailureMessage();
                Announce(BarMessage);
                return;
            }

            var previous = _hdrPreviousEnabled;
            var result = await DisplayHdrService.SetEnabledAsync(target, previous, current.State.Enabled).ConfigureAwait(true);
            ClearChainState();
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
            _ = ReloadHdrStateAsync();
            if (result.Success)
            {
                var reread = await Task.Run(() => DisplayHdrService.ReadState(target)).ConfigureAwait(true);
                var stateMatches = reread.Success && reread.State is not null && reread.State.Enabled == previous;
                if (stateMatches)
                {
                    ClearHdrRevert();
                    HasRevertFailed = false;
                    BarMessage = string.Empty;
                    ShowStatus("Reverted.", DisplayNoticeKind.Saved);
                    Announce("Reverted.");
                }
                else
                {
                    var weaker = DisplayHdrService.RevertMismatchMessage(reread.Success && reread.State is not null);
                    HasRevertFailed = true;
                    _hdrRevertArmed = false;
                    BarMessage = weaker;
                    ShowStatus(weaker, DisplayNoticeKind.Failure);
                    Announce(weaker);
                }
            }
            else
            {
                HasRevertFailed = true;
                _hdrRevertArmed = false;
                BarMessage = result.Message;
                Announce(result.Message);
            }
        }
        catch
        {
            HasRevertFailed = true;
            _temporaryApplied = false;
            _hdrRevertArmed = false;
            BarMessage = DisplayHdrService.RevertFailureMessage();
            Announce(BarMessage);
        }
        finally
        {
            IsSettling = false;
            _chain.EndChain();
        }

        if (_chain.TakeCoalescedPlan() is not null)
            _ = RequestApplyAsync();
    }

    public async Task RequestScaleApplyAsync(int requestedStep)
    {
        if (SelectedMonitor is null || Snapshot is null || !_hasLoaded)
            return;

        if (PendingPositions.Count > 0 || _makePrimaryPending)
        {
            var refusal = DisplayChainPolicy.ScaleStagedConflictMessage();
            ShowStatus(refusal, DisplayNoticeKind.Refusal);
            Announce(refusal);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        if (_chain.IsChainActive || IsApplying || IsBusy || IsSettling)
        {
            var refusal = DisplayChainPolicy.ScaleBusyMessage();
            ShowStatus(refusal, DisplayNoticeKind.Refusal);
            Announce(refusal);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        var monitorKey = SelectedMonitor.GdiDeviceName;
        var monitorSnapshot = SelectedMonitor;
        ScaleResolveState state;
        try
        {
            state = await Task.Run(() => ResolveScaleState(monitorKey, monitorSnapshot)).ConfigureAwait(true);
        }
        catch
        {
            ShowStatus("Couldn't read the current scale. Nothing changed.", DisplayNoticeKind.Failure);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        if (!state.Found)
        {
            ShowStatus("Couldn't find that display's scale control. Nothing changed.", DisplayNoticeKind.Failure);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        if (!state.Triple.Success)
        {
            ShowStatus("Couldn't read the current scale. Nothing changed.", DisplayNoticeKind.Failure);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        if (state.Triple.MaxRelativeScaleStep < state.Triple.MinRelativeScaleStep)
        {
            ShowStatus("Couldn't read the current scale (the reported range was empty). Nothing changed.", DisplayNoticeKind.Failure);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        _scaleLearned.Observe(monitorKey, state.Triple.CurRelativeScaleStep, state.PercentAtCur);
        var clamped = DisplayScaleOptions.ClampStep(requestedStep, state.Triple.MinRelativeScaleStep, state.Triple.MaxRelativeScaleStep);
        if (clamped == state.Triple.CurRelativeScaleStep)
        {
            ShowStatus(DisplayScaleService.AlreadyActiveMessage, DisplayNoticeKind.Neutral);
            Announce(DisplayScaleService.AlreadyActiveMessage);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        if (!_chain.TryBeginScaleChain(Snapshot, DateTimeOffset.UtcNow, out _))
        {
            var refusal = DisplayChainPolicy.ScaleBusyMessage();
            ShowStatus(refusal, DisplayNoticeKind.Refusal);
            Announce(refusal);
            _ = ReloadScaleOptionsAsync();
            return;
        }

        HasRevertFailed = false;
        IsApplying = true;
        BarMessage = "Applying…";
        Announce("Applying scale change.");
        ClearHdrRevert();
        _scaleSource = state.Source;
        _scalePreviousStep = state.Triple.CurRelativeScaleStep;
        _scaleRequestedStep = clamped;
        _scaleMonitorKey = monitorKey;
        _scaleRevertArmed = true;
        try
        {

            var result = await DisplayScaleService.SetStepAsync(state.Source, clamped, _scalePreviousStep).ConfigureAwait(true);
            if (!result.Success)
            {
                ShowStatus(result.Message, DisplayNoticeKind.Failure);
                Announce(result.Message);
                await FailScaleCycleAsync().ConfigureAwait(true);
                return;
            }

            var readBack = await DisplayService.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(readBack, preserveSelectionKey: monitorKey);
            _appliedReadBack = readBack;
            _countdownCanvas = null;
            _chain.CompleteCycle(DateTimeOffset.UtcNow, success: true, out _);
            BeginCountdownUI(_chain.CurrentCycleId);
            IsApplying = false;
        }
        catch (Exception ex)
        {
            ErrorTitle = "Couldn't apply scale change";
            ErrorLog = ex.ToString();
            var recovery = await ResolveScaleApplyRecoveryAsync(monitorKey).ConfigureAwait(true);
            if (recovery == ScaleApplyRecovery.OfferRevert)
            {
                ErrorMessage = DisplayChainPolicy.ScaleApplyReadFailureMessage(confirmedApplied: true);
                ShowStatus(ErrorMessage, DisplayNoticeKind.Failure);
                Announce(ErrorMessage);
                _countdownCanvas = null;
                _chain.CompleteCycle(DateTimeOffset.UtcNow, success: true, out _);
                BeginCountdownUI(_chain.CurrentCycleId);
            }
            else
            {
                ErrorMessage = recovery == ScaleApplyRecovery.NothingChanged
                    ? "Couldn't apply the scale change. Nothing changed."
                    : DisplayChainPolicy.ScaleApplyReadFailureMessage(confirmedApplied: false);
                StopCountdown();
                ClearScaleRevert();
                _temporaryApplied = false;
                _chain.EndChain();
                ReassertSelections();
                _ = ReloadScaleOptionsAsync();
            }
        }
        finally
        {
            IsApplying = false;
        }
    }

    private async Task<ScaleApplyRecovery> ResolveScaleApplyRecoveryAsync(string monitorKey)
    {
        try
        {
            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(live, preserveSelectionKey: monitorKey);
            _appliedReadBack = live;
            var source = _scaleSource;
            var triple = await Task.Run(() => DisplayService.GetScaleTriple(source)).ConfigureAwait(true);
            if (!triple.Success)
                return ScaleApplyRecovery.Unconfirmed;

            return DisplayChainPolicy.ResolveScaleApplyRecovery(true, triple.CurRelativeScaleStep, _scaleRequestedStep, _scalePreviousStep);
        }
        catch
        {
            return ScaleApplyRecovery.Unconfirmed;
        }
    }

    private async Task KeepScaleAsync()
    {
        var requestedStep = _scaleRequestedStep;
        var source = _scaleSource;
        ClearScaleRevert();
        _temporaryApplied = false;
        IsSettling = true;
        try
        {
            var triple = await Task.Run(() => DisplayService.GetScaleTriple(source)).ConfigureAwait(true);
            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(live, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
            var message = DisplayChainPolicy.ScaleKeepMessage(triple.Success, triple.Success && triple.CurRelativeScaleStep == requestedStep);
            ShowStatus(message, triple.Success && triple.CurRelativeScaleStep == requestedStep ? DisplayNoticeKind.Saved : DisplayNoticeKind.Failure);
            Announce(message);
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorTitle = "Couldn't confirm the scale change";
            ErrorMessage = "Couldn't confirm the kept scale. Change it again to try.";
            ErrorLog = ex.ToString();
            ShowStatus("Couldn't confirm the scale change. Change it again to try.", DisplayNoticeKind.Failure);
            Announce("Couldn't confirm the scale change. Change it again to try.");
        }
        finally
        {
            IsSettling = false;
            _chain.EndChain();
        }

        if (_chain.TakeCoalescedPlan() is not null)
            _ = RequestApplyAsync();
    }

    private async Task RevertScaleAsync()
    {
        IsSettling = true;
        BarMessage = "Reverting…";
        Announce("Reverting scale change.");
        try
        {
            var current = await Task.Run(() => DisplayService.GetScaleTriple(_scaleSource)).ConfigureAwait(true);
            if (!current.Success)
            {
                HasRevertFailed = true;
                _temporaryApplied = false;
                _scaleRevertArmed = false;
                BarMessage = "Couldn't read the current scale. Nothing changed.";
                Announce(BarMessage);
                return;
            }

            var previousStep = _scalePreviousStep;
            var result = await DisplayScaleService.SetStepAsync(_scaleSource, previousStep, current.CurRelativeScaleStep).ConfigureAwait(true);
            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            ApplySnapshot(live, preserveSelectionKey: SelectedMonitor?.GdiDeviceName ?? _scaleMonitorKey);
            var liveScale = live.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, _scaleMonitorKey, StringComparison.OrdinalIgnoreCase));
            if (result.Success && liveScale is not null)
                _scaleLearned.Observe(_scaleMonitorKey, result.AppliedStep, liveScale.ScalePercent);
            ClearChainState();
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
            if (result.Success)
            {
                var reread = await Task.Run(() => DisplayService.GetScaleTriple(_scaleSource)).ConfigureAwait(true);
                var stepMatches = reread.Success && reread.CurRelativeScaleStep == previousStep;
                if (stepMatches)
                {
                    ClearScaleRevert();
                    HasRevertFailed = false;
                    BarMessage = string.Empty;
                    ShowStatus("Reverted.", DisplayNoticeKind.Saved);
                    Announce("Reverted.");
                }
                else
                {
                    var weaker = DisplayChainPolicy.ScaleRevertMismatchMessage(reread.Success);
                    HasRevertFailed = true;
                    _scaleRevertArmed = false;
                    BarMessage = weaker;
                    ShowStatus(weaker, DisplayNoticeKind.Failure);
                    Announce(weaker);
                }
            }
            else
            {
                HasRevertFailed = true;
                _scaleRevertArmed = false;
                BarMessage = result.Message;
                Announce(result.Message);
            }
        }
        catch
        {
            HasRevertFailed = true;
            _temporaryApplied = false;
            _scaleRevertArmed = false;
            BarMessage = DisplayScaleService.RevertFailureMessage();
            Announce(BarMessage);
        }
        finally
        {
            IsSettling = false;
            _chain.EndChain();
        }

        if (_chain.TakeCoalescedPlan() is not null)
            _ = RequestApplyAsync();
    }

    private static ScaleResolveState ResolveScaleState(string monitorKey, DisplayMonitor monitor)
    {
        if (!DisplayService.TryGetSourceForMonitor(monitorKey, out var source))
            return new ScaleResolveState(false, default, new DisplayScaleTriple(0, 0, 0, -1), 0);

        var triple = DisplayService.GetScaleTriple(source);
        var percent = DisplayService.ReadScalePercent(monitor.PositionX, monitor.PositionY, monitor.PixelWidth, monitor.PixelHeight);
        return new ScaleResolveState(true, source, triple, percent);
    }

    private async Task ReloadScaleOptionsAsync()
    {
        var version = Interlocked.Increment(ref _scaleVersion);
        var monitor = SelectedMonitor;
        var prevSuppress = _suppressUi;
        if (monitor is null || !_hasLoaded)
        {
            _suppressUi = true;
            try
            {
                ScaleOptions.Clear();
                SelectedScaleOption = null;
                IsScaleEmpty = true;
                ScaleEmptyMessage = !_hasLoaded
                    ? DisplayChainPolicy.DisabledControlsMessage(isBusy: true, isApplying: false, isCountdown: false, isSettling: false)
                    : DisplayScaleOptions.NoSelectionMessage();
            }
            finally
            {
                _suppressUi = prevSuppress;
            }

            return;
        }

        ScaleResolveState state;
        try
        {
            var monitorSnapshot = monitor;
            state = await Task.Run(() => ResolveScaleState(monitorSnapshot.GdiDeviceName, monitorSnapshot)).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (version != _scaleVersion)
            return;

        prevSuppress = _suppressUi;
        _suppressUi = true;
        try
        {
            ScaleOptions.Clear();
            if (!state.Found || !state.Triple.Success)
            {
                SelectedScaleOption = null;
                IsScaleEmpty = true;
                ScaleEmptyMessage = DisplayScaleOptions.EmptyListMessage(readSucceeded: false);
                return;
            }

            _scaleLearned.Observe(monitor.GdiDeviceName, state.Triple.CurRelativeScaleStep, state.PercentAtCur);
            foreach (var option in DisplayScaleOptions.GetLabeledOptions(
                state.Triple.MinRelativeScaleStep,
                state.Triple.CurRelativeScaleStep,
                state.Triple.MaxRelativeScaleStep,
                state.PercentAtCur,
                monitor.GdiDeviceName,
                _scaleLearned))
                ScaleOptions.Add(option);

            SelectedScaleOption = ScaleOptions.FirstOrDefault(option => option.Step == state.Triple.CurRelativeScaleStep);
            IsScaleEmpty = ScaleOptions.Count == 0;
            ScaleEmptyMessage = IsScaleEmpty
                ? DisplayScaleOptions.EmptyListMessage(readSucceeded: true)
                : string.Empty;
        }
        finally
        {
            _suppressUi = prevSuppress;
        }
    }

    private sealed record ScaleResolveState(
        bool Found,
        DisplayNative.DisplayConfigPathSourceInfo Source,
        DisplayScaleTriple Triple,
        int PercentAtCur);

    [RelayCommand(CanExecute = nameof(CanKeep))]
    private async Task KeepAsync()
    {
        StopCountdown();
        if (!_chain.TryBeginSettle())
            return;

        if (_scaleRevertArmed)
        {
            await KeepScaleAsync().ConfigureAwait(true);
            return;
        }

        if (_hdrRevertArmed)
        {
            await KeepHdrAsync().ConfigureAwait(true);
            return;
        }

        var plan = _chain.LatestPlan;
        if (plan is null)
        {
            _temporaryApplied = false;
            _chain.EndChain();
            return;
        }

        IsSettling = true;
        BarMessage = "Saving…";
        Announce("Saving display settings.");
        try
        {
            DisplayApplyResult result;
            var persistClaimed = _chain.TryClaimPersist();
            if (persistClaimed)
                result = await DisplayApplyService.PersistAsync(plan).ConfigureAwait(true);
            else
                result = new DisplayApplyResult(true, "Display settings saved.", await DisplayService.LoadAsync().ConfigureAwait(true));
            if (!result.Success)
            {
                var restoreBaseline = _chain.ChainBaseline ?? Snapshot;
                var wasMakePrimary = _makePrimaryPending;
                var revert = await RevertInternalAsync().ConfigureAwait(true);
                RestoreStageAfterFailure(_chain.StageRestorePlan ?? plan, restoreBaseline, wasMakePrimary);
                ShowStatus($"{result.Message} {revert.Message}", DisplayNoticeKind.Failure);
                Announce($"{result.Message} {revert.Message}");
                BarMessage = revert.Success && !DisplayApplyService.IsPostRevertNotice(revert.Message) ? string.Empty : revert.Message;
                return;
            }

            var (keepTopology, keepDetail) = DisplayChainPolicy.DescribeMismatch(plan, result.ReadBack);
            if (persistClaimed)
                ClearChainState();
            if (result.ReadBack is not null)
                ApplySnapshot(result.ReadBack, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);

            string keepMessage;
            if (keepDetail is null)
                keepMessage = keepTopology
                    ? "Saved, but the new configuration couldn't be read back. Re-apply from the pickers if you want to try again."
                    : "Display settings saved.";
            else if (keepTopology)
                keepMessage = $"Saved, but {keepDetail} is no longer showing up. Re-apply from the pickers if you want to try again.";
            else
                keepMessage = $"Saved, but {keepDetail}. Re-apply from the pickers if you want to try again.";
            ShowStatus(keepMessage, keepDetail is null && !keepTopology ? DisplayNoticeKind.Saved : DisplayNoticeKind.Failure);
            Announce(keepMessage);
            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorTitle = "Couldn't save display settings";
            ErrorLog = ex.ToString();
            var revert = await RevertInternalAsync().ConfigureAwait(true);
            ErrorMessage = $"Couldn't save those settings. {revert.Message}";
            ShowStatus($"Couldn't save those settings. {revert.Message}", DisplayNoticeKind.Failure);
            Announce($"Couldn't save those settings. {revert.Message}");
            BarMessage = revert.Success && !DisplayApplyService.IsPostRevertNotice(revert.Message) ? string.Empty : revert.Message;
        }
        finally
        {
            IsSettling = false;
            _chain.EndChain();
        }

        if (_chain.TakeCoalescedPlan() is not null)
            _ = RequestApplyAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRevertNow))]
    private async Task RevertNowAsync()
    {
        StopCountdown();
        var isRetry = !_chain.IsChainActive && HasRevertFailed;
        if (!_chain.TryBeginSettle() && !(isRetry && _chain.TryBeginRetry()))
            return;

        if (DisplayChainPolicy.ShouldRevertScale(_scaleRevertArmed, HasRevertFailed, _scaleMonitorKey))
        {
            await RevertScaleAsync().ConfigureAwait(true);
            return;
        }

        if (DisplayChainPolicy.ShouldRevertScale(_hdrRevertArmed, HasRevertFailed, _hdrMonitorKey))
        {
            await RevertHdrAsync().ConfigureAwait(true);
            return;
        }

        IsSettling = true;
        BarMessage = "Reverting…";
        Announce("Reverting display settings.");
        try
        {
            var result = await RevertInternalAsync().ConfigureAwait(true);
            if (result.Success)
            {
                HasRevertFailed = false;
                if (DisplayApplyService.IsPostRevertNotice(result.Message))
                {
                    BarMessage = result.Message;
                    ShowStatus(result.Message, DisplayNoticeKind.Failure);
                    Announce(result.Message);
                }
                else
                {
                    BarMessage = string.Empty;
                    ShowStatus("Reverted.", DisplayNoticeKind.Saved);
                    Announce("Reverted.");
                }

            }
            else
            {
                HasRevertFailed = true;
                BarMessage = result.Message;
                Announce(result.Message);
            }
        }
        catch (Exception ex)
        {
            HasRevertFailed = true;
            _temporaryApplied = false;
            ErrorTitle = "Couldn't finish reverting";
            ErrorMessage = DisplayApplyService.RevertUnfinishedMessage();
            ErrorLog = ex.ToString();
            BarMessage = DisplayApplyService.RevertUnfinishedMessage();
            Announce(BarMessage);
        }
        finally
        {
            IsSettling = false;
            _chain.EndChain();
        }

        if (_chain.TakeCoalescedPlan() is not null)
            _ = RequestApplyAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCopyErrorLog))]
    private void CopyErrorLog()
    {
        if (string.IsNullOrEmpty(ErrorLog))
            return;

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(ErrorLog);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    [RelayCommand]
    private void CloseError() => ClearError();

    private async Task LoadModesForSelectionAsync()
    {
        var version = Interlocked.Increment(ref _modeVersion);
        var prevSuppress = _suppressUi;
        _suppressUi = true;
        try
        {
            OrientationOptions.Clear();
            foreach (var option in DisplayModeCatalog.Orientations)
                OrientationOptions.Add(option);
        }
        finally
        {
            _suppressUi = prevSuppress;
        }

        var raised = false;
        try
        {
            if (SelectedMonitor is null)
            {
                prevSuppress = _suppressUi;
                _suppressUi = true;
                try
                {
                    Resolutions.Clear();
                    RefreshRates.Clear();
                    ScaleOptions.Clear();
                    SelectedScaleOption = null;
                    IsScaleEmpty = true;
                    ScaleEmptyMessage = DisplayScaleOptions.NoSelectionMessage();
                    HdrState = null;
                    IsHdrOn = false;
                    HdrStatusText = DisplayHdrService.SupportText(null);
                }
                finally
                {
                    _suppressUi = prevSuppress;
                }

                return;
            }

            raised = !IsApplying && !IsCountdownActive && !IsSettling;
            if (raised)
            {
                BusyText = "Loading display modes...";
                AcquireBusy();
            }

            var signature = DisplayModeCatalog.ModesCacheKey(SelectedMonitor);
            if (!string.Equals(signature, _modesSignature, StringComparison.Ordinal) || _modes.Count == 0)
            {
                _modes = await DisplayService.EnumerateModesAsync(SelectedMonitor.GdiDeviceName).ConfigureAwait(true);
                if (version != _modeVersion)
                    return;

                _modesSignature = signature;
            }

            var resolutions = DisplayModeCatalog.BuildResolutions(
                _modes,
                SelectedMonitor.PreferredWidth,
                SelectedMonitor.PreferredHeight);

            prevSuppress = _suppressUi;
            _suppressUi = true;
            DisplayResolutionOption? intendedResolution = null;
            try
            {
                Resolutions.Clear();
                foreach (var resolution in resolutions)
                    Resolutions.Add(resolution);

                intendedResolution = DisplayModeCatalog.SelectResolution(
                    Resolutions,
                    SelectedMonitor.PixelWidth,
                    SelectedMonitor.PixelHeight,
                    SelectedMonitor.Rotation);
                SelectedResolution = intendedResolution;

                RebuildRefreshRates(
                    SelectedResolution?.Width ?? SelectedMonitor.PixelWidth,
                    SelectedResolution?.Height ?? SelectedMonitor.PixelHeight,
                    preferCurrent: true,
                    preferredHz: SelectedMonitor.RefreshHz);

                SelectedOrientation = OrientationOptions.FirstOrDefault(option => option.CcdRotation == SelectedMonitor.Rotation)
                    ?? OrientationOptions.FirstOrDefault();
            }
            finally
            {
                _suppressUi = prevSuppress;
            }

            await ReloadScaleOptionsAsync().ConfigureAwait(true);
            await ReloadHdrStateAsync().ConfigureAwait(true);

            _dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                DisplayChainPolicy.RunSuppressed(ref _suppressUi, () =>
                {
                    if (SelectedResolution is null || !Resolutions.Contains(SelectedResolution))
                        SelectedResolution = intendedResolution;
                });

                CanvasInvalidated?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            if (version == _modeVersion)
            {
                ShowStatus("Couldn't load the display modes. Nothing changed.", DisplayNoticeKind.Failure);
                ErrorLog = ex.ToString();
            }
        }
        finally
        {
            if (raised)
                ReleaseBusy();
        }
    }

    private void RebuildRefreshRates(uint width, uint height, bool preferCurrent, double preferredHz = 0)
    {
        var rates = DisplayModeCatalog.BuildAdvancedRefreshRates(_modes, width, height);
        var intended = preferCurrent && preferredHz > 0
            ? DisplayModeCatalog.SelectRefreshRate(rates, preferredHz)
            : rates.FirstOrDefault(option => !option.ChangesResolution) ?? rates.FirstOrDefault();
        var prevSuppress = _suppressUi;
        _suppressUi = true;
        try
        {
            RefreshRates.Clear();
            foreach (var rate in rates)
                RefreshRates.Add(rate);

            SelectedRefresh = intended;
        }
        finally
        {
            _suppressUi = prevSuppress;
        }

        _dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            DisplayChainPolicy.RunSuppressed(ref _suppressUi, () =>
            {
                if (SelectedRefresh is null || !RefreshRates.Contains(SelectedRefresh))
                    SelectedRefresh = intended;
            });

            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        });
    }

    private DisplayApplyPlan BuildPlan()
    {
        var canvas = BuildCanvasSnapshot(includeStage: true, validateDisplay: false);
        return new DisplayApplyPlan(canvas.Monitors.ToArray(), Array.Empty<DisplayPendingChange>());
    }

    private void ApplySnapshot(DisplaySnapshot snapshot, string? preserveSelectionKey)
    {
        _snapshotEpoch++;
        var before = new HashSet<string>(
            Monitors.Select(monitor => monitor.GdiDeviceName),
            StringComparer.OrdinalIgnoreCase);
        Snapshot = snapshot;
        _chain.NoteLiveSnapshot(snapshot);
        var prevSuppress = _suppressUi;
        _suppressUi = true;
        try
        {
            Monitors.Clear();
            foreach (var monitor in snapshot.Monitors)
                Monitors.Add(monitor);

            if (!before.SetEquals(snapshot.Monitors.Select(monitor => monitor.GdiDeviceName)))
            {
                _scaleLearned.Clear();
                _lastValidCanvas = null;
                if (DisplayArrangementComposer.DiscardStageForMissingMonitors(PendingPositions, snapshot.Monitors))
                {
                    if (_makePrimaryPending)
                    {
                        _makePrimaryPending = false;
                        OnPropertyChanged(nameof(CanMakePrimary));
                        OnPropertyChanged(nameof(IsMakePrimaryEnabled));
                        OnPropertyChanged(nameof(MakePrimaryDisabledTip));
                        MakePrimaryCommand.NotifyCanExecuteChanged();
                    }

                    RaiseStageChanged();
                    var discarded = DisplayChainPolicy.StageDiscardedForTopologyMessage();
                    ShowStatus(discarded, DisplayNoticeKind.Neutral);
                    Announce(discarded);
                    CanvasInvalidated?.Invoke(this, EventArgs.Empty);
                }
            }

            SelectedMonitor = Monitors.FirstOrDefault(monitor =>
                preserveSelectionKey is not null &&
                string.Equals(monitor.GdiDeviceName, preserveSelectionKey, StringComparison.OrdinalIgnoreCase))
                ?? Monitors.FirstOrDefault();
        }
        finally
        {
            _suppressUi = prevSuppress;
        }

        _ = LoadModesForSelectionAsync();
    }

    private void BeginCountdownUI(int cycleId)
    {
        if (DisplayChainPolicy.ShouldClearStatusForCountdown(_lastStatusKind))
        {
            StatusMessage = string.Empty;
            IsStatusOpen = false;
            _lastStatusKind = DisplayNoticeKind.Neutral;
        }

        var remaining = _chain.SecondsRemaining(DateTimeOffset.UtcNow);
        _tickCycleId = cycleId;
        _countdownLowAnnounced = false;
        BarMessage = $"Keep these settings? {DisplayChainPolicy.CountdownMessage(remaining)}";
        Announce("Keep these settings?");
        IsCountdownActive = true;

        _dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (_countdownTimer is not null)
        {
            _countdownTimer.Tick -= CountdownTick;
            _countdownTimer.Stop();
            _countdownTimer = null;
        }

        _countdownTimer = _dispatcher.CreateTimer();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.IsRepeating = true;
        _countdownTimer.Tick += CountdownTick;
        _countdownTimer.Start();
    }

    private async void CountdownTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (!ReferenceEquals(sender, _countdownTimer))
                return;

            if (_tickCycleId != _chain.CurrentCycleId || !_chain.IsCountdownActive)
                return;

            var remaining = _chain.SecondsRemaining(DateTimeOffset.UtcNow);
            if (remaining <= 0)
            {
                StopCountdown();
                if (await CountdownGuardAllowsRevertAsync().ConfigureAwait(true))
                    await RevertNowAsync().ConfigureAwait(true);
                else
                {
                    AbandonStaleCountdown();
                }

                return;
            }

            BarMessage = $"Keep these settings? {DisplayChainPolicy.CountdownMessage(remaining)}";
            if (DisplayChainPolicy.ShouldAnnounceCountdown(remaining, _countdownLowAnnounced))
            {
                _countdownLowAnnounced = true;
                Announce(DisplayChainPolicy.CountdownMessage(remaining));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Display countdown tick failed: {ex.Message}");
        }
    }

    private async Task<bool> CountdownGuardAllowsRevertAsync()
    {
        _countdownGuardOutcome = DisplayCountdownGuardOutcome.Matches;
        try
        {
            if (_scaleRevertArmed)
            {
                var source = _scaleSource;
                var triple = await Task.Run(() => DisplayService.GetScaleTriple(source)).ConfigureAwait(true);
                if (!triple.Success)
                {
                    _countdownGuardOutcome = DisplayCountdownGuardOutcome.Unreadable;
                    return false;
                }

                if (triple.CurRelativeScaleStep != _scaleRequestedStep)
                {
                    _countdownGuardOutcome = DisplayCountdownGuardOutcome.Differs;
                    return false;
                }

                return true;
            }

            if (_hdrRevertArmed)
            {
                if (_hdrTarget is not { } hdrTarget)
                {
                    _countdownGuardOutcome = DisplayCountdownGuardOutcome.Unreadable;
                    return false;
                }

                var current = await Task.Run(() => DisplayHdrService.ReadState(hdrTarget)).ConfigureAwait(true);
                if (!current.Success || current.State is null)
                {
                    _countdownGuardOutcome = DisplayCountdownGuardOutcome.Unreadable;
                    return false;
                }

                if (current.State.Enabled != _hdrRequestedEnabled)
                {
                    _countdownGuardOutcome = DisplayCountdownGuardOutcome.Differs;
                    return false;
                }

                return true;
            }

            var applied = _appliedReadBack;
            if (applied is null || _chain.LatestPlan is null)
            {
                _countdownGuardOutcome = DisplayCountdownGuardOutcome.Unreadable;
                return false;
            }

            var live = await DisplayService.LoadAsync().ConfigureAwait(true);
            if (!DisplayChainPolicy.LiveMatchesAppliedState(live, applied))
            {
                _countdownGuardOutcome = DisplayCountdownGuardOutcome.Differs;
                return false;
            }

            return true;
        }
        catch
        {
            _countdownGuardOutcome = DisplayCountdownGuardOutcome.Unreadable;
            return false;
        }
    }

    private void AbandonStaleCountdown()
    {
        if (!_chain.IsCountdownActive)
            return;

        var readable = _countdownGuardOutcome == DisplayCountdownGuardOutcome.Differs;
        _chain.EndChain();
        ClearScaleRevert();
        ClearHdrRevert();
        _temporaryApplied = false;
        _appliedReadBack = null;
        IsApplying = false;
        var message = DisplayChainPolicy.StaleCountdownMessage(readable);
        ShowStatus(message, DisplayNoticeKind.Refusal);
        Announce(message);
        if (!IsBarVisible)
            BarMessage = string.Empty;
    }

    private void StopCountdown()
    {
        if (_countdownTimer is not null)
        {
            _countdownTimer.Tick -= CountdownTick;
            _countdownTimer.Stop();
            _countdownTimer = null;
        }

        _countdownCanvas = null;
        IsCountdownActive = false;
    }

    private async Task<DisplayApplyResult> RevertInternalAsync()
    {
        DisplayApplyResult result;
        DisplaySnapshot? baseline = null;
        try
        {
            baseline = _chain.ChainBaseline ?? Snapshot;
            result = await DisplayApplyService.RevertToDatabaseAsync(baseline).ConfigureAwait(true);
        }
        catch
        {
            var outcome = DisplayApplyService.RevertFaultOutcome(revertSucceeded: false, listUpdated: false);
            HasRevertFailed = outcome.HasRevertFailed;
            _chain.NoteRevertFailure(baseline);
            var failure = new DisplayApplyResult(false, outcome.Message, null);
            BarMessage = failure.Message;
            _temporaryApplied = false;
            return failure;
        }

        try
        {
            HasRevertFailed = !result.Success;
            if (!result.Success)
                _chain.NoteRevertFailure(baseline);
            ClearChainState();
            if (result.ReadBack is not null)
                ApplySnapshot(result.ReadBack, preserveSelectionKey: SelectedMonitor?.GdiDeviceName);
            else
                await LoadCoreAsync().ConfigureAwait(true);

            try
            {
                CanvasInvalidated?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                var redraw = DisplayApplyService.RevertFaultOutcome(result.Success, listUpdated: true);
                HasRevertFailed = redraw.HasRevertFailed;
                if (!result.Success)
                {
                    _chain.NoteRevertFailure(baseline);
                    BarMessage = result.Message;
                    return result;
                }

                BarMessage = redraw.Message;
                Announce(redraw.Message);
                return new DisplayApplyResult(true, redraw.Message, result.ReadBack);
            }
        }
        catch
        {
            var refresh = DisplayApplyService.RevertFaultOutcome(result.Success, listUpdated: false);
            HasRevertFailed = refresh.HasRevertFailed;
            if (!result.Success)
            {
                _chain.NoteRevertFailure(baseline);
                BarMessage = result.Message;
                return result;
            }

            BarMessage = refresh.Message;
            Announce(refresh.Message);
            return new DisplayApplyResult(true, refresh.Message, result.ReadBack);
        }

        return result;
    }

    private void ReassertSelections()
    {
        var intendedResolution = SelectedResolution;
        var intendedRefresh = SelectedRefresh;
        var intendedOrientation = SelectedOrientation;
        _dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            DisplayChainPolicy.RunSuppressed(ref _suppressUi, () =>
            {
                if (intendedResolution is not null &&
                    (SelectedResolution is null || !Resolutions.Contains(SelectedResolution)))
                    SelectedResolution = intendedResolution;
                if (intendedRefresh is not null &&
                    (SelectedRefresh is null || !RefreshRates.Contains(SelectedRefresh)))
                    SelectedRefresh = intendedRefresh;
                if (intendedOrientation is not null &&
                    (SelectedOrientation is null || !OrientationOptions.Contains(SelectedOrientation)))
                    SelectedOrientation = intendedOrientation;
            });

            CanvasInvalidated?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ClearChainState()
    {
        _temporaryApplied = false;
        PendingPositions.Clear();
        _makePrimaryPending = false;
        OnPropertyChanged(nameof(CanMakePrimary));
        OnPropertyChanged(nameof(IsMakePrimaryEnabled));
        OnPropertyChanged(nameof(MakePrimaryDisabledTip));
        MakePrimaryCommand.NotifyCanExecuteChanged();
        RaiseStageChanged();
    }

    public void ShowStatus(string message, DisplayNoticeKind kind)
    {
        StatusMessage = message;
        _lastStatusKind = kind;
        StatusSeverity = DisplayChainPolicy.SeverityForNotice(kind) switch
        {
            DisplayBannerSeverity.Success => InfoBarSeverity.Success,
            DisplayBannerSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        IsStatusOpen = DisplayChainPolicy.ShouldShowBannerForNotice(kind);
    }

    public void Announce(string message)
    {
        if (_dispatcher is not null && _dispatcher.TryEnqueue(() =>
        {
            AnnouncerMessage = string.Empty;
            AnnouncerMessage = message;
        }))
            return;

        AnnouncerMessage = string.Empty;
        AnnouncerMessage = message;
    }

    private void ClearError()
    {
        ErrorTitle = string.Empty;
        ErrorMessage = string.Empty;
        ErrorLog = string.Empty;
    }
}
