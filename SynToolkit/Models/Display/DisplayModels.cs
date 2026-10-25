#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Models.Display;

public sealed record DisplayMonitor(
    string GdiDeviceName,
    string FriendlyName,
    string MonitorDevicePath,
    uint Rotation,
    uint RefreshNumerator,
    uint RefreshDenominator,
    uint PixelWidth,
    uint PixelHeight,
    int PositionX,
    int PositionY,
    uint PreferredWidth,
    uint PreferredHeight,
    int Ordinal,
    int ScalePercent,
    uint TargetActiveWidth = 0,
    uint TargetActiveHeight = 0,
    uint TargetTotalWidth = 0,
    uint TargetTotalHeight = 0,
    ulong TargetPixelRate = 0,
    uint TargetHSyncNumerator = 0,
    uint TargetHSyncDenominator = 0,
    uint TargetVSyncNumerator = 0,
    uint TargetVSyncDenominator = 0)
{
    public bool IsPrimary => PositionX == 0 && PositionY == 0;

    public string ResolutionText => $"{PixelWidth} × {PixelHeight}";

    public string RefreshText => RefreshDenominator == 0
        ? "Unknown"
        : $"{RefreshNumerator / (double)RefreshDenominator:0.###} Hz";

    public double RefreshHz => RefreshDenominator == 0
        ? 0
        : RefreshNumerator / (double)RefreshDenominator;

    public string PositionText => $"({PositionX}, {PositionY})";

    public string RotationText => Rotation switch
    {
        2 => "Portrait",
        3 => "Landscape (flipped)",
        4 => "Portrait (flipped)",
        _ => "Landscape"
    };

    public string TileAutomationName(bool selected, bool staged)
    {
        var name = FriendlyName.Trim();
        var text = string.IsNullOrEmpty(name)
            ? $"Display {Ordinal}, {ResolutionText}, {PositionText}"
            : $"Display {Ordinal}, {name}, {ResolutionText}, {PositionText}";
        if (selected)
            text += ", selected";
        if (staged)
            text += ", staged";
        return text;
    }

    public string DisplayLabel
    {
        get
        {
            var candidate = FriendlyName.Trim();
            if (candidate.Length > 0 && !candidate.StartsWith(@"\\.\", StringComparison.Ordinal))
                return candidate;
            return $"Display {Ordinal}";
        }
    }

    public DisplayMonitor WithPosition(int x, int y) => this with { PositionX = x, PositionY = y };

    public DisplayMonitor WithMode(uint width, uint height, uint refreshNumerator, uint refreshDenominator, uint rotation) =>
        this with
        {
            PixelWidth = width,
            PixelHeight = height,
            RefreshNumerator = refreshNumerator,
            RefreshDenominator = refreshDenominator,
            Rotation = rotation
        };
}

public sealed record DisplaySnapshot(IReadOnlyList<DisplayMonitor> Monitors)
{
    public static DisplaySnapshot Empty { get; } = new(Array.Empty<DisplayMonitor>());

    public int MinX => Monitors.Count == 0 ? 0 : Monitors.Min(monitor => monitor.PositionX);

    public int MinY => Monitors.Count == 0 ? 0 : Monitors.Min(monitor => monitor.PositionY);

    public int MaxX => Monitors.Count == 0 ? 0 : Monitors.Max(monitor => monitor.PositionX + (int)monitor.PixelWidth);

    public int MaxY => Monitors.Count == 0 ? 0 : Monitors.Max(monitor => monitor.PositionY + (int)monitor.PixelHeight);

    public int Width => MaxX - MinX;

    public int Height => MaxY - MinY;
}

public sealed record DisplayGraphicsMode(
    uint Width,
    uint Height,
    uint RefreshHz,
    uint BitsPerPel,
    uint Orientation)
{
    public string ResolutionKey => $"{Width}x{Height}";
}

public sealed record DisplayResolutionOption(uint Width, uint Height, bool IsRecommended, IReadOnlyList<uint> RefreshRates)
{
    public string Label => IsRecommended
        ? $"{Width} × {Height} (Recommended)"
        : $"{Width} × {Height}";
}

public sealed record DisplayRefreshOption(uint RefreshHz, bool ChangesResolution, string ResolutionKey)
{
    public string Label => ChangesResolution
        ? $"{RefreshHz} Hz (also changes resolution)"
        : $"{RefreshHz} Hz";
}

public sealed record DisplayOrientationOption(uint CcdRotation, string Label);

public sealed record DisplayPickedMode(
    DisplayResolutionOption? Resolution,
    DisplayRefreshOption? Refresh,
    DisplayOrientationOption? Orientation)
{
    public static readonly DisplayPickedMode Empty = new(null, null, null);
}

public sealed record DisplayPendingChange(
    string MonitorKey,
    string MonitorName,
    string Setting,
    string FromValue,
    string ToValue)
{
    public string Summary => $"{MonitorName} · {Setting}: {FromValue} → {ToValue}";
}

public sealed record DisplayApplyPlan(
    IReadOnlyList<DisplayMonitor> TargetMonitors,
    IReadOnlyList<DisplayPendingChange> Changes);

public sealed record DisplayApplyResult(bool Success, string Message, DisplaySnapshot? ReadBack);
