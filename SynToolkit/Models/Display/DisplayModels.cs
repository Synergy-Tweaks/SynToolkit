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
    uint TargetActiveWidth,
    uint TargetActiveHeight,
    uint TargetTotalWidth,
    uint TargetTotalHeight,
    ulong TargetPixelRate,
    uint TargetHSyncNumerator,
    uint TargetHSyncDenominator,
    uint TargetVSyncNumerator,
    uint TargetVSyncDenominator)
{
    public bool IsPrimary => PositionX == 0 && PositionY == 0;

    public double RefreshHz => RefreshDenominator == 0
        ? 0
        : RefreshNumerator / (double)RefreshDenominator;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(FriendlyName) ? $"Display {Ordinal}" : FriendlyName;

    public string ResolutionText => $"{PixelWidth} × {PixelHeight}";

    public string RefreshText => $"{RefreshHz:0.#} Hz";

    public string RotationText => Rotation switch
    {
        1 => "Landscape",
        2 => "Portrait",
        3 => "Landscape (flipped)",
        4 => "Portrait (flipped)",
        _ => Rotation.ToString()
    };

    public string PositionText => $"({PositionX}, {PositionY})";

    public DisplayMonitor WithPosition(int x, int y) => this with { PositionX = x, PositionY = y };

    public DisplayMonitor WithMode(
        uint width,
        uint height,
        uint refreshNumerator,
        uint refreshDenominator,
        uint rotation) => this with
    {
        PixelWidth = width,
        PixelHeight = height,
        RefreshNumerator = refreshNumerator,
        RefreshDenominator = refreshDenominator,
        Rotation = rotation
    };

    public string TileAutomationName(bool selected, bool staged)
    {
        var selection = selected ? "selected" : "not selected";
        var arrangement = staged ? "arrangement staged" : "arrangement not staged";
        return $"{DisplayLabel}, display {Ordinal}, {ResolutionText}, {selection}, {arrangement}";
    }
}

public sealed class DisplaySnapshot
{
    public static DisplaySnapshot Empty { get; } = new(Array.Empty<DisplayMonitor>());

    public DisplaySnapshot(IEnumerable<DisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        Monitors = monitors.ToArray();
    }

    public IReadOnlyList<DisplayMonitor> Monitors { get; }

    public int MinX => Monitors.Count == 0 ? 0 : Monitors.Min(monitor => monitor.PositionX);

    public int MinY => Monitors.Count == 0 ? 0 : Monitors.Min(monitor => monitor.PositionY);

    public int Width => Monitors.Count == 0
        ? 0
        : Monitors.Max(monitor => monitor.PositionX + (int)monitor.PixelWidth) - MinX;

    public int Height => Monitors.Count == 0
        ? 0
        : Monitors.Max(monitor => monitor.PositionY + (int)monitor.PixelHeight) - MinY;
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

public sealed record DisplayResolutionOption(
    uint Width,
    uint Height,
    bool IsRecommended,
    IReadOnlyList<uint> RefreshRates)
{
    public string Label => IsRecommended ? $"{Width} × {Height} (Recommended)" : $"{Width} × {Height}";
}

public sealed record DisplayRefreshOption(uint RefreshHz, bool ChangesResolution, string ResolutionKey)
{
    public string Label => ChangesResolution
        ? $"{RefreshHz:0.#} Hz ({ResolutionKey})"
        : $"{RefreshHz:0.#} Hz";
}

public sealed record DisplayOrientationOption(uint CcdRotation, string Label);

public sealed record DisplayPickedMode(
    DisplayResolutionOption? Resolution,
    DisplayRefreshOption? Refresh,
    DisplayOrientationOption? Orientation)
{
    public static DisplayPickedMode Empty { get; } = new(null, null, null);
}

public sealed record DisplayPendingChange(
    string MonitorKey,
    string FriendlyName,
    string Setting,
    string From,
    string To);

public sealed record DisplayApplyPlan(
    IReadOnlyList<DisplayMonitor> TargetMonitors,
    IReadOnlyList<DisplayPendingChange> Changes);

public sealed record DisplayApplyResult(bool Success, string Message, DisplaySnapshot? ReadBack);
