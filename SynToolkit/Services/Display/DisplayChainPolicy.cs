#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public enum DisplayModeRepairKind
{
    NotNeeded,
    Repaired,
    Unrepairable
}

public sealed record DisplayModeRepairOutcome(
    DisplayModeRepairKind Kind,
    DisplayApplyPlan? Plan,
    IReadOnlyList<string> MovedMonitorKeys,
    string Announcement,
    string? RefusalMessage);

public enum DisplayCountdownGuardOutcome
{
    Matches,
    Differs,
    Unreadable
}

public enum ScaleApplyRecovery
{
    OfferRevert,
    NothingChanged,
    Unconfirmed
}



public enum DisplayNoticeKind
{
    Neutral,
    Progress,
    Saved,
    Staged,
    Refusal,
    Failure
}

public enum DisplayBannerSeverity
{
    Informational,
    Success,
    Error
}

public sealed class DisplayChainPolicy
{
    public const int CountdownSecondsTotal = 15;
    public enum Phase
    {
        Idle,
        Applying,
        Countdown,
        Settling
    }

    private DisplayApplyPlan? _coalescedPlan;
    private DisplayApplyPlan? _refusedPlan;
    private DisplaySnapshot? _refusedBaseline;
    private bool _cycleIsSupersede;
    private bool _persistedThisChain;

    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    public int CurrentCycleId { get; private set; }

    public DisplaySnapshot? ChainBaseline { get; private set; }

    public DisplaySnapshot? RevertRetryBaseline { get; private set; }

    public DisplayApplyPlan? LatestPlan { get; private set; }

    public DisplayApplyPlan? StageRestorePlan { get; private set; }


    public DateTimeOffset DeadlineUtc { get; private set; }

    public int StoredRemainingSeconds { get; private set; } = CountdownSecondsTotal;

    public bool IsChainActive => CurrentPhase is Phase.Applying or Phase.Countdown or Phase.Settling;

    public bool IsCountdownActive => CurrentPhase == Phase.Countdown;

    public bool HasCoalescedPlan => _coalescedPlan is not null;

    public bool TryBeginApply(DisplayApplyPlan plan, DisplaySnapshot? chainBaselineIfNew, DateTimeOffset nowUtc, out bool isNewChain)
    {
        isNewChain = false;
        if (plan is null)
            return false;

        if (IsRefusedReapply(plan))
            return false;

        switch (CurrentPhase)
        {
            case Phase.Applying:
            case Phase.Settling:
                _coalescedPlan = plan;
                return false;
            case Phase.Countdown:
                StoredRemainingSeconds = SecondsRemaining(nowUtc);
                StageRestorePlan = null;
                _cycleIsSupersede = true;
                break;
            default:
                ChainBaseline = chainBaselineIfNew;
                RevertRetryBaseline = null;
                _persistedThisChain = false;
                StoredRemainingSeconds = CountdownSecondsTotal;
                _coalescedPlan = null;
                _refusedPlan = null;
                _refusedBaseline = null;
                _cycleIsSupersede = false;
                StageRestorePlan = null;
                isNewChain = true;
                break;
        }

        LatestPlan = plan;
        CurrentPhase = Phase.Applying;
        CurrentCycleId++;
        return true;
    }

    public bool TryBeginScaleChain(DisplaySnapshot? chainBaselineIfNew, DateTimeOffset nowUtc, out bool isNewChain)
    {
        isNewChain = false;
        if (CurrentPhase != Phase.Idle)
            return false;

        ChainBaseline = chainBaselineIfNew;
        RevertRetryBaseline = null;
        _persistedThisChain = false;
        StoredRemainingSeconds = CountdownSecondsTotal;
        _coalescedPlan = null;
        _refusedPlan = null;
        _refusedBaseline = null;
        _cycleIsSupersede = false;
        StageRestorePlan = null;
        isNewChain = true;
        LatestPlan = null;
        CurrentPhase = Phase.Applying;
        CurrentCycleId++;
        return true;
    }

    public void CompleteCycle(DateTimeOffset nowUtc, bool success, out bool resumeCountdown)
    {
        resumeCountdown = false;
        if (CurrentPhase != Phase.Applying)
            return;

        if (success || _cycleIsSupersede)
        {
            CurrentPhase = Phase.Countdown;
            DeadlineUtc = nowUtc + TimeSpan.FromSeconds(Math.Max(1, StoredRemainingSeconds));
            resumeCountdown = true;
        }
        else
        {
            _refusedPlan = StageRestorePlan ?? LatestPlan;
            _refusedBaseline = ChainBaseline;
            EndChain();
        }
    }

    public bool TryBeginSettle()
    {
        if (CurrentPhase != Phase.Countdown)
            return false;

        CurrentPhase = Phase.Settling;
        CurrentCycleId++;
        return true;
    }

    public bool TryBeginRetry()
    {
        if (CurrentPhase != Phase.Idle)
            return false;

        if (RevertRetryBaseline is not null)
        {
            ChainBaseline = RevertRetryBaseline;
            RevertRetryBaseline = null;
        }

        CurrentPhase = Phase.Settling;
        CurrentCycleId++;
        return true;
    }

    public void NoteRevertFailure(DisplaySnapshot? baselineUsed) => RevertRetryBaseline = baselineUsed;

    public bool TryClaimPersist()
    {
        if (_persistedThisChain)
            return false;

        _persistedThisChain = true;
        return true;
    }

    public void EndChain()
    {
        CurrentPhase = Phase.Idle;
        LatestPlan = null;
        StageRestorePlan = null;
        ChainBaseline = null;
        _cycleIsSupersede = false;
    }

    public DisplayApplyPlan? TakeCoalescedPlan()
    {
        var pending = _coalescedPlan;
        _coalescedPlan = null;
        return pending;
    }


    public int SecondsRemaining(DateTimeOffset nowUtc)
    {
        var remaining = (int)Math.Ceiling((DeadlineUtc - nowUtc).TotalSeconds);
        return Math.Max(0, remaining);
    }

    public static bool ShouldDeferBackgroundRefresh(bool hasPendingArrangement, bool chainActive, bool isBusy, bool isApplying, bool hasLoaded) =>
        !hasLoaded || isBusy || isApplying || chainActive || hasPendingArrangement;

    public static bool ShouldDropApplyRequest(bool chainActive, bool isCountdownActive, bool scaleRevertArmed, bool hdrRevertArmed) =>
        scaleRevertArmed || hdrRevertArmed || (chainActive && !isCountdownActive);

    public const int BusyOverlayDelayMs = 250;

    public static bool ShouldRaiseBusyOverlay(int scheduledVersion, int currentVersion, int busyDepth, TimeSpan elapsed) =>
        scheduledVersion == currentVersion && busyDepth > 0 && elapsed >= TimeSpan.FromMilliseconds(BusyOverlayDelayMs);

    public static bool ShouldSetPostedApplyingFlag(int postedVersion, int currentVersion) =>
        postedVersion == currentVersion;

    public static bool ShouldShowApplyBar(bool isApplying, bool arrangementOnly, bool isCountdown, bool isSettling, bool hasRevertFailed) =>
        (isApplying && !arrangementOnly) || isCountdown || isSettling || hasRevertFailed;

    public static void RunSuppressed(ref bool flag, Action body)
    {
        var captured = flag;
        flag = true;
        try
        {
            body();
        }
        finally
        {
            flag = captured;
        }
    }

    public static string DisabledControlsMessage(bool isBusy, bool isApplying, bool isCountdown, bool isSettling)
    {
        if (isApplying || isCountdown || isSettling)
            return "Finish or revert the current change first.";

        if (isBusy)
            return "The display settings are still loading. Wait a moment and try again.";

        return "Finish or revert the current change first.";
    }

    public static string MakePrimaryDisabledReason(bool isBusy, bool isApplying, bool isCountdown, bool isSettling, bool hasSelection, bool isAlreadyPrimary, bool stagePending)
    {
        if (isApplying || isCountdown || isSettling || isBusy)
            return DisabledControlsMessage(isBusy, isApplying, isCountdown, isSettling);

        if (!hasSelection)
            return "Select a display to make it the main display.";

        if (isAlreadyPrimary)
            return "This display is already your main display.";

        if (stagePending)
            return "A main-display change is already staged. Apply the staged arrangement to finish.";

        return DisabledControlsMessage(isBusy, isApplying, isCountdown, isSettling);
    }

    public static bool AreModeControlsEnabled(bool isBusy, bool isApplying, bool isCountdown, bool isSettling, bool hasPendingArrangement) =>
        !isBusy && !isApplying && !isCountdown && !isSettling && !hasPendingArrangement;

    public static string StagedControlsDisabledMessage() =>
        "Apply or cancel the staged arrangement first.";

    public static string ModeControlsDisabledReason(bool isBusy, bool isApplying, bool isCountdown, bool isSettling, bool hasPendingArrangement)
    {
        if (isApplying || isCountdown || isSettling || isBusy)
            return DisabledControlsMessage(isBusy, isApplying, isCountdown, isSettling);

        if (hasPendingArrangement)
            return StagedControlsDisabledMessage();

        return DisabledControlsMessage(isBusy, isApplying, isCountdown, isSettling);
    }

    public static string KeepDisabledReason(bool isBusy, bool isApplying, bool isCountdown, bool isSettling, bool hasRevertFailed)
    {
        if (!isCountdown && hasRevertFailed && !isBusy && !isApplying && !isSettling)
            return "There is no countdown running, so there is nothing to keep. Use Revert now to put the old settings back.";

        return DisabledControlsMessage(isBusy, isApplying, isCountdown, isSettling);
    }

    public static bool IsStageActionAllowed(bool hasStage, bool isApplying, bool isCountdown, bool isSettling, bool isBusy) =>
        hasStage && !isApplying && !isCountdown && !isSettling && !isBusy;

    public static bool ShouldReportStage(bool hasStage, bool chainActive, bool isApplying, bool isSettling) =>
        hasStage && !chainActive && !isApplying && !isSettling;

    public static string? GeometryRefusalForPlan(DisplayApplyPlan? plan)
    {
        if (plan is null)
            return null;

        return GeometryRefusalForSnapshot(new DisplaySnapshot(plan.TargetMonitors));
    }

    public static string? GeometryRefusalForSnapshot(DisplaySnapshot? snapshot)
    {
        if (snapshot is null)
            return null;

        var blocking = DisplayGeometry.BlockingIssues(snapshot).FirstOrDefault();
        if (blocking is null)
            return null;

        return blocking.Kind switch
        {
            DisplayGeometryIssueKind.Overlap => "That change would stack two displays on top of each other, so nothing was applied. Drag one aside and try again.",
            _ => "That change would leave no display at the desktop corner (0, 0), so nothing was applied. Drag a display back to that corner and try again."
        };
    }

    public static (DisplaySnapshot Snapshot, string? Refusal) ResolveCanvasSnapshot(
        DisplaySnapshot baseline,
        DisplaySnapshot composed,
        DisplaySnapshot? lastValid,
        bool hasStage = true)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(composed);

        var blocking = DisplayGeometry.BlockingIssues(composed);
        if (blocking.Count == 0)
            return (composed, null);

        var seated = TrySeatedPreview(baseline, composed, blocking);
        if (seated is not null)
            return (seated, null);

        var refusal = hasStage
            ? CanvasRefusalMessage(blocking[0].Kind)
            : CanvasPickerRefusalMessage(blocking[0].Kind);
        return (lastValid ?? composed, refusal);
    }

    private static DisplaySnapshot? TrySeatedPreview(
        DisplaySnapshot baseline,
        DisplaySnapshot composed,
        IReadOnlyList<DisplayGeometryIssue> issues)
    {
        var repair = RepairModePlan(
            baseline,
            new DisplayApplyPlan(composed.Monitors.ToArray(), Array.Empty<DisplayPendingChange>()));
        if (repair.Kind != DisplayModeRepairKind.Repaired || repair.Plan is null)
            return null;

        return new DisplaySnapshot(repair.Plan.TargetMonitors.ToArray());
    }

    public static string CanvasRefusalMessage(DisplayGeometryIssueKind kind) => kind switch
    {
        DisplayGeometryIssueKind.Overlap => "The preview could not be drawn because two displays are stacked on top of each other. Change the staged arrangement or pick another mode.",
        _ => "The preview could not be drawn because no display is at the desktop corner (0, 0). Change the staged arrangement or pick another mode."
    };

    public static string CanvasPickerRefusalMessage(DisplayGeometryIssueKind kind) => kind switch
    {
        DisplayGeometryIssueKind.Overlap => "The preview could not be drawn because the new size stacks two displays on top of each other. The arrangement may be adjusted when the change is applied, or pick another mode.",
        _ => "The preview could not be drawn because the new size leaves no display at the desktop corner (0, 0). The arrangement may be adjusted when the change is applied, or pick another mode."
    };

    public static bool ShouldRevertAfterApplyFault(bool temporaryApplied, bool liveMatchesBaseline) =>
        temporaryApplied || !liveMatchesBaseline;

    public void ReplaceLatestPlan(DisplayApplyPlan repaired, DisplayApplyPlan? stageRestorePlan)
    {
        ArgumentNullException.ThrowIfNull(repaired);

        LatestPlan = repaired;
        StageRestorePlan = stageRestorePlan;
    }

    public static string ScaleStagedConflictMessage() =>
        "Apply or cancel the staged arrangement first. Changing the scale now would discard that scale change.";

    public static string ModePickerStagedConflictMessage() =>
        "Apply or cancel the staged arrangement first. Changing this setting now would also apply those staged moves.";

    public static string StaleCountdownMessage(bool stateReadable) => stateReadable
        ? "The display settings no longer match what was applied, and the cause is unknown, so the automatic revert was not attempted. Nothing was reverted."
        : "Couldn't read the current display state, so the automatic revert was not attempted. Nothing was reverted.";

    public static string ScaleBusyMessage() =>
        "A change is in progress. Wait for it to finish, then try again.";

    public static string StageMatchesLiveMessage() =>
        "The staged arrangement already matches the screen. Cleared. Nothing was applied.";

    public static bool ShouldConsumeMatchingStage(DisplayApplyPlan? plan, DisplaySnapshot? live, bool hasPositionStage, bool hasPrimaryStage) =>
        (hasPositionStage || hasPrimaryStage) && plan is not null && live is not null && TargetsEqual(plan.TargetMonitors, live.Monitors);

    public static string ScaleApplyReadFailureMessage(bool confirmedApplied) => confirmedApplied
        ? "The scale change was applied, but the follow-up check failed. Keep it or revert it below."
        : "Couldn't confirm the scale change. The settings on screen may be out of date. Change the scale again to try.";

    public static string StageDiscardedForTopologyMessage() =>
        "The set of displays changed, so the staged arrangement was discarded. Nothing changed.";

    public static string ApplyCatchRevertedMessage() =>
        "Couldn't apply those display settings. Reverted to your previous setup.";

    public static bool ShouldClearStatusForCountdown(DisplayNoticeKind kind) => kind == DisplayNoticeKind.Staged;

    public static ScaleApplyRecovery ResolveScaleApplyRecovery(bool tripleSuccess, int curStep, int requestedStep, int previousStep)
    {
        if (!tripleSuccess)
            return ScaleApplyRecovery.Unconfirmed;

        if (curStep == requestedStep)
            return ScaleApplyRecovery.OfferRevert;

        if (curStep == previousStep)
            return ScaleApplyRecovery.NothingChanged;

        return ScaleApplyRecovery.Unconfirmed;
    }

    public static bool LiveMatchesAppliedState(DisplaySnapshot? live, DisplaySnapshot? applied)
    {
        if (live is null || applied is null)
            return false;

        if (live.Monitors.Count != applied.Monitors.Count)
            return false;

        var liveByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in live.Monitors)
        {
            if (!liveByKey.TryAdd(monitor.GdiDeviceName, monitor))
                return false;
        }

        foreach (var monitor in applied.Monitors)
        {
            if (!liveByKey.TryGetValue(monitor.GdiDeviceName, out var current))
                return false;

            if (current.PositionX != monitor.PositionX ||
                current.PositionY != monitor.PositionY ||
                current.PixelWidth != monitor.PixelWidth ||
                current.PixelHeight != monitor.PixelHeight ||
                current.RefreshNumerator != monitor.RefreshNumerator ||
                current.RefreshDenominator != monitor.RefreshDenominator ||
                current.Rotation != monitor.Rotation)
                return false;
        }

        return true;
    }

    public static bool ShouldRevertScale(bool scaleRevertArmed, bool hasRevertFailed, string? scaleMonitorKey) =>
        scaleRevertArmed || (hasRevertFailed && !string.IsNullOrWhiteSpace(scaleMonitorKey));

    public static bool ShouldShowBannerForNotice(DisplayNoticeKind kind) => kind != DisplayNoticeKind.Staged;

    public static DisplayBannerSeverity SeverityForNotice(DisplayNoticeKind kind) => kind switch
    {
        DisplayNoticeKind.Saved => DisplayBannerSeverity.Success,
        DisplayNoticeKind.Staged => DisplayBannerSeverity.Success,
        DisplayNoticeKind.Refusal => DisplayBannerSeverity.Error,
        DisplayNoticeKind.Failure => DisplayBannerSeverity.Error,
        _ => DisplayBannerSeverity.Informational
    };

    public static string CountdownMessage(int remaining) => remaining == 1
        ? "Reverting in 1 second"
        : $"Reverting in {remaining} seconds";

    public static bool ShouldAnnounceCountdown(int remaining, bool alreadyAnnounced) =>
        !alreadyAnnounced && remaining <= 3;

    public static string ScaleKeepMessage(bool readSucceeded, bool stepMatches)
    {
        if (!readSucceeded)
            return "Couldn't confirm the scale change. Change it again to try.";

        return stepMatches
            ? "Scale change confirmed on screen."
            : "The scale change was kept, but the current scale is not the requested step. Change it again to try.";
    }

    public static string ScaleRevertMismatchMessage(bool readSucceeded) => readSucceeded
        ? "The scale was reverted, but the current scale is not the previous step. Change it again to try."
        : "Couldn't confirm the scale revert. Change it again to try.";

    public static DisplayModeRepairOutcome RepairModePlan(DisplaySnapshot? baseline, DisplayApplyPlan? plan)
    {
        var emptyKeys = Array.Empty<string>();
        if (plan is null)
            return new DisplayModeRepairOutcome(DisplayModeRepairKind.NotNeeded, null, emptyKeys, string.Empty, null);

        if (baseline is null || !SameKeys(baseline, plan))
            return new DisplayModeRepairOutcome(DisplayModeRepairKind.NotNeeded, plan, emptyKeys, string.Empty, null);

        var planIssues = DisplayGeometry.BlockingIssues(new DisplaySnapshot(plan.TargetMonitors));
        if (planIssues.Count == 0)
            return new DisplayModeRepairOutcome(DisplayModeRepairKind.NotNeeded, plan, emptyKeys, string.Empty, null);

        if (TargetsEqual(plan.TargetMonitors, baseline.Monitors))
            return new DisplayModeRepairOutcome(DisplayModeRepairKind.NotNeeded, plan, emptyKeys, string.Empty, null);

        var changed = ChangedSizeOrRotationKeys(baseline, plan);

        if (TrySeatingRepair(baseline, plan, changed, out var seated, out var seatedMoved))
            return new DisplayModeRepairOutcome(
                DisplayModeRepairKind.Repaired,
                seated,
                seatedMoved,
                ModeRepairAnnouncement(seated!.TargetMonitors, seatedMoved, baseline, plan),
                null);

        var issue = planIssues[0];
        return new DisplayModeRepairOutcome(
            DisplayModeRepairKind.Unrepairable,
            plan,
            emptyKeys,
            string.Empty,
            ModeRepairRefusal(baseline, plan, issue));
    }

    private static bool SameKeys(DisplaySnapshot baseline, DisplayApplyPlan plan)
    {
        if (baseline.Monitors.Count != plan.TargetMonitors.Count)
            return false;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in baseline.Monitors)
        {
            if (!keys.Add(monitor.GdiDeviceName))
                return false;
        }

        foreach (var target in plan.TargetMonitors)
        {
            if (!keys.Contains(target.GdiDeviceName))
                return false;
        }

        return true;
    }

    private static HashSet<string> ChangedSizeOrRotationKeys(DisplaySnapshot baseline, DisplayApplyPlan plan)
    {
        var liveByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in baseline.Monitors)
            liveByKey[monitor.GdiDeviceName] = monitor;

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.TargetMonitors)
        {
            if (!liveByKey.TryGetValue(target.GdiDeviceName, out var live))
                continue;

            if (live.PixelWidth != target.PixelWidth ||
                live.PixelHeight != target.PixelHeight ||
                live.Rotation != target.Rotation)
                changed.Add(target.GdiDeviceName);
        }

        return changed;
    }

    private static bool TrySeatingRepair(
        DisplaySnapshot baseline,
        DisplayApplyPlan plan,
        HashSet<string> changedKeys,
        out DisplayApplyPlan? repaired,
        out IReadOnlyList<string> movedKeys)
    {
        repaired = null;
        movedKeys = Array.Empty<string>();

        var planByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.TargetMonitors)
            planByKey[target.GdiDeviceName] = target;

        var pinned = new HashSet<string>(changedKeys, StringComparer.OrdinalIgnoreCase);
        var origin = plan.TargetMonitors.FirstOrDefault(monitor => monitor.PositionX == 0 && monitor.PositionY == 0);
        if (origin is not null)
            pinned.Add(origin.GdiDeviceName);

        var seated = new List<DisplayMonitor>(baseline.Monitors.Count);
        foreach (var monitor in baseline.Monitors)
        {
            if (pinned.Contains(monitor.GdiDeviceName) && planByKey.TryGetValue(monitor.GdiDeviceName, out var target))
                seated.Add(target);
        }

        var prefixIssues = DisplayGeometry.Validate(new DisplaySnapshot(seated.ToArray()));
        if (prefixIssues.Count != 0)
            return TrySeatChangedDisplay(baseline, plan, planByKey, changedKeys, origin, pinned, prefixIssues, out repaired, out movedKeys);

        return SeatRemainingAndCompose(baseline, plan, planByKey, pinned, seated, out repaired, out movedKeys);
    }

    private static bool TrySeatChangedDisplay(
        DisplaySnapshot baseline,
        DisplayApplyPlan plan,
        Dictionary<string, DisplayMonitor> planByKey,
        HashSet<string> changedKeys,
        DisplayMonitor? origin,
        HashSet<string> pinned,
        IReadOnlyList<DisplayGeometryIssue> prefixIssues,
        out DisplayApplyPlan? repaired,
        out IReadOnlyList<string> movedKeys)
    {
        repaired = null;
        movedKeys = Array.Empty<string>();

        if (!prefixIssues.All(issue => issue.Kind == DisplayGeometryIssueKind.Gap) &&
            !prefixIssues.Any(issue => issue.Kind == DisplayGeometryIssueKind.Overlap))
            return false;

        var loosened = new HashSet<string>(pinned, StringComparer.OrdinalIgnoreCase);
        var loosenedAny = false;
        foreach (var monitor in baseline.Monitors)
        {
            if (!changedKeys.Contains(monitor.GdiDeviceName))
                continue;

            if (origin is not null && string.Equals(monitor.GdiDeviceName, origin.GdiDeviceName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!planByKey.ContainsKey(monitor.GdiDeviceName))
                continue;

            loosened.Remove(monitor.GdiDeviceName);
            loosenedAny = true;
        }

        if (!loosenedAny)
            return false;

        var reseated = new List<DisplayMonitor>(baseline.Monitors.Count);
        foreach (var monitor in baseline.Monitors)
        {
            if (loosened.Contains(monitor.GdiDeviceName) && planByKey.TryGetValue(monitor.GdiDeviceName, out var target))
                reseated.Add(target);
        }

        if (DisplayGeometry.Validate(new DisplaySnapshot(reseated.ToArray())).Count != 0)
            return false;

        return SeatRemainingAndCompose(baseline, plan, planByKey, loosened, reseated, out repaired, out movedKeys);
    }

    private static bool SeatRemainingAndCompose(
        DisplaySnapshot baseline,
        DisplayApplyPlan plan,
        Dictionary<string, DisplayMonitor> planByKey,
        HashSet<string> pinSet,
        List<DisplayMonitor> seated,
        out DisplayApplyPlan? repaired,
        out IReadOnlyList<string> movedKeys)
    {
        repaired = null;
        movedKeys = Array.Empty<string>();

        foreach (var monitor in baseline.Monitors)
        {
            if (pinSet.Contains(monitor.GdiDeviceName))
                continue;

            if (!planByKey.TryGetValue(monitor.GdiDeviceName, out var target))
                return false;

            var prefix = new List<DisplayMonitor>(seated) { target };
            var result = DisplayPlacementSolver.TrySolve(
                new DisplaySnapshot(prefix.ToArray()),
                target.GdiDeviceName,
                target.PositionX,
                target.PositionY,
                double.MaxValue);
            if (result.Reason != DisplayPlacementReason.Placed || result.Arrangement is null)
                return false;

            seated.Add(result.Arrangement.Monitors.First(candidate =>
                string.Equals(candidate.GdiDeviceName, target.GdiDeviceName, StringComparison.OrdinalIgnoreCase)));
        }

        var ordered = InPlanOrder(new DisplaySnapshot(seated.ToArray()), plan);
        if (DisplayGeometry.Validate(new DisplaySnapshot(ordered)).Count != 0)
            return false;

        repaired = new DisplayApplyPlan(ordered, plan.Changes);
        movedKeys = MovedKeysInBaselineOrder(baseline, plan, repaired);
        return true;
    }

    private static IReadOnlyList<DisplayMonitor> InPlanOrder(DisplaySnapshot arrangement, DisplayApplyPlan plan)
    {
        var byKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in arrangement.Monitors)
            byKey[monitor.GdiDeviceName] = monitor;

        var ordered = new List<DisplayMonitor>(plan.TargetMonitors.Count);
        foreach (var target in plan.TargetMonitors)
        {
            if (byKey.TryGetValue(target.GdiDeviceName, out var monitor))
                ordered.Add(monitor);
        }

        return ordered;
    }

    private static IReadOnlyList<string> MovedKeysInBaselineOrder(
        DisplaySnapshot baseline,
        DisplayApplyPlan plan,
        DisplayApplyPlan repaired)
    {
        var before = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.TargetMonitors)
            before[target.GdiDeviceName] = target;

        var after = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in repaired.TargetMonitors)
            after[target.GdiDeviceName] = target;

        var moved = new List<string>();
        foreach (var monitor in baseline.Monitors)
        {
            if (!before.TryGetValue(monitor.GdiDeviceName, out var oldPosition) ||
                !after.TryGetValue(monitor.GdiDeviceName, out var newPosition))
                continue;

            if (oldPosition.PositionX != newPosition.PositionX || oldPosition.PositionY != newPosition.PositionY)
                moved.Add(monitor.GdiDeviceName);
        }

        return moved;
    }

    private static string ModeRepairAnnouncement(
        IReadOnlyList<DisplayMonitor> repairedTargets,
        IReadOnlyList<string> movedKeys,
        DisplaySnapshot baseline,
        DisplayApplyPlan plan)
    {
        var byKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in repairedTargets)
            byKey[monitor.GdiDeviceName] = monitor;

        var names = new List<string>(movedKeys.Count);
        foreach (var key in movedKeys)
        {
            if (byKey.TryGetValue(key, out var monitor))
                names.Add($"Display {monitor.Ordinal}");
        }

        var subject = names.Count switch
        {
            0 => "A display",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
        };

        var changes = ChangedSettingsInBaselineOrder(baseline, plan);
        var changeClause = changes.Count switch
        {
            0 => "a display change",
            1 => changes[0],
            2 => $"{changes[0]} and {changes[1]}",
            _ => $"{string.Join(", ", changes.Take(changes.Count - 1))} and {changes[^1]}"
        };
        return $"{subject} moved to keep a valid layout after {changeClause}.";
    }

    private static IReadOnlyList<string> ChangedSettingsInBaselineOrder(DisplaySnapshot baseline, DisplayApplyPlan plan)
    {
        var planByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.TargetMonitors)
            planByKey[target.GdiDeviceName] = target;

        var changes = new List<string>();
        foreach (var monitor in baseline.Monitors)
        {
            if (!planByKey.TryGetValue(monitor.GdiDeviceName, out var target))
                continue;

            var sizeChanged = monitor.PixelWidth != target.PixelWidth || monitor.PixelHeight != target.PixelHeight;
            var rotationChanged = monitor.Rotation != target.Rotation;
            if (!sizeChanged && !rotationChanged)
                continue;

            if (sizeChanged && rotationChanged)
                changes.Add($"Display {target.Ordinal} changed resolution and orientation");
            else if (rotationChanged)
                changes.Add($"Display {target.Ordinal} changed orientation");
            else
                changes.Add($"Display {target.Ordinal} changed resolution");
        }

        return changes;
    }

    private static string ModeRepairRefusal(DisplaySnapshot baseline, DisplayApplyPlan plan, DisplayGeometryIssue issue)
    {
        var liveByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in baseline.Monitors)
            liveByKey[monitor.GdiDeviceName] = monitor;

        var setting = "resolution";
        var settingNoun = "resolution";
        foreach (var target in plan.TargetMonitors)
        {
            if (!liveByKey.TryGetValue(target.GdiDeviceName, out var live))
                continue;

            if (live.Rotation != target.Rotation)
            {
                setting = "orientation";
                settingNoun = "orientation";
                break;
            }
        }

        var first = $"Display {issue.First.Ordinal}";
        var second = $"Display {issue.Second.Ordinal}";
        if (issue.Kind == DisplayGeometryIssueKind.Origin)
            return $"That {setting} change leaves no display at the desktop corner (0, 0), so nothing was applied. Your previous {settingNoun} is still selected.";

        var tail = plan.TargetMonitors.Any(monitor => monitor.PositionX == 0 && monitor.PositionY == 0)
            ? $"This tool does not move the display at the desktop corner (0, 0), so nothing was applied and your previous {settingNoun} is still selected."
            : $"Nothing was applied and your previous {settingNoun} is still selected.";

        var axis = DisplayGeometry.AxisWord(issue.First, issue.Second);
        return $"That {setting} change would leave {first} and {second} overlapping {axis}. {tail}";
    }

    public static (bool IsTopology, string? Detail) DescribeMismatch(DisplayApplyPlan plan, DisplaySnapshot? readBack)
    {
        if (readBack is null)
            return (true, null);

        string? missing = null;
        var details = new List<string>();
        foreach (var target in plan.TargetMonitors)
        {
            var actual = readBack.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, target.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
            var name = target.DisplayLabel;
            if (actual is null)
            {
                missing ??= name;
                continue;
            }

            if (actual.PixelWidth != target.PixelWidth || actual.PixelHeight != target.PixelHeight)
                details.Add($"{name} is showing {actual.ResolutionText} instead of the requested {target.ResolutionText}");

            var refreshReported = false;
            if (!DisplayModeCatalog.SameRefreshRate(actual.RefreshHz, target.RefreshHz))
            {
                details.Add($"{name} is showing {actual.RefreshText} instead of the requested {target.RefreshText}");
                refreshReported = true;
            }

            if (actual.Rotation != target.Rotation)
                details.Add($"{name} is showing {actual.RotationText} instead of the requested {target.RotationText}");

            if (actual.ScalePercent != target.ScalePercent && actual.ScalePercent > 0 && target.ScalePercent > 0)
                details.Add($"{name} is showing scale {actual.ScalePercent}% instead of the requested {target.ScalePercent}%");

            if (actual.PositionX != target.PositionX || actual.PositionY != target.PositionY)
                details.Add($"{name} is showing position {actual.PositionText} instead of the requested {target.PositionText}");

            var nativeScanOut = actual.PixelWidth == target.PixelWidth &&
                actual.PixelHeight == target.PixelHeight &&
                actual.Rotation == target.Rotation &&
                actual.PreferredWidth > 0 && actual.PreferredHeight > 0 &&
                actual.TargetActiveWidth == actual.PreferredWidth &&
                actual.TargetActiveHeight == actual.PreferredHeight;

            if (!nativeScanOut &&
                (actual.TargetActiveWidth != target.TargetActiveWidth || actual.TargetActiveHeight != target.TargetActiveHeight) &&
                actual.TargetActiveWidth > 0 && actual.TargetActiveHeight > 0 &&
                target.TargetActiveWidth > 0 && target.TargetActiveHeight > 0)
                details.Add($"{name} is showing active size {actual.TargetActiveWidth} × {actual.TargetActiveHeight} instead of the requested {target.TargetActiveWidth} × {target.TargetActiveHeight}");

            if (RequestAuthoredTimings(target))
            {
                if ((actual.TargetTotalWidth != target.TargetTotalWidth || actual.TargetTotalHeight != target.TargetTotalHeight) &&
                    actual.TargetTotalWidth > 0 && actual.TargetTotalHeight > 0 &&
                    target.TargetTotalWidth > 0 && target.TargetTotalHeight > 0)
                    details.Add($"{name} is showing total size {actual.TargetTotalWidth} × {actual.TargetTotalHeight} instead of the requested {target.TargetTotalWidth} × {target.TargetTotalHeight}");

                if (actual.TargetPixelRate != target.TargetPixelRate && actual.TargetPixelRate > 0 && target.TargetPixelRate > 0)
                    details.Add($"{name} is showing pixel rate {actual.TargetPixelRate} instead of the requested {target.TargetPixelRate}");

                if (!SameHorizontalSync(actual, target) &&
                    actual.TargetHSyncNumerator > 0 && actual.TargetHSyncDenominator > 0 &&
                    target.TargetHSyncNumerator > 0 && target.TargetHSyncDenominator > 0)
                    details.Add($"{name} is showing horizontal sync {actual.TargetHSyncNumerator}/{actual.TargetHSyncDenominator} instead of the requested {target.TargetHSyncNumerator}/{target.TargetHSyncDenominator}");
            }

            var actualVSyncHz = actual.TargetVSyncDenominator == 0 ? 0 : actual.TargetVSyncNumerator / (double)actual.TargetVSyncDenominator;
            var requestedVSyncHz = target.TargetVSyncDenominator == 0 ? 0 : target.TargetVSyncNumerator / (double)target.TargetVSyncDenominator;
            if (!refreshReported &&
                !DisplayModeCatalog.SameRefreshRate(actualVSyncHz, requestedVSyncHz) &&
                actual.TargetVSyncNumerator > 0 && actual.TargetVSyncDenominator > 0 &&
                target.TargetVSyncNumerator > 0 && target.TargetVSyncDenominator > 0)
                details.Add($"{name} is showing vertical sync {actualVSyncHz:0.###} Hz instead of the requested {requestedVSyncHz:0.###} Hz");
        }

        if (missing is not null)
            return (true, missing);

        return (false, details.Count == 0 ? null : string.Join("; ", details));
    }

    public static bool ReadBackConfirmsMovedPositions(DisplayApplyPlan? plan, IReadOnlyList<string>? movedKeys, DisplaySnapshot? readBack)
    {
        if (plan is null || movedKeys is null || readBack is null)
            return false;

        foreach (var key in movedKeys)
        {
            var target = plan.TargetMonitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, key, StringComparison.OrdinalIgnoreCase));
            var actual = readBack.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, key, StringComparison.OrdinalIgnoreCase));
            if (target is null || actual is null)
                return false;

            if (actual.PositionX != target.PositionX || actual.PositionY != target.PositionY)
                return false;
        }

        return true;
    }

    public static string MismatchAdjustmentMessage(string mismatchDetail) =>
        $"That change needed a small adjustment to apply ({mismatchDetail}). Keep it if it looks right, or let it revert.";

    private const double HorizontalSyncToleranceHz = 1.0;

    private static bool SameHorizontalSync(DisplayMonitor actual, DisplayMonitor target)
    {
        var actualHz = actual.TargetHSyncDenominator == 0
            ? 0
            : actual.TargetHSyncNumerator / (double)actual.TargetHSyncDenominator;
        var requestedHz = target.TargetHSyncDenominator == 0
            ? 0
            : target.TargetHSyncNumerator / (double)target.TargetHSyncDenominator;
        return Math.Abs(actualHz - requestedHz) <= HorizontalSyncToleranceHz;
    }

    private static bool RequestAuthoredTimings(DisplayMonitor target)
    {
        if (target.TargetPixelRate == 0 || target.TargetTotalWidth == 0 || target.TargetTotalHeight == 0)
            return false;

        var vsyncHz = target.TargetVSyncDenominator == 0
            ? 0
            : target.TargetVSyncNumerator / (double)target.TargetVSyncDenominator;
        if (vsyncHz <= 0)
            return false;

        var impliedHz = target.TargetPixelRate / (double)((ulong)target.TargetTotalWidth * target.TargetTotalHeight);
        return DisplayModeCatalog.SameRefreshRate(impliedHz, vsyncHz);
    }

    public static DisplaySnapshot? ResolveCountdownCanvas(DisplayApplyPlan plan, DisplaySnapshot? baseline, DisplaySnapshot? readBack, string? mismatchDetail)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (mismatchDetail is null || readBack is null)
            return null;

        if (!PlanCarriesModeChange(plan, baseline))
            return null;

        foreach (var target in plan.TargetMonitors)
        {
            var actual = readBack.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, target.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
                return null;

            if (actual.PixelWidth != target.PixelWidth ||
                actual.PixelHeight != target.PixelHeight ||
                actual.Rotation != target.Rotation ||
                actual.PositionX != target.PositionX ||
                actual.PositionY != target.PositionY)
                return readBack;
        }

        return null;
    }

    public static string ArrangementKeepMessage(bool keepTopology, string? keepDetail)
    {
        if (keepDetail is null)
            return keepTopology
                ? "Saved, but the new arrangement could not be read back. Drag again and press Apply to retry."
                : "Arrangement applied.";
        if (keepTopology)
            return $"Saved, but {keepDetail} is no longer showing up. Drag again and press Apply to retry.";
        if (keepDetail.Contains("showing scale ", StringComparison.Ordinal))
            return $"Saved, but {keepDetail}. Change it again from the controls above to retry.";
        return $"Saved, but {keepDetail}. Drag again and press Apply to retry.";
    }

    public static string RepairedApplyStatus(string? mismatchDetail, string repairAnnouncement, bool movedConfirmed)
    {
        if (mismatchDetail is null)
            return repairAnnouncement;

        if (!movedConfirmed)
            return MismatchAdjustmentMessage(mismatchDetail);

        return $"{MismatchAdjustmentMessage(mismatchDetail)} {repairAnnouncement}";
    }

    public static string TopologyMessage(string? mismatchDetail, bool revertSucceeded, bool stageRestored)
    {
        string preview = string.IsNullOrEmpty(mismatchDetail)
            ? "Couldn't preview that mode."
            : $"Couldn't preview that mode ({mismatchDetail} is no longer showing up).";
        string revertClause = revertSucceeded
            ? "Reverted to your previous setup."
            : "Could not revert to your previous setup.";
        if (stageRestored)
            return $"{preview} {revertClause} Your staged arrangement is still there. Review it and press Apply to try again.";
        return $"{preview} {revertClause}";
    }

    public static bool PlanCarriesModeChange(DisplayApplyPlan? plan, DisplaySnapshot? baseline)
    {
        if (plan is null || baseline is null)
            return true;

        if (plan.TargetMonitors.Count != baseline.Monitors.Count)
            return true;

        foreach (var target in plan.TargetMonitors)
        {
            var live = baseline.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, target.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
            if (live is null)
                return true;

            if (live.PixelWidth != target.PixelWidth ||
                live.PixelHeight != target.PixelHeight ||
                live.RefreshNumerator != target.RefreshNumerator ||
                live.RefreshDenominator != target.RefreshDenominator ||
                live.Rotation != target.Rotation)
                return true;
        }

        return false;
    }

    public bool IsNoOp(DisplayApplyPlan plan, DisplaySnapshot? current)
    {
        if (plan is null)
            return true;

        if (LatestPlan is not null && PlansEqual(plan, LatestPlan))
            return true;

        if (IsRefusedReapply(plan))
            return true;

        return !IsChainActive && current is not null && TargetsEqual(plan.TargetMonitors, current.Monitors);
    }

    public bool IsRefusedReapply(DisplayApplyPlan? plan) =>
        !IsChainActive && plan is not null && _refusedPlan is not null && PlansEqual(plan, _refusedPlan);

    public void NoteLiveSnapshot(DisplaySnapshot? live)
    {
        if (_refusedPlan is null || live is null || _refusedBaseline is null)
            return;

        if (TargetsEqual(_refusedBaseline.Monitors, live.Monitors))
            return;

        _refusedPlan = null;
        _refusedBaseline = null;
    }

    public static bool PlansEqual(DisplayApplyPlan? first, DisplayApplyPlan? second)
    {
        if (ReferenceEquals(first, second))
            return true;

        if (first is null || second is null)
            return false;

        return TargetsEqual(first.TargetMonitors, second.TargetMonitors);
    }

    public static bool TargetsEqual(IReadOnlyList<DisplayMonitor>? first, IReadOnlyList<DisplayMonitor>? second)
    {
        if (ReferenceEquals(first, second))
            return true;

        if (first is null || second is null || first.Count != second.Count)
            return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (!Equals(first[i], second[i]))
                return false;
        }

        return true;
    }
}
