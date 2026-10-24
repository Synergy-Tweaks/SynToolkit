#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public enum DisplayPlacementSide
{
    Left,
    Right,
    Above,
    Below
}

public static class DisplayArrangement
{
    public static DisplaySnapshot? TryPlace(
        DisplaySnapshot snapshot,
        string movingKey,
        string anchorKey,
        DisplayPlacementSide side)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(movingKey) ||
            string.IsNullOrWhiteSpace(anchorKey) ||
            string.Equals(movingKey, anchorKey, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var monitors = snapshot.Monitors.ToList();
        var movingIndex = monitors.FindIndex(monitor =>
            string.Equals(monitor.GdiDeviceName, movingKey, StringComparison.OrdinalIgnoreCase));
        var anchorIndex = monitors.FindIndex(monitor =>
            string.Equals(monitor.GdiDeviceName, anchorKey, StringComparison.OrdinalIgnoreCase));

        if (movingIndex < 0 || anchorIndex < 0)
            return null;

        var moving = monitors[movingIndex];
        var anchor = monitors[anchorIndex];
        var (x, y) = side switch
        {
            DisplayPlacementSide.Left => (anchor.PositionX - (int)moving.PixelWidth, anchor.PositionY),
            DisplayPlacementSide.Right => (anchor.PositionX + (int)anchor.PixelWidth, anchor.PositionY),
            DisplayPlacementSide.Above => (anchor.PositionX, anchor.PositionY - (int)moving.PixelHeight),
            DisplayPlacementSide.Below => (anchor.PositionX, anchor.PositionY + (int)anchor.PixelHeight),
            _ => (moving.PositionX, moving.PositionY)
        };

        monitors[movingIndex] = moving.WithPosition(x, y);
        var candidate = new DisplaySnapshot(monitors);
        return DisplayGeometry.Validate(candidate).Count == 0 ? candidate : null;
    }

    public static bool AreEquivalent(DisplaySnapshot? left, DisplaySnapshot? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        if (left.Monitors.Count != right.Monitors.Count)
            return false;

        var rightByKey = new Dictionary<string, DisplayMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in right.Monitors)
        {
            if (!rightByKey.TryAdd(EquivalenceKey(monitor), monitor))
                return false;
        }

        foreach (var monitor in left.Monitors)
        {
            if (!rightByKey.TryGetValue(EquivalenceKey(monitor), out var other))
                return false;

            if (monitor.PixelWidth != other.PixelWidth ||
                monitor.PixelHeight != other.PixelHeight ||
                monitor.PositionX != other.PositionX ||
                monitor.PositionY != other.PositionY ||
                monitor.Rotation != other.Rotation ||
                monitor.RefreshNumerator != other.RefreshNumerator ||
                monitor.RefreshDenominator != other.RefreshDenominator ||
                monitor.ScalePercent != other.ScalePercent)
            {
                return false;
            }
        }

        return true;
    }

    private static string EquivalenceKey(DisplayMonitor monitor) =>
        string.IsNullOrWhiteSpace(monitor.GdiDeviceName)
            ? $"#{monitor.Ordinal}"
            : monitor.GdiDeviceName;

    public static (int X, int Y)? FindNearestValidPlacement(
        DisplaySnapshot snapshot,
        string movingKey,
        int proposedX,
        int proposedY)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(movingKey))
            return null;

        var result = DisplayPlacementSolver.TrySolve(snapshot, movingKey, proposedX, proposedY, double.MaxValue);
        if (result.Reason != DisplayPlacementReason.Placed || result.Arrangement is null)
            return null;

        var moved = result.Arrangement.Monitors.First(monitor =>
            string.Equals(monitor.GdiDeviceName, movingKey, StringComparison.OrdinalIgnoreCase));
        return (moved.PositionX, moved.PositionY);
    }
}
