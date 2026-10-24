#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public enum DisplayPlacementReason
{
    Placed,
    NoValidArrangement,
    BeyondTolerance
}

public sealed record DisplayPlacementCandidate(
    string AnchorKey,
    DisplayPlacementSide Side,
    int X,
    int Y,
    DisplaySnapshot Arrangement);

public sealed record DisplayPlacementResult(
    DisplayPlacementReason Reason,
    DisplaySnapshot? Arrangement,
    bool Snapped,
    string? AnchorKey,
    DisplayPlacementSide? Side,
    long BestDistanceSquared);

public sealed record SnapMotion(bool Snapped, int DesktopX, int DesktopY, bool LandingDiffersFromPointer);

public static class DisplayPlacementSolver
{
    private static readonly DisplayPlacementSide[] SideOrder =
    [
        DisplayPlacementSide.Left,
        DisplayPlacementSide.Right,
        DisplayPlacementSide.Above,
        DisplayPlacementSide.Below
    ];

    private const double SnapCanvasPx = 16;
    private const double MinDistancePx = 32;
    private const double MaxDistancePx = 320;

    public static double ComputeMaxDistancePx(double dipScale, double layoutScale)
    {
        if (dipScale <= 0 || layoutScale <= 0)
            return MinDistancePx;

        var value = SnapCanvasPx * dipScale / layoutScale;
        if (value < MinDistancePx)
            return MinDistancePx;
        if (value > MaxDistancePx)
            return MaxDistancePx;
        return value;
    }

    public static bool IsWithinTolerance(long distanceSquared, double maxDistancePx) =>
        distanceSquared >= 0 && distanceSquared < maxDistancePx * maxDistancePx;

    public static SnapMotion ResolveSnapMotion(
        DisplaySnapshot current,
        string movingKey,
        int pointerDesktopX,
        int pointerDesktopY,
        IReadOnlyList<DisplayPlacementCandidate> candidates,
        double maxDistancePx,
        bool wasSnapped,
        int lastSnappedX,
        int lastSnappedY)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);

        var nearest = FindNearestCandidate(candidates, pointerDesktopX, pointerDesktopY, out var bestDistanceSquared);
        var verbatimArrangement = TryVerbatimArrangement(current, movingKey, pointerDesktopX, pointerDesktopY);

        if (wasSnapped)
        {
            var holdX = (long)lastSnappedX - pointerDesktopX;
            var holdY = (long)lastSnappedY - pointerDesktopY;
            var holdDistanceSquared = (holdX * holdX) + (holdY * holdY);
            if ((nearest is null || bestDistanceSquared >= holdDistanceSquared) &&
                holdDistanceSquared < maxDistancePx * maxDistancePx)
                return new SnapMotion(true, lastSnappedX, lastSnappedY, LandingDiffersFromPointer(lastSnappedX, lastSnappedY, pointerDesktopX, pointerDesktopY, verbatimArrangement));

            if (nearest is not null &&
                nearest.X == pointerDesktopX &&
                nearest.Y == pointerDesktopY)
                return new SnapMotion(true, nearest.X, nearest.Y, LandingDiffersFromPointer(nearest.X, nearest.Y, pointerDesktopX, pointerDesktopY, verbatimArrangement));
        }

        if (nearest is not null &&
            nearest.X == pointerDesktopX &&
            nearest.Y == pointerDesktopY &&
            verbatimArrangement is not null)
            return new SnapMotion(false, pointerDesktopX, pointerDesktopY, false);

        if (nearest is not null && IsWithinTolerance(bestDistanceSquared, maxDistancePx))
            return new SnapMotion(true, nearest.X, nearest.Y, LandingDiffersFromPointer(nearest.X, nearest.Y, pointerDesktopX, pointerDesktopY, verbatimArrangement));

        return new SnapMotion(false, pointerDesktopX, pointerDesktopY, verbatimArrangement is null);
    }

    private static bool LandingDiffersFromPointer(int landingX, int landingY, int pointerDesktopX, int pointerDesktopY, DisplaySnapshot? verbatimArrangement) =>
        landingX != pointerDesktopX || landingY != pointerDesktopY || verbatimArrangement is null;

    public static DisplaySnapshot? TryVerbatimArrangement(
        DisplaySnapshot current,
        string movingKey,
        int proposedX,
        int proposedY)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (string.IsNullOrWhiteSpace(movingKey))
            return null;

        var monitors = current.Monitors.ToList();
        var movingIndex = FindIndex(monitors, movingKey);
        if (movingIndex < 0)
            return null;

        var reorigin = monitors.Count > 1 &&
            monitors[movingIndex].PositionX == 0 &&
            monitors[movingIndex].PositionY == 0;

        var arrangement = BuildArrangement(monitors, movingIndex, proposedX, proposedY, reorigin);
        return arrangement;
    }

    public static IReadOnlyList<DisplayPlacementCandidate> GetCandidates(
        DisplaySnapshot current,
        string movingKey,
        int proposedX,
        int proposedY) =>
        MergeSlots(
            GetPointerSlots(current, movingKey, proposedX, proposedY),
            GetAlignedSlots(current, movingKey));

    internal static IReadOnlyList<DisplayPlacementCandidate?> GetPointerSlots(
        DisplaySnapshot current,
        string movingKey,
        int proposedX,
        int proposedY)
    {
        ArgumentNullException.ThrowIfNull(current);

        var none = Array.Empty<DisplayPlacementCandidate?>();
        if (!TryPrepare(current, movingKey, out var monitors, out var movingIndex, out var reorigin))
            return none;

        var moving = monitors[movingIndex];
        var slots = new List<DisplayPlacementCandidate?>();
        for (var anchorIndex = 0; anchorIndex < monitors.Count; anchorIndex++)
        {
            if (anchorIndex == movingIndex)
                continue;

            var anchor = monitors[anchorIndex];
            foreach (var side in SideOrder)
            {
                var position = TangentPosition(side, moving, anchor, proposedX, proposedY);
                slots.Add(position is null
                    ? null
                    : TryBuildCandidate(monitors, movingIndex, anchorIndex, anchor.GdiDeviceName, side, position.Value, reorigin));
            }
        }

        return slots;
    }

    internal static IReadOnlyList<DisplayPlacementCandidate?> GetAlignedSlots(
        DisplaySnapshot current,
        string movingKey)
    {
        ArgumentNullException.ThrowIfNull(current);

        var none = Array.Empty<DisplayPlacementCandidate?>();
        if (!TryPrepare(current, movingKey, out var monitors, out var movingIndex, out var reorigin))
            return none;

        var moving = monitors[movingIndex];
        var slots = new List<DisplayPlacementCandidate?>();
        for (var anchorIndex = 0; anchorIndex < monitors.Count; anchorIndex++)
        {
            if (anchorIndex == movingIndex)
                continue;

            var anchor = monitors[anchorIndex];
            foreach (var side in SideOrder)
            {
                foreach (var position in AlignedPositions(side, moving, anchor))
                    slots.Add(TryBuildCandidate(monitors, movingIndex, anchorIndex, anchor.GdiDeviceName, side, position, reorigin));
            }
        }

        return slots;
    }

    internal static IReadOnlyList<DisplayPlacementCandidate> MergeSlots(
        IReadOnlyList<DisplayPlacementCandidate?> pointerSlots,
        IReadOnlyList<DisplayPlacementCandidate?> alignedSlots)
    {
        ArgumentNullException.ThrowIfNull(pointerSlots);
        ArgumentNullException.ThrowIfNull(alignedSlots);

        var merged = new List<DisplayPlacementCandidate>(pointerSlots.Count + alignedSlots.Count);
        var pointerIndex = 0;
        var alignedIndex = 0;
        while (pointerIndex < pointerSlots.Count)
        {
            if (pointerSlots[pointerIndex++] is DisplayPlacementCandidate tangent)
                merged.Add(tangent);

            for (var taken = 0; taken < 3 && alignedIndex < alignedSlots.Count; taken++)
            {
                if (alignedSlots[alignedIndex++] is DisplayPlacementCandidate aligned)
                    merged.Add(aligned);
            }
        }

        while (alignedIndex < alignedSlots.Count)
        {
            if (alignedSlots[alignedIndex++] is DisplayPlacementCandidate aligned)
                merged.Add(aligned);
        }

        return merged;
    }

    private static bool TryPrepare(
        DisplaySnapshot current,
        string movingKey,
        out List<DisplayMonitor> monitors,
        out int movingIndex,
        out bool reorigin)
    {
        monitors = current.Monitors.ToList();
        movingIndex = string.IsNullOrWhiteSpace(movingKey) ? -1 : FindIndex(monitors, movingKey);
        if (movingIndex < 0)
        {
            reorigin = false;
            return false;
        }

        var moving = monitors[movingIndex];
        reorigin = monitors.Count > 1 && moving.PositionX == 0 && moving.PositionY == 0;
        return true;
    }

    private static DisplayPlacementCandidate? TryBuildCandidate(
        List<DisplayMonitor> monitors,
        int movingIndex,
        int anchorIndex,
        string anchorKey,
        DisplayPlacementSide side,
        (int X, int Y) position,
        bool reorigin)
    {
        var arrangement = BuildArrangement(monitors, movingIndex, position.X, position.Y, reorigin);
        if (arrangement is null || !TouchesAnchor(arrangement, movingIndex, anchorIndex))
            return null;

        return new DisplayPlacementCandidate(anchorKey, side, position.X, position.Y, arrangement);
    }

    public static DisplayPlacementCandidate? FindNearestCandidate(
        IReadOnlyList<DisplayPlacementCandidate> candidates,
        int proposedX,
        int proposedY,
        out long bestDistanceSquared)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        DisplayPlacementCandidate? best = null;
        bestDistanceSquared = -1;

        foreach (var candidate in candidates)
        {
            var dx = (long)candidate.X - proposedX;
            var dy = (long)candidate.Y - proposedY;
            var distance = (dx * dx) + (dy * dy);
            if (best is not null && distance >= bestDistanceSquared)
                continue;

            best = candidate;
            bestDistanceSquared = distance;
        }

        return best;
    }

    public static DisplayPlacementResult TrySolve(
        DisplaySnapshot current,
        string movingKey,
        int proposedX,
        int proposedY,
        double maxDistancePx)
    {
        ArgumentNullException.ThrowIfNull(current);

        var candidates = GetCandidates(current, movingKey, proposedX, proposedY);
        var nearest = FindNearestCandidate(candidates, proposedX, proposedY, out var bestDistanceSquared);
        if (nearest is not null &&
            (nearest.X != proposedX || nearest.Y != proposedY) &&
            IsWithinTolerance(bestDistanceSquared, maxDistancePx))
        {
            return new DisplayPlacementResult(
                DisplayPlacementReason.Placed,
                nearest.Arrangement,
                true,
                nearest.AnchorKey,
                nearest.Side,
                bestDistanceSquared);
        }

        var verbatim = TryVerbatimArrangement(current, movingKey, proposedX, proposedY);
        if (verbatim is not null)
            return new DisplayPlacementResult(DisplayPlacementReason.Placed, verbatim, false, null, null, 0);

        if (nearest is not null)
            return new DisplayPlacementResult(DisplayPlacementReason.BeyondTolerance, nearest.Arrangement, true, nearest.AnchorKey, nearest.Side, bestDistanceSquared);

        return new DisplayPlacementResult(DisplayPlacementReason.NoValidArrangement, null, false, null, null, bestDistanceSquared);
    }

    private static (int X, int Y)? TangentPosition(
        DisplayPlacementSide side,
        DisplayMonitor moving,
        DisplayMonitor anchor,
        int proposedX,
        int proposedY)
    {
        var movingWidth = (int)moving.PixelWidth;
        var movingHeight = (int)moving.PixelHeight;

        return side switch
        {
            DisplayPlacementSide.Left => (anchor.PositionX - movingWidth, proposedY),
            DisplayPlacementSide.Right => (anchor.PositionX + (int)anchor.PixelWidth, proposedY),
            DisplayPlacementSide.Above => (proposedX, anchor.PositionY - movingHeight),
            DisplayPlacementSide.Below => (proposedX, anchor.PositionY + (int)anchor.PixelHeight),
            _ => null
        };
    }

    private static IEnumerable<(int X, int Y)> AlignedPositions(
        DisplayPlacementSide side,
        DisplayMonitor moving,
        DisplayMonitor anchor)
    {
        var movingWidth = (int)moving.PixelWidth;
        var movingHeight = (int)moving.PixelHeight;
        var anchorWidth = (int)anchor.PixelWidth;
        var anchorHeight = (int)anchor.PixelHeight;

        switch (side)
        {
            case DisplayPlacementSide.Left:
                var leftX = anchor.PositionX - movingWidth;
                yield return (leftX, anchor.PositionY);
                yield return (leftX, anchor.PositionY + anchorHeight - movingHeight);
                yield return (leftX, anchor.PositionY + ((anchorHeight - movingHeight) / 2));
                break;
            case DisplayPlacementSide.Right:
                var rightX = anchor.PositionX + anchorWidth;
                yield return (rightX, anchor.PositionY);
                yield return (rightX, anchor.PositionY + anchorHeight - movingHeight);
                yield return (rightX, anchor.PositionY + ((anchorHeight - movingHeight) / 2));
                break;
            case DisplayPlacementSide.Above:
                var aboveY = anchor.PositionY - movingHeight;
                yield return (anchor.PositionX, aboveY);
                yield return (anchor.PositionX + anchorWidth - movingWidth, aboveY);
                yield return (anchor.PositionX + ((anchorWidth - movingWidth) / 2), aboveY);
                break;
            case DisplayPlacementSide.Below:
                var belowY = anchor.PositionY + anchorHeight;
                yield return (anchor.PositionX, belowY);
                yield return (anchor.PositionX + anchorWidth - movingWidth, belowY);
                yield return (anchor.PositionX + ((anchorWidth - movingWidth) / 2), belowY);
                break;
            default:
                yield break;
        }
    }

    private static DisplaySnapshot? BuildArrangement(
        List<DisplayMonitor> baseline,
        int movingIndex,
        int x,
        int y,
        bool reorigin)
    {
        var shiftX = reorigin ? -x : 0;
        var shiftY = reorigin ? -y : 0;
        var monitors = new List<DisplayMonitor>(baseline.Count);
        for (var index = 0; index < baseline.Count; index++)
        {
            var monitor = baseline[index];
            if (index == movingIndex)
                monitors.Add(monitor.WithPosition(x + shiftX, y + shiftY));
            else if (reorigin)
                monitors.Add(monitor.WithPosition(monitor.PositionX + shiftX, monitor.PositionY + shiftY));
            else
                monitors.Add(monitor);
        }

        var snapshot = new DisplaySnapshot(monitors);
        return DisplayGeometry.Validate(snapshot).Count == 0 ? snapshot : null;
    }

    private static bool TouchesAnchor(DisplaySnapshot arrangement, int movingIndex, int anchorIndex)
    {
        return DisplayGeometry.AreEdgeAdjacent(arrangement.Monitors[movingIndex], arrangement.Monitors[anchorIndex]);
    }

    private static int FindIndex(List<DisplayMonitor> monitors, string key)
    {
        for (var index = 0; index < monitors.Count; index++)
        {
            if (string.Equals(monitors[index].GdiDeviceName, key, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }
}
