#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public sealed record DisplayCanvasRect(double X, double Y, double Width, double Height, DisplayMonitor Monitor);

public sealed record DisplayLayout(IReadOnlyList<DisplayCanvasRect> Rects, double Scale, double OffsetX, double OffsetY)
{
    public static DisplayLayout Empty { get; } = new(Array.Empty<DisplayCanvasRect>(), 1, 0, 0);
}

public enum DisplayGeometryIssueKind
{
    Overlap,
    Gap,
    Origin
}

public sealed record DisplayGeometryIssue(DisplayGeometryIssueKind Kind, DisplayMonitor First, DisplayMonitor Second);

public static class DisplayGeometry
{
    public const double CanvasBoxHeight = 310;
    public const double CanvasBoxWidthMax = 724;
    public const double ArrangeButtonRowHeight = 52;
    internal const double CanvasBorderThickness = 1;
    internal const double FitMarginFraction = 0.08;
    internal const double TileClampInset = 4;

    public static double CanvasBoxWidthFor(double availableWidth)
    {
        if (availableWidth <= 0)
            return 0;

        return Math.Min(CanvasBoxWidthMax, availableWidth);
    }

    public static DisplayLayout BuildLayout(DisplaySnapshot snapshot, double canvasWidth, double canvasHeight, double dipScale)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Monitors.Count == 0 || canvasWidth <= 0 || canvasHeight <= 0 || dipScale <= 0)
            return DisplayLayout.Empty;

        var insetX = canvasWidth * FitMarginFraction;
        var insetY = canvasHeight * FitMarginFraction;
        var availableWidth = Math.Max(1, canvasWidth - (insetX * 2));
        var availableHeight = Math.Max(1, canvasHeight - (insetY * 2));

        var dipWidth = Math.Max(1, snapshot.Width / dipScale);
        var dipHeight = Math.Max(1, snapshot.Height / dipScale);

        var fit = Math.Min(availableWidth / dipWidth, availableHeight / dipHeight);
        var contentWidth = dipWidth * fit;
        var contentHeight = dipHeight * fit;
        var offsetX = insetX + ((availableWidth - contentWidth) / 2);
        var offsetY = insetY + ((availableHeight - contentHeight) / 2);

        var rects = new List<DisplayCanvasRect>(snapshot.Monitors.Count);
        foreach (var monitor in snapshot.Monitors)
        {
            rects.Add(new DisplayCanvasRect(
                offsetX + (((monitor.PositionX - snapshot.MinX) / dipScale) * fit),
                offsetY + (((monitor.PositionY - snapshot.MinY) / dipScale) * fit),
                Math.Max(1, (monitor.PixelWidth / dipScale) * fit),
                Math.Max(1, (monitor.PixelHeight / dipScale) * fit),
                monitor));
        }

        return new DisplayLayout(rects, fit, offsetX, offsetY);
    }

    public static (int X, int Y) CanvasToDesktop(DisplayLayout layout, double canvasX, double canvasY, int originX, int originY, double dipScale)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var desktopX = originX + (int)Math.Round((canvasX - layout.OffsetX) / layout.Scale * dipScale);
        var desktopY = originY + (int)Math.Round((canvasY - layout.OffsetY) / layout.Scale * dipScale);
        return (desktopX, desktopY);
    }

    public static (double X, double Y) DesktopToCanvas(DisplayLayout layout, int desktopX, int desktopY, int originX, int originY, double dipScale)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var canvasX = layout.OffsetX + (((desktopX - originX) / dipScale) * layout.Scale);
        var canvasY = layout.OffsetY + (((desktopY - originY) / dipScale) * layout.Scale);
        return (canvasX, canvasY);
    }

    public static (double X, double Y) ClampTile(double left, double top, double tileWidth, double tileHeight, double contentWidth, double contentHeight)
    {
        var maxLeft = Math.Max(TileClampInset, contentWidth - tileWidth - TileClampInset);
        var maxTop = Math.Max(TileClampInset, contentHeight - tileHeight - TileClampInset);
        return (Math.Min(Math.Max(left, TileClampInset), maxLeft), Math.Min(Math.Max(top, TileClampInset), maxTop));
    }

    public static IReadOnlyList<DisplayGeometryIssue> Validate(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var issues = new List<DisplayGeometryIssue>();
        var monitors = snapshot.Monitors;

        for (var i = 0; i < monitors.Count; i++)
        {
            for (var j = i + 1; j < monitors.Count; j++)
            {
                if (Overlaps(monitors[i], monitors[j]))
                    issues.Add(new DisplayGeometryIssue(DisplayGeometryIssueKind.Overlap, monitors[i], monitors[j]));
            }
        }

        if (issues.Count == 0 && monitors.Count > 1 && !IsConnected(monitors))
            issues.Add(new DisplayGeometryIssue(DisplayGeometryIssueKind.Gap, monitors[0], monitors[1]));

        if (issues.Count == 0 && monitors.Count > 1 && !monitors.Any(monitor => monitor.PositionX == 0 && monitor.PositionY == 0))
            issues.Add(new DisplayGeometryIssue(DisplayGeometryIssueKind.Origin, monitors[0], monitors[1]));

        return issues;
    }

    public static IReadOnlyList<DisplayGeometryIssue> BlockingIssues(DisplaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var blocking = Validate(snapshot)
            .Where(issue => issue.Kind != DisplayGeometryIssueKind.Gap)
            .ToList();
        var monitors = snapshot.Monitors;
        if (blocking.Count == 0 &&
            monitors.Count > 1 &&
            !monitors.Any(monitor => monitor.PositionX == 0 && monitor.PositionY == 0))
            blocking.Add(new DisplayGeometryIssue(DisplayGeometryIssueKind.Origin, monitors[0], monitors[1]));

        return blocking;
    }

    private static bool IsConnected(IReadOnlyList<DisplayMonitor> monitors)
    {
        var visited = new HashSet<int> { 0 };
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            for (var index = 0; index < monitors.Count; index++)
            {
                if (visited.Contains(index))
                    continue;
                if (AreEdgeAdjacent(monitors[current], monitors[index]))
                {
                    visited.Add(index);
                    queue.Enqueue(index);
                }
            }
        }

        return visited.Count == monitors.Count;
    }

    private static bool Overlaps(DisplayMonitor a, DisplayMonitor b)
    {
        var left = Math.Max(a.PositionX, b.PositionX);
        var top = Math.Max(a.PositionY, b.PositionY);
        var right = Math.Min(a.PositionX + (int)a.PixelWidth, b.PositionX + (int)b.PixelWidth);
        var bottom = Math.Min(a.PositionY + (int)a.PixelHeight, b.PositionY + (int)b.PixelHeight);
        return right > left && bottom > top;
    }

    internal static string AxisWord(DisplayMonitor first, DisplayMonitor second)
    {
        var firstBottom = first.PositionY + (int)first.PixelHeight;
        var secondBottom = second.PositionY + (int)second.PixelHeight;
        return CrossAxisOverlaps(first.PositionY, firstBottom, second.PositionY, secondBottom) ? "side by side" : "one above the other";
    }

    internal static bool CrossAxisOverlaps(int firstStart, int firstEnd, int secondStart, int secondEnd) =>
        Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart) >= 0;

    public static DisplayMonitor KeepSharedEdge(
        DisplayMonitor before,
        DisplayMonitor after,
        IReadOnlyList<DisplayMonitor> others)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(others);

        if (before.PixelWidth == after.PixelWidth && before.PixelHeight == after.PixelHeight)
            return after;

        var beforeRight = before.PositionX + (int)before.PixelWidth;
        var beforeBottom = before.PositionY + (int)before.PixelHeight;

        int? anchorX = null;
        int? anchorY = null;

        foreach (var other in others)
        {
            if (string.Equals(other.GdiDeviceName, before.GdiDeviceName, StringComparison.OrdinalIgnoreCase))
                continue;

            var otherRight = other.PositionX + (int)other.PixelWidth;
            var otherBottom = other.PositionY + (int)other.PixelHeight;

            if (beforeRight == other.PositionX &&
                CrossAxisOverlaps(before.PositionY, beforeBottom, other.PositionY, otherBottom))
            {
                var x = other.PositionX - (int)after.PixelWidth;
                if (anchorX.HasValue && anchorX.Value != x)
                    return after;
                anchorX = x;
            }

            if (otherRight == before.PositionX &&
                CrossAxisOverlaps(before.PositionY, beforeBottom, other.PositionY, otherBottom))
            {
                if (anchorX.HasValue && anchorX.Value != otherRight)
                    return after;
                anchorX = otherRight;
            }

            if (beforeBottom == other.PositionY &&
                CrossAxisOverlaps(before.PositionX, beforeRight, other.PositionX, otherRight))
            {
                var y = other.PositionY - (int)after.PixelHeight;
                if (anchorY.HasValue && anchorY.Value != y)
                    return after;
                anchorY = y;
            }

            if (otherBottom == before.PositionY &&
                CrossAxisOverlaps(before.PositionX, beforeRight, other.PositionX, otherRight))
            {
                if (anchorY.HasValue && anchorY.Value != otherBottom)
                    return after;
                anchorY = otherBottom;
            }
        }

        var result = after;
        if (anchorX.HasValue)
            result = result with { PositionX = anchorX.Value };
        if (anchorY.HasValue)
            result = result with { PositionY = anchorY.Value };
        return result;
    }

    internal static bool AreEdgeAdjacent(DisplayMonitor a, DisplayMonitor b)
    {
        var aRight = a.PositionX + (int)a.PixelWidth;
        var bRight = b.PositionX + (int)b.PixelWidth;
        var aBottom = a.PositionY + (int)a.PixelHeight;
        var bBottom = b.PositionY + (int)b.PixelHeight;

        var horizontalOverlap = CrossAxisOverlaps(a.PositionX, aRight, b.PositionX, bRight);
        var verticalOverlap = CrossAxisOverlaps(a.PositionY, aBottom, b.PositionY, bBottom);

        var touchesHorizontally = (aRight == b.PositionX || bRight == a.PositionX) && verticalOverlap;
        var touchesVertically = (aBottom == b.PositionY || bBottom == a.PositionY) && horizontalOverlap;

        return touchesHorizontally || touchesVertically;
    }
}
