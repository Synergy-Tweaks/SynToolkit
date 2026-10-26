#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public static class DisplayModeCatalog
{
    public static bool SwapsDimensions(uint rotation) => rotation is 2 or 4;

    public static string ModesCacheKey(DisplayMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return string.Join(
            "|",
            monitor.GdiDeviceName,
            $"{monitor.PixelWidth}x{monitor.PixelHeight}",
            $"{monitor.RefreshNumerator}/{monitor.RefreshDenominator}",
            monitor.Rotation.ToString(),
            $"{monitor.PreferredWidth}x{monitor.PreferredHeight}");
    }

    public static (uint Width, uint Height) EffectiveModeSize(uint width, uint height, uint rotation) =>
        SwapsDimensions(rotation) ? (height, width) : (width, height);

    public static bool SamePanelShape(uint liveWidth, uint liveHeight, uint nextWidth, uint nextHeight) =>
        Math.Min(liveWidth, liveHeight) == Math.Min(nextWidth, nextHeight) &&
        Math.Max(liveWidth, liveHeight) == Math.Max(nextWidth, nextHeight);

    public static bool SameFraction(ulong leftNumerator, uint leftDenominator, ulong rightNumerator, uint rightDenominator)
    {
        if (leftDenominator == 0 || rightDenominator == 0)
            return leftNumerator == rightNumerator && leftDenominator == rightDenominator;

        return (UInt128)leftNumerator * rightDenominator == (UInt128)rightNumerator * leftDenominator;
    }

    public const double RefreshRateToleranceHz = 1.0;

    private const double LiveLabelEpsilon = 0.02;

    public static uint RefreshLabel(double hz) =>
        hz <= 0 ? 0u : (uint)Math.Floor(hz + LiveLabelEpsilon);

    public static bool SameRefreshRate(double left, double right) =>
        RefreshLabel(left) == RefreshLabel(right);

    public static DisplayRefreshOption? SelectRefreshRate(IReadOnlyList<DisplayRefreshOption> options, double liveHz)
    {
        ArgumentNullException.ThrowIfNull(options);

        DisplayRefreshOption? labelled = null;
        DisplayRefreshOption? closest = null;
        var closestDistance = double.MaxValue;
        foreach (var option in options)
        {
            if (option.ChangesResolution)
                continue;

            if (option.RefreshHz <= liveHz + LiveLabelEpsilon &&
                SameRefreshRate(option.RefreshHz, liveHz) &&
                (labelled is null || option.RefreshHz > labelled.RefreshHz))
            {
                labelled = option;
            }

            var distance = Math.Abs(option.RefreshHz - liveHz);
            if (distance >= RefreshRateToleranceHz || distance >= closestDistance)
                continue;

            closest = option;
            closestDistance = distance;
        }

        return labelled
            ?? closest
            ?? ClosestRefreshRate(options, liveHz, skipResolutionChanges: true)
            ?? ClosestRefreshRate(options, liveHz, skipResolutionChanges: false);
    }

    private static DisplayRefreshOption? ClosestRefreshRate(
        IReadOnlyList<DisplayRefreshOption> options,
        double liveHz,
        bool skipResolutionChanges)
    {
        DisplayRefreshOption? best = null;
        var bestDistance = double.MaxValue;
        foreach (var option in options)
        {
            if (skipResolutionChanges && option.ChangesResolution)
                continue;

            var distance = Math.Abs(option.RefreshHz - liveHz);
            if (best is not null && distance >= bestDistance)
                continue;

            best = option;
            bestDistance = distance;
        }

        return best;
    }

    public static DisplayResolutionOption? SelectResolution(
        IReadOnlyList<DisplayResolutionOption> options,
        uint pixelWidth,
        uint pixelHeight,
        uint rotation)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (matchWidth, matchHeight) = EffectiveModeSize(pixelWidth, pixelHeight, rotation);
        return options.FirstOrDefault(option => option.Width == matchWidth && option.Height == matchHeight)
            ?? options.FirstOrDefault();
    }

    public static string? IncoherentModeMessage(uint width, uint height, uint rotation)
    {
        if (width > height && rotation is not (1 or 3))
            return "The mode is landscape but the orientation says portrait. Nothing was applied; pick a matching orientation first.";

        if (height > width && rotation is not (2 or 4))
            return "The mode is portrait but the orientation says landscape. Nothing was applied; pick a matching orientation first.";

        return null;
    }

    public static DisplayPickedMode WithOrientation(
        DisplayPickedMode mode,
        DisplayOrientationOption orientation)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(orientation);

        return new DisplayPickedMode(mode.Resolution, mode.Refresh, orientation);
    }

    public static DisplayPickedMode WithResolution(
        DisplayPickedMode mode,
        DisplayResolutionOption resolution)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(resolution);

        return new DisplayPickedMode(resolution, mode.Refresh, mode.Orientation);
    }

    public static readonly IReadOnlyList<DisplayOrientationOption> Orientations =
    [
        new(1, "Landscape"),
        new(2, "Portrait"),
        new(3, "Landscape (flipped)"),
        new(4, "Portrait (flipped)")
    ];

    public static IReadOnlyList<DisplayResolutionOption> BuildResolutions(
        IReadOnlyList<DisplayGraphicsMode> modes,
        uint preferredWidth,
        uint preferredHeight)
    {
        ArgumentNullException.ThrowIfNull(modes);

        return modes
            .GroupBy(mode => ShapeKey(mode.Width, mode.Height))
            .Select(group =>
            {
                var sample = NativeListing(group);
                var refreshRates = group
                    .Select(mode => mode.RefreshHz)
                    .Distinct()
                    .OrderByDescending(hz => hz)
                    .ToArray();
                var isRecommended = group.Any(mode => mode.Width == preferredWidth && mode.Height == preferredHeight);
                return new DisplayResolutionOption(sample.Width, sample.Height, isRecommended, refreshRates);
            })
            .OrderByDescending(option => option.IsRecommended)
            .ThenByDescending(option => option.Width)
            .ThenByDescending(option => option.Height)
            .ToArray();
    }

    private static string ShapeKey(uint width, uint height) =>
        $"{Math.Min(width, height)}x{Math.Max(width, height)}";

    private static DisplayGraphicsMode NativeListing(IEnumerable<DisplayGraphicsMode> group) =>
        group.FirstOrDefault(mode => mode.Orientation == DisplayNative.DisplayConfigRotationIdentity)
            ?? group.First();

    public static IReadOnlyList<DisplayRefreshOption> BuildAdvancedRefreshRates(
        IReadOnlyList<DisplayGraphicsMode> modes,
        uint selectedWidth,
        uint selectedHeight)
    {
        ArgumentNullException.ThrowIfNull(modes);

        var selectedKey = $"{selectedWidth}x{selectedHeight}";
        return modes
            .GroupBy(mode => mode.RefreshHz)
            .Select(group =>
            {
                var sameResolution = group.Any(mode => mode.Width == selectedWidth && mode.Height == selectedHeight);
                var sample = group.FirstOrDefault(mode => mode.Width == selectedWidth && mode.Height == selectedHeight)
                    ?? group.OrderByDescending(mode => mode.Width * mode.Height).First();
                return new DisplayRefreshOption(
                    group.Key,
                    ChangesResolution: !sameResolution,
                    sameResolution ? selectedKey : sample.ResolutionKey);
            })
            .OrderByDescending(option => option.RefreshHz)
            .ToArray();
    }

    public static (uint Numerator, uint Denominator) ResolveRefreshRational(
        DisplayMonitor current,
        uint width,
        uint height,
        uint refreshHz)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (current.RefreshDenominator != 0 &&
            Math.Min(width, height) == Math.Min(current.PixelWidth, current.PixelHeight) &&
            Math.Max(width, height) == Math.Max(current.PixelWidth, current.PixelHeight) &&
            SameRefreshRate(current.RefreshHz, refreshHz))
            return (current.RefreshNumerator, current.RefreshDenominator);

        return (refreshHz, 1);
    }

    public static IReadOnlyList<DisplayPendingChange> Diff(
        DisplayMonitor baseline,
        uint width,
        uint height,
        uint refreshHz,
        uint rotation,
        bool makePrimary,
        int positionX,
        int positionY)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var changes = new List<DisplayPendingChange>();
        var key = baseline.GdiDeviceName;

        if (baseline.PixelWidth != width || baseline.PixelHeight != height)
        {
            changes.Add(new DisplayPendingChange(
                key,
                baseline.FriendlyName,
                "Resolution",
                baseline.ResolutionText,
                $"{width} × {height}"));
        }

        if (!SameRefreshRate(baseline.RefreshHz, refreshHz))
        {
            changes.Add(new DisplayPendingChange(
                key,
                baseline.FriendlyName,
                "Refresh rate",
                baseline.RefreshText,
                $"{refreshHz} Hz"));
        }

        if (baseline.Rotation != rotation)
        {
            var toLabel = Orientations.FirstOrDefault(option => option.CcdRotation == rotation)?.Label ?? rotation.ToString();
            changes.Add(new DisplayPendingChange(
                key,
                baseline.FriendlyName,
                "Orientation",
                baseline.RotationText,
                toLabel));
        }

        if (makePrimary && !baseline.IsPrimary)
        {
            changes.Add(new DisplayPendingChange(
                key,
                baseline.FriendlyName,
                "Primary",
                "No",
                "Yes"));
        }

        if (baseline.PositionX != positionX || baseline.PositionY != positionY)
        {
            changes.Add(new DisplayPendingChange(
                key,
                baseline.FriendlyName,
                "Position",
                baseline.PositionText,
                $"({positionX}, {positionY})"));
        }

        return changes;
    }
}
