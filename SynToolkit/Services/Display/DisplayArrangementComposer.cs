#nullable enable

using System;
using System.Collections.Generic;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public static class DisplayArrangementComposer
{
    public static DisplaySnapshot? TryCompose(DisplaySnapshot baseline, DisplaySnapshot arrangement)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(arrangement);

        if (baseline.Monitors.Count != arrangement.Monitors.Count)
            return null;

        var baselineByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in baseline.Monitors)
        {
            if (!baselineByKey.TryAdd(monitor.GdiDeviceName, monitor))
                return null;
        }

        var composed = new List<DisplayMonitor>(baseline.Monitors.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var differs = false;
        foreach (var monitor in arrangement.Monitors)
        {
            if (!baselineByKey.TryGetValue(monitor.GdiDeviceName, out var current))
                return null;

            if (!seen.Add(monitor.GdiDeviceName))
                return null;

            if (current.PositionX != monitor.PositionX || current.PositionY != monitor.PositionY)
                differs = true;

            composed.Add(current.WithPosition(monitor.PositionX, monitor.PositionY));
        }

        if (!differs)
            return null;

        var snapshot = new DisplaySnapshot(composed);
        return DisplayGeometry.BlockingIssues(snapshot).Count == 0 ? snapshot : null;
    }

    public static bool TryReplaceStage(DisplaySnapshot baseline, DisplaySnapshot arrangement, Dictionary<string, (int X, int Y)> stage)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(stage);

        var composed = TryCompose(baseline, arrangement);
        if (composed is null)
            return false;

        var baselineByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in baseline.Monitors)
            baselineByKey[monitor.GdiDeviceName] = monitor;

        stage.Clear();
        foreach (var monitor in composed.Monitors)
        {
            var current = baselineByKey[monitor.GdiDeviceName];
            if (current.PositionX != monitor.PositionX || current.PositionY != monitor.PositionY)
                stage[monitor.GdiDeviceName] = (monitor.PositionX, monitor.PositionY);
        }

        return true;
    }

    public static void RestoreStage(IReadOnlyList<DisplayMonitor>? targets, DisplaySnapshot? baseline, Dictionary<string, (int X, int Y)> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        if (targets is null || baseline is null)
            return;

        var baselineByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in baseline.Monitors)
            baselineByKey[monitor.GdiDeviceName] = monitor;

        stage.Clear();
        foreach (var target in targets)
        {
            if (!baselineByKey.TryGetValue(target.GdiDeviceName, out var current))
                continue;

            if (current.PositionX != target.PositionX || current.PositionY != target.PositionY)
                stage[target.GdiDeviceName] = (target.PositionX, target.PositionY);
        }
    }

    public static bool DiscardStageForMissingMonitors(Dictionary<string, (int X, int Y)> stage, IReadOnlyList<DisplayMonitor> present)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(present);

        if (stage.Count == 0)
            return false;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in present)
            keys.Add(monitor.GdiDeviceName);

        foreach (var stagedKey in stage.Keys)
        {
            if (!keys.Contains(stagedKey))
            {
                stage.Clear();
                return true;
            }
        }

        return false;
    }

    public static bool RestoreStageExcludingSwap(IReadOnlyList<DisplayMonitor>? targets, DisplaySnapshot? baseline, Dictionary<string, (int X, int Y)> stage, string? selectedKey, string? oldPrimaryKey, bool excludeSwap)
    {
        ArgumentNullException.ThrowIfNull(stage);

        RestoreStage(targets, baseline, stage);
        if (excludeSwap)
        {
            if (!string.IsNullOrEmpty(selectedKey))
                stage.Remove(selectedKey);
            if (!string.IsNullOrEmpty(oldPrimaryKey))
                stage.Remove(oldPrimaryKey);
        }

        return stage.Count > 0;
    }
}
