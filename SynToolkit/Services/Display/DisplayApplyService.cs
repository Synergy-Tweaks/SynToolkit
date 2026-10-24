#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public sealed record DisplayRevertFaultOutcome(string Message, bool HasRevertFailed);

public sealed record DisplayExportMonitor(
    int Ordinal,
    string GdiDeviceName,
    string FriendlyName,
    uint PixelWidth,
    uint PixelHeight,
    uint RefreshNumerator,
    uint RefreshDenominator,
    uint Rotation,
    int PositionX,
    int PositionY,
    bool IsPrimary,
    int ScalePercent);

public sealed record DisplayExportRecord(string? Outcome, DisplayExportMonitor[]? Monitors);

public static class DisplayApplyService
{
    internal const string ExportOutcomePending = "pending";
    internal const string ExportOutcomeKept = "kept";
    internal const string ExportOutcomeReverted = "reverted";
    internal const string ExportOutcomeFailed = "failed";
    internal const string ExportOutcomeAbandoned = "abandoned";

    internal static string ExportDirectoryPath => ExportDirectory;
    private static readonly string ExportDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "SynToolkit",
        "Exports");

    public static Task<DisplayApplyResult> ValidateAsync(DisplayApplyPlan plan) =>
        Task.Run(() => ApplyInternal(plan, persist: false, validateOnly: true));

    public static Task<DisplayApplyResult> ApplyTemporaryAsync(DisplayApplyPlan plan) =>
        Task.Run(() => ApplyInternal(plan, persist: false, validateOnly: false));

    public static Task<DisplayApplyResult> PersistAsync(DisplayApplyPlan plan) =>
        Task.Run(() => ApplyInternal(plan, persist: true, validateOnly: false));

    public static Task<DisplayApplyResult> RevertToDatabaseAsync(DisplaySnapshot? baseline) =>
        Task.Run(() => RevertToDatabase(baseline));

    internal static DisplayNative.DisplayConfigVideoSignalInfo ComposeTargetSignal(
        DisplayNative.DisplayConfigVideoSignalInfo live,
        uint pixelWidth,
        uint pixelHeight,
        DisplayNative.DisplayConfigRational refresh,
        uint liveRotation,
        uint targetRotation)
    {
        if (DisplayModeCatalog.SwapsDimensions(liveRotation) != DisplayModeCatalog.SwapsDimensions(targetRotation))
        {
            live.ActiveSize = new DisplayNative.DisplayConfig2DRegion { Cx = pixelWidth, Cy = pixelHeight };
            live.VSyncFreq = refresh;
            var total = live.TotalSize;
            live.TotalSize = new DisplayNative.DisplayConfig2DRegion { Cx = total.Cy, Cy = total.Cx };
            var quotient = (double)live.PixelRate / live.TotalSize.Cx;
            if (quotient <= uint.MaxValue)
                live.HSyncFreq = new DisplayNative.DisplayConfigRational
                {
                    Numerator = (uint)Math.Round(quotient, MidpointRounding.AwayFromZero),
                    Denominator = live.HSyncFreq.Denominator
                };

            return live;
        }

        live.ActiveSize = new DisplayNative.DisplayConfig2DRegion { Cx = pixelWidth, Cy = pixelHeight };
        live.VSyncFreq = refresh;
        return live;
    }

    internal static DisplayNative.DisplayConfigRational SelectRequestRefresh(
        uint targetNumerator,
        uint targetDenominator,
        uint liveNumerator,
        uint liveDenominator)
    {
        var requestedHz = targetDenominator == 0 ? 0 : targetNumerator / (double)targetDenominator;
        var liveHz = liveDenominator == 0 ? 0 : liveNumerator / (double)liveDenominator;
        if (!DisplayModeCatalog.SameRefreshRate(requestedHz, liveHz))
        {
            return new DisplayNative.DisplayConfigRational
            {
                Numerator = targetNumerator,
                Denominator = targetDenominator == 0 ? 1u : targetDenominator
            };
        }

        return new DisplayNative.DisplayConfigRational
        {
            Numerator = liveNumerator,
            Denominator = liveDenominator
        };
    }

    internal static DisplayNative.DisplayConfigVideoSignalInfo TargetSignalOf(DisplayMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return new DisplayNative.DisplayConfigVideoSignalInfo
        {
            PixelRate = monitor.TargetPixelRate,
            HSyncFreq = new DisplayNative.DisplayConfigRational
            {
                Numerator = monitor.TargetHSyncNumerator,
                Denominator = monitor.TargetHSyncDenominator
            },
            VSyncFreq = new DisplayNative.DisplayConfigRational
            {
                Numerator = monitor.TargetVSyncNumerator,
                Denominator = monitor.TargetVSyncDenominator
            },
            ActiveSize = new DisplayNative.DisplayConfig2DRegion
            {
                Cx = monitor.TargetActiveWidth,
                Cy = monitor.TargetActiveHeight
            },
            TotalSize = new DisplayNative.DisplayConfig2DRegion
            {
                Cx = monitor.TargetTotalWidth,
                Cy = monitor.TargetTotalHeight
            }
        };
    }

    internal static DisplayMonitor WithTargetSignal(DisplayMonitor monitor, DisplayNative.DisplayConfigVideoSignalInfo signal)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return monitor with
        {
            TargetActiveWidth = signal.ActiveSize.Cx,
            TargetActiveHeight = signal.ActiveSize.Cy,
            TargetTotalWidth = signal.TotalSize.Cx,
            TargetTotalHeight = signal.TotalSize.Cy,
            TargetPixelRate = signal.PixelRate,
            TargetHSyncNumerator = signal.HSyncFreq.Numerator,
            TargetHSyncDenominator = signal.HSyncFreq.Denominator,
            TargetVSyncNumerator = signal.VSyncFreq.Numerator,
            TargetVSyncDenominator = signal.VSyncFreq.Denominator
        };
    }

    internal static bool ComposeModeWrites(
        DisplayNative.DisplayConfigModeInfo[] modes,
        DisplayNative.DisplayConfigPathInfo path,
        DisplayMonitor target,
        uint liveRotation)
    {
        ArgumentNullException.ThrowIfNull(modes);
        ArgumentNullException.ThrowIfNull(target);

        var sourceIdx = path.SourceInfo.ModeInfoIdx;
        if (sourceIdx == DisplayNative.DisplayConfigPathModeIdxInvalid || sourceIdx >= modes.Length)
            return false;

        var targetIdx = path.TargetInfo.ModeInfoIdx;
        if (targetIdx == DisplayNative.DisplayConfigPathModeIdxInvalid || targetIdx >= modes.Length)
            return false;

        var mode = modes[sourceIdx];
        var source = mode.Mode.SourceMode;
        source.Width = target.PixelWidth;
        source.Height = target.PixelHeight;
        source.PositionX = target.PositionX;
        source.PositionY = target.PositionY;
        mode.Mode.SourceMode = source;
        modes[sourceIdx] = mode;

        mode = modes[targetIdx];
        mode.Mode.TargetMode.TargetVideoSignalInfo = ComposeTargetSignal(
            mode.Mode.TargetMode.TargetVideoSignalInfo,
            target.PixelWidth,
            target.PixelHeight,
            path.TargetInfo.RefreshRate,
            liveRotation,
            target.Rotation);
        modes[targetIdx] = mode;
        return true;
    }

    internal static bool ComposePositionWrites(
        DisplayNative.DisplayConfigModeInfo[] modes,
        DisplayNative.DisplayConfigPathInfo path,
        DisplayMonitor target)
    {
        ArgumentNullException.ThrowIfNull(modes);
        ArgumentNullException.ThrowIfNull(target);

        var sourceIdx = path.SourceInfo.ModeInfoIdx;
        if (sourceIdx == DisplayNative.DisplayConfigPathModeIdxInvalid || sourceIdx >= modes.Length)
            return false;

        var mode = modes[sourceIdx];
        if (mode.InfoType != DisplayNative.DisplayConfigModeInfoTypeSource)
            return false;

        var source = mode.Mode.SourceMode;
        source.PositionX = target.PositionX;
        source.PositionY = target.PositionY;
        mode.Mode.SourceMode = source;
        modes[sourceIdx] = mode;
        return true;
    }

    internal static bool PositionsMatch(DisplayApplyPlan plan, DisplaySnapshot? readBack)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (readBack is null)
            return true;

        foreach (var target in plan.TargetMonitors)
        {
            var actual = readBack.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.GdiDeviceName, target.GdiDeviceName, StringComparison.OrdinalIgnoreCase));
            if (actual is null)
                continue;

            if (actual.PositionX != target.PositionX || actual.PositionY != target.PositionY)
                return false;
        }

        return true;
    }

    internal static DisplayMonitor ComposeSelectedMonitorMode(
        DisplayMonitor monitor,
        DisplayPickedMode picked)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(picked);

        return ComposeSelectedMonitorMode(
            monitor,
            () => picked.Resolution,
            () => picked.Refresh,
            () => picked.Orientation);
    }

    internal static DisplayMonitor ComposeSelectedMonitorMode(
        DisplayMonitor monitor,
        Func<DisplayResolutionOption?> resolutionReader,
        Func<DisplayRefreshOption?> refreshReader,
        Func<DisplayOrientationOption?> orientationReader)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(resolutionReader);
        ArgumentNullException.ThrowIfNull(refreshReader);
        ArgumentNullException.ThrowIfNull(orientationReader);

        var resolution = resolutionReader();
        var refresh = refreshReader();
        var orientation = orientationReader();
        if (resolution is null || refresh is null || orientation is null)
            return monitor;

        var (effectiveWidth, effectiveHeight) = DisplayModeCatalog.EffectiveModeSize(
            resolution.Width,
            resolution.Height,
            orientation.CcdRotation);
        var (refreshNumerator, refreshDenominator) = DisplayModeCatalog.ResolveRefreshRational(
            monitor,
            resolution.Width,
            resolution.Height,
            refresh.RefreshHz);
        var working = monitor.WithMode(
            effectiveWidth,
            effectiveHeight,
            refreshNumerator,
            refreshDenominator,
            orientation.CcdRotation);
        var requestRefresh = SelectRequestRefresh(
            refreshNumerator,
            refreshDenominator,
            monitor.RefreshNumerator,
            monitor.RefreshDenominator);
        return WithTargetSignal(
            working,
            ComposeTargetSignal(
                TargetSignalOf(monitor),
                effectiveWidth,
                effectiveHeight,
                requestRefresh,
                monitor.Rotation,
                orientation.CcdRotation));
    }

    public static uint BuildFlags(bool validateOnly, bool persist)
    {
        if (validateOnly)
            return DisplayNative.SdcValidate | DisplayNative.SdcUseSuppliedDisplayConfig | DisplayNative.SdcAllowChanges;

        var flags = DisplayNative.SdcApply | DisplayNative.SdcUseSuppliedDisplayConfig | DisplayNative.SdcAllowChanges;
        if (persist)
            flags |= DisplayNative.SdcSaveToDatabase;

        return flags;
    }

    public static string NextAvailableExportPath(string directory, DateTime timestamp)
    {
        var candidate = Path.Combine(directory, $"display-{timestamp:yyyyMMdd-HHmmss-fff}.json");
        for (var attempt = 2; File.Exists(candidate); attempt++)
            candidate = Path.Combine(directory, $"display-{timestamp:yyyyMMdd-HHmmss-fff}-{attempt}.json");
        return candidate;
    }

    public static string ExportSnapshot(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ExportSnapshotTo(ExportDirectory, snapshot);
    }

    internal static string ExportSnapshotTo(string directory, DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Directory.CreateDirectory(directory);
        var path = NextAvailableExportPath(directory, DateTime.Now);
        var payload = new DisplayExportRecord(
            ExportOutcomePending,
            snapshot.Monitors.Select(monitor => new DisplayExportMonitor(
                monitor.Ordinal,
                monitor.GdiDeviceName,
                monitor.FriendlyName,
                monitor.PixelWidth,
                monitor.PixelHeight,
                monitor.RefreshNumerator,
                monitor.RefreshDenominator,
                monitor.Rotation,
                monitor.PositionX,
                monitor.PositionY,
                monitor.IsPrimary,
                monitor.ScalePercent)).ToArray());
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    internal static bool TryStampExportOutcome(string path, string outcome)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!document.RootElement.TryGetProperty("Monitors", out var monitorsElement) ||
                monitorsElement.ValueKind != JsonValueKind.Array)
                return false;

            File.WriteAllText(path, $"{{\"Outcome\":{JsonSerializer.Serialize(outcome)},\"Monitors\":{monitorsElement.GetRawText()}}}");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ValidateFailureMessage(string displayText, string modeText) =>
        $"That mode didn't validate ({displayText}, {modeText}). Nothing changed. Re-apply from the pickers to try again; if it repeats, that mode isn't reachable on this machine right now.";

    public static string PersistFailureMessage() =>
        "Couldn't save those settings. Reverting to your previous setup…";

    public static string PreviewFailureMessage(string previewDisplayText) =>
        $"Couldn't preview that mode ({previewDisplayText}). Nothing on screen changed. Your settings are as they were. Try again or pick another mode.";

    public static string ValidationSuccessMessage() =>
        "Configuration validated.";

    public static string ApplySuccessMessage(bool persist) =>
        persist ? "Display settings saved." : "Display settings applied temporarily.";

    public static string NoActivePathsMessage() =>
        "No active display paths were found.";

    public static string InactivePathMessage(string displayLabel) =>
        $"\"{displayLabel}\" is no longer an active display path; apply aborted.";

    public static string RevertFailureMessage() =>
        "Couldn't revert. Press Revert again to try.";

    public static string RevertRefreshFailureMessage() =>
        "Reverted, but the display list couldn't be updated. It may show out-of-date values.";

    public static string RevertRedrawFailureMessage() =>
        "Reverted, but the display preview couldn't be redrawn. It may look out of date.";

    public static string RevertUnfinishedMessage() =>
        "Couldn't finish reverting. The display list may be out of date. Press Revert again to try.";

    public static string RevertFaultMessage(bool revertSucceeded) =>
        RevertFaultOutcome(revertSucceeded, listUpdated: false).Message;

    public static DisplayRevertFaultOutcome RevertFaultOutcome(bool revertSucceeded, bool listUpdated)
    {
        if (!revertSucceeded)
            return new DisplayRevertFaultOutcome(RevertFailureMessage(), true);

        return listUpdated
            ? new DisplayRevertFaultOutcome(RevertRedrawFailureMessage(), false)
            : new DisplayRevertFaultOutcome(RevertRefreshFailureMessage(), false);
    }

    public static bool IsPostRevertNotice(string? message) =>
        string.Equals(message, RevertFaultOutcome(revertSucceeded: true, listUpdated: false).Message, StringComparison.Ordinal) ||
        string.Equals(message, RevertFaultOutcome(revertSucceeded: true, listUpdated: true).Message, StringComparison.Ordinal);

    public static string RevertSuccessMessage() =>
        "Reverted to the last saved display configuration.";

    public static string RevertReappliedMessage() =>
        "Reverted; the live configuration was re-applied from the pre-apply snapshot.";

    public static string RevertPartialMessage(string detail) =>
        $"Reverted to database, but the live configuration still differs ({detail}).";

    private static DisplayApplyResult ApplyInternal(DisplayApplyPlan plan, bool persist, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var target in plan.TargetMonitors)
        {
            if (string.IsNullOrWhiteSpace(target.GdiDeviceName))
                return new DisplayApplyResult(false, "A display in the apply plan has no device name; refusing a partial apply.", null);
        }

        var targetsByGdi = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.TargetMonitors)
        {
            if (!targetsByGdi.TryAdd(target.GdiDeviceName, target))
                return new DisplayApplyResult(false, "Duplicate display names in the apply plan; refusing a partial apply.", null);
        }

        for (var i = 0; i < plan.TargetMonitors.Count; i++)
        {
            for (var j = i + 1; j < plan.TargetMonitors.Count; j++)
            {
                if (plan.TargetMonitors[i].PositionX == plan.TargetMonitors[j].PositionX &&
                    plan.TargetMonitors[i].PositionY == plan.TargetMonitors[j].PositionY)
                    return new DisplayApplyResult(false, "Two displays are stacked in exactly the same spot. Drag one aside, then apply again.", null);
            }
        }

        if (plan.TargetMonitors.Count > 1 && !plan.TargetMonitors.Any(target => target.PositionX == 0 && target.PositionY == 0))
            return new DisplayApplyResult(false, "One display has to stay at the desktop's top-left corner (0, 0); your arrangement leaves it empty. Drag a display back to that corner, then apply again.", null);

        foreach (var target in plan.TargetMonitors)
        {
            var incoherent = DisplayModeCatalog.IncoherentModeMessage(target.PixelWidth, target.PixelHeight, target.Rotation);
            if (incoherent is not null)
                return new DisplayApplyResult(false, incoherent, null);
        }

        var (paths, modes) = DisplayService.QueryActivePaths();
        if (paths.Length == 0)
            return new DisplayApplyResult(false, NoActivePathsMessage(), null);

        var gdiBySource = BuildSourceNameMap(paths);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            if ((path.Flags & DisplayNative.DisplayConfigPathActive) == 0)
                continue;

            if (!gdiBySource.TryGetValue(SourceKey(path.SourceInfo), out var gdiName))
                continue;

            if (!targetsByGdi.TryGetValue(gdiName, out var target))
                continue;

            matched.Add(gdiName);
            var liveRotation = path.TargetInfo.Rotation;
            path.TargetInfo.Rotation = target.Rotation;
            path.TargetInfo.RefreshRate = SelectRequestRefresh(
                target.RefreshNumerator,
                target.RefreshDenominator,
                path.TargetInfo.RefreshRate.Numerator,
                path.TargetInfo.RefreshRate.Denominator);

            if (!ComposeModeWrites(modes, path, target, liveRotation))
                return new DisplayApplyResult(false, "Could not stage the display mode: an internal mode index was invalid. No changes were applied.", null);

            paths[i] = path;
        }

        foreach (var target in plan.TargetMonitors)
        {
            if (!matched.Contains(target.GdiDeviceName))
                return new DisplayApplyResult(false, InactivePathMessage(target.DisplayLabel), null);
        }

        var flags = BuildFlags(validateOnly, persist);

        var result = DisplayNative.SetDisplayConfig(
            (uint)paths.Length,
            paths,
            (uint)modes.Length,
            modes,
            flags);

        if (result != DisplayNative.ErrorSuccess)
        {
            if (validateOnly)
            {
                var displayText = string.Join(", ", plan.TargetMonitors.Select(target => target.DisplayLabel));
                var modeText = string.Join(", ", plan.TargetMonitors.Select(target =>
                    $"{target.PixelWidth} × {target.PixelHeight}@{target.RefreshHz:0.#} Hz"));
                return new DisplayApplyResult(false, ValidateFailureMessage(displayText, modeText), null);
            }

            if (persist)
                return new DisplayApplyResult(false, PersistFailureMessage(), null);

            var previewDisplayText = string.Join(", ", plan.TargetMonitors.Select(target => target.DisplayLabel));
            return new DisplayApplyResult(false, PreviewFailureMessage(previewDisplayText), null);
        }

        if (validateOnly)
            return new DisplayApplyResult(true, ValidationSuccessMessage(), null);

        var readBack = DisplayService.Load();
        if (!PositionsMatch(plan, readBack))
        {
            var realigned = ApplyPositionsInternal(plan.TargetMonitors, persist);
            if (realigned.Success && realigned.ReadBack is not null)
                readBack = realigned.ReadBack;
        }

        return new DisplayApplyResult(true, ApplySuccessMessage(persist), readBack);
    }

    private static DisplayApplyResult ApplyPositionsInternal(IReadOnlyList<DisplayMonitor> targets, bool persist)
    {
        var targetsByGdi = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.GdiDeviceName))
                return new DisplayApplyResult(false, "A display in the apply plan has no device name; refusing a partial apply.", null);

            if (!targetsByGdi.TryAdd(target.GdiDeviceName, target))
                return new DisplayApplyResult(false, "Duplicate display names in the apply plan; refusing a partial apply.", null);
        }

        var (paths, modes) = DisplayService.QueryActivePaths();
        if (paths.Length == 0)
            return new DisplayApplyResult(false, NoActivePathsMessage(), null);

        var gdiBySource = BuildSourceNameMap(paths);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[i];
            if ((path.Flags & DisplayNative.DisplayConfigPathActive) == 0)
                continue;

            if (!gdiBySource.TryGetValue(SourceKey(path.SourceInfo), out var gdiName))
                continue;

            if (!targetsByGdi.TryGetValue(gdiName, out var target))
                continue;

            matched.Add(gdiName);
            if (!ComposePositionWrites(modes, path, target))
                return new DisplayApplyResult(false, "Could not stage the display position: an internal mode index was invalid. No changes were applied.", null);
        }

        foreach (var target in targets)
        {
            if (!matched.Contains(target.GdiDeviceName))
                return new DisplayApplyResult(false, InactivePathMessage(target.DisplayLabel), null);
        }

        var result = DisplayNative.SetDisplayConfig(
            (uint)paths.Length,
            paths,
            (uint)modes.Length,
            modes,
            BuildFlags(validateOnly: false, persist));

        if (result != DisplayNative.ErrorSuccess)
            return new DisplayApplyResult(false, "Couldn't put the displays back where they belong. The settings on screen may not match what you picked.", null);

        return new DisplayApplyResult(true, ApplySuccessMessage(persist), DisplayService.Load());
    }

    private static DisplayApplyResult RevertToDatabase(DisplaySnapshot? baseline)
    {
        var result = DisplayNative.SetDisplayConfig(
            0,
            null,
            0,
            null,
            DisplayNative.SdcApply | DisplayNative.SdcUseDatabaseCurrent);

        if (result != DisplayNative.ErrorSuccess)
            return new DisplayApplyResult(false, RevertFailureMessage(), null);

        var active = DisplayService.Load();
        if (baseline is null)
            return new DisplayApplyResult(true, RevertSuccessMessage(), active);

        if (DisplayArrangement.AreEquivalent(baseline, active))
            return new DisplayApplyResult(true, RevertSuccessMessage(), active);

        var explicitApply = ApplyInternal(
            new DisplayApplyPlan(baseline.Monitors.ToArray(), Array.Empty<DisplayPendingChange>()),
            persist: false,
            validateOnly: false);
        if (!explicitApply.Success)
            return new DisplayApplyResult(false, RevertPartialMessage(explicitApply.Message), active);

        return new DisplayApplyResult(true, RevertReappliedMessage(), explicitApply.ReadBack ?? active);
    }

    private static Dictionary<string, string> BuildSourceNameMap(DisplayNative.DisplayConfigPathInfo[] paths)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if ((path.Flags & DisplayNative.DisplayConfigPathActive) == 0)
                continue;

            var info = new DisplayNative.DisplayConfigSourceDeviceName
            {
                Header = DisplayNative.CreateHeader(
                    DisplayNative.DeviceInfoGetSourceName,
                    DisplayNative.SizeOf<DisplayNative.DisplayConfigSourceDeviceName>(),
                    path.SourceInfo.AdapterId,
                    path.SourceInfo.Id)
            };

            if (DisplayNative.DisplayConfigGetDeviceInfo(ref info) != DisplayNative.ErrorSuccess)
                continue;

            var name = info.ViewGdiDeviceName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
                map[SourceKey(path.SourceInfo)] = name;
        }

        return map;
    }

    internal static string SourceKey(DisplayNative.DisplayConfigPathSourceInfo source) =>
        $"{source.AdapterId.LowPart}:{source.AdapterId.HighPart}:{source.Id}";
}
