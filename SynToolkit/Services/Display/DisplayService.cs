#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SynToolkit.Models.Display;

namespace SynToolkit.Services.Display;

public static class DisplayService
{
    public static Task<DisplaySnapshot> LoadAsync() => Task.Run(Load);

    public static Task<IReadOnlyList<DisplayGraphicsMode>> EnumerateModesAsync(string gdiDeviceName) =>
        Task.Run(() => EnumerateModes(gdiDeviceName));

    public static DisplaySnapshot Load()
    {
        var (paths, modes) = QueryActivePaths();
        return BuildSnapshot(paths, modes, requireSourceMode: true);
    }

    public static DisplaySnapshot LoadFromDatabase()
    {
        var (paths, modes) = QueryDatabasePaths();
        return BuildSnapshot(paths, modes, requireSourceMode: false);
    }

    internal static DisplaySnapshot BuildSnapshot(
        DisplayNative.DisplayConfigPathInfo[] paths,
        DisplayNative.DisplayConfigModeInfo[] modes,
        bool requireSourceMode = false)
    {
        if (paths.Length == 0)
            return DisplaySnapshot.Empty;

        var monitors = new List<DisplayMonitor>(paths.Length);
        var ordinal = 0;

        foreach (var path in paths)
        {
            if ((path.Flags & DisplayNative.DisplayConfigPathActive) == 0)
                continue;

            ordinal++;

            var sourceMode = ResolveSourceMode(modes, path.SourceInfo.ModeInfoIdx);
            var targetSignal = ResolveTargetSignal(modes, path.TargetInfo.ModeInfoIdx);
            var gdiDeviceName = ReadGdiDeviceName(path.SourceInfo);
            if (requireSourceMode && sourceMode is null)
                throw new InvalidOperationException($"Couldn't read the position of {gdiDeviceName}; no display was changed.");
            var (friendlyName, monitorPath) = ReadTargetName(path.TargetInfo);
            var (preferredWidth, preferredHeight) = ReadPreferredMode(path.TargetInfo);

            var rawWidth = sourceMode?.Width ?? preferredWidth;
            var rawHeight = sourceMode?.Height ?? preferredHeight;
            var (width, height) = DisplayModeCatalog.EffectiveModeSize(rawWidth, rawHeight, path.TargetInfo.Rotation);
            var positionX = sourceMode?.PositionX ?? 0;
            var positionY = sourceMode?.PositionY ?? 0;

            var effectiveSignal = DisplayModeCatalog.SwapsDimensions(path.TargetInfo.Rotation)
                ? DisplayApplyService.ComposeTargetSignal(
                    targetSignal,
                    width,
                    height,
                    path.TargetInfo.RefreshRate,
                    liveRotation: 1,
                    targetRotation: path.TargetInfo.Rotation)
                : targetSignal;

            monitors.Add(new DisplayMonitor(
                gdiDeviceName,
                friendlyName,
                monitorPath,
                path.TargetInfo.Rotation,
                path.TargetInfo.RefreshRate.Numerator,
                path.TargetInfo.RefreshRate.Denominator,
                width,
                height,
                positionX,
                positionY,
                preferredWidth,
                preferredHeight,
                ordinal,
                ReadScalePercent(positionX, positionY, width, height),
                effectiveSignal.ActiveSize.Cx,
                effectiveSignal.ActiveSize.Cy,
                effectiveSignal.TotalSize.Cx,
                effectiveSignal.TotalSize.Cy,
                effectiveSignal.PixelRate,
                effectiveSignal.HSyncFreq.Numerator,
                effectiveSignal.HSyncFreq.Denominator,
                effectiveSignal.VSyncFreq.Numerator,
                effectiveSignal.VSyncFreq.Denominator));
        }

        return new DisplaySnapshot(monitors);
    }

    public static IReadOnlyList<DisplayGraphicsMode> EnumerateModes(string gdiDeviceName)
    {
        if (string.IsNullOrWhiteSpace(gdiDeviceName))
            return Array.Empty<DisplayGraphicsMode>();

        var modes = new List<DisplayGraphicsMode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (uint index = 0; ; index++)
        {
            var devMode = DisplayNative.CreateDevMode();
            if (!DisplayNative.EnumDisplaySettingsEx(gdiDeviceName, index, ref devMode, DisplayNative.EdsRotatedMode))
                break;

            if (devMode.dmPelsWidth == 0 || devMode.dmPelsHeight == 0 || devMode.dmDisplayFrequency == 0)
                continue;

            var key = $"{devMode.dmPelsWidth}x{devMode.dmPelsHeight}@{devMode.dmDisplayFrequency}:{devMode.dmDisplayOrientation}:{devMode.dmBitsPerPel}";
            if (!seen.Add(key))
                continue;

            modes.Add(new DisplayGraphicsMode(
                devMode.dmPelsWidth,
                devMode.dmPelsHeight,
                devMode.dmDisplayFrequency,
                devMode.dmBitsPerPel,
                DisplayNative.DmdoToCcdRotation(devMode.dmDisplayOrientation)));
        }

        return modes;
    }

    internal static (DisplayNative.DisplayConfigPathInfo[] Paths, DisplayNative.DisplayConfigModeInfo[] Modes) QueryActivePaths() =>
        QueryPaths(DisplayNative.QdcOnlyActivePaths);

    internal static (DisplayNative.DisplayConfigPathInfo[] Paths, DisplayNative.DisplayConfigModeInfo[] Modes) QueryDatabasePaths() =>
        QueryPaths(DisplayNative.QdcDatabaseCurrent);

    private static (DisplayNative.DisplayConfigPathInfo[] Paths, DisplayNative.DisplayConfigModeInfo[] Modes) QueryPaths(uint flags)
    {
        var queryDatabase = (flags & DisplayNative.QdcDatabaseCurrent) != 0;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var sizeResult = DisplayNative.GetDisplayConfigBufferSizes(
                flags, out var pathCount, out var modeCount);

            if (sizeResult != DisplayNative.ErrorSuccess)
                throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed (error {sizeResult}).");

            if (pathCount == 0)
                return (Array.Empty<DisplayNative.DisplayConfigPathInfo>(), Array.Empty<DisplayNative.DisplayConfigModeInfo>());

            var paths = new DisplayNative.DisplayConfigPathInfo[pathCount];
            var modes = new DisplayNative.DisplayConfigModeInfo[modeCount];

            int queryResult;
            if (queryDatabase)
                queryResult = DisplayNative.QueryDisplayConfig(
                    flags, ref pathCount, paths, ref modeCount, modes, out _);
            else
                queryResult = DisplayNative.QueryDisplayConfig(
                    flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (queryResult == DisplayNative.ErrorSuccess)
            {
                if (pathCount != paths.Length)
                    Array.Resize(ref paths, (int)pathCount);
                if (modeCount != modes.Length)
                    Array.Resize(ref modes, (int)modeCount);

                return (paths, modes);
            }

            if (queryResult != DisplayNative.ErrorInsufficientBuffer)
                throw new InvalidOperationException($"QueryDisplayConfig failed (error {queryResult}).");
        }

        throw new InvalidOperationException("QueryDisplayConfig failed after repeated display configuration changes.");
    }

    private static DisplayNative.DisplayConfigSourceMode? ResolveSourceMode(
        DisplayNative.DisplayConfigModeInfo[] modes, uint modeInfoIdx)
    {
        if (modeInfoIdx == DisplayNative.DisplayConfigPathModeIdxInvalid || modeInfoIdx >= modes.Length)
            return null;

        if (modes[modeInfoIdx].InfoType != DisplayNative.DisplayConfigModeInfoTypeSource)
            return null;

        return modes[modeInfoIdx].Mode.SourceMode;
    }

    private static DisplayNative.DisplayConfigVideoSignalInfo ResolveTargetSignal(
        DisplayNative.DisplayConfigModeInfo[] modes, uint modeInfoIdx)
    {
        if (modeInfoIdx == DisplayNative.DisplayConfigPathModeIdxInvalid || modeInfoIdx >= modes.Length)
            return default;

        if (modes[modeInfoIdx].InfoType != DisplayNative.DisplayConfigModeInfoTypeTarget)
            return default;

        return modes[modeInfoIdx].Mode.TargetMode.TargetVideoSignalInfo;
    }

    private static string ReadGdiDeviceName(DisplayNative.DisplayConfigPathSourceInfo source)
    {
        var info = new DisplayNative.DisplayConfigSourceDeviceName
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoGetSourceName,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigSourceDeviceName>(),
                source.AdapterId,
                source.Id)
        };

        return DisplayNative.DisplayConfigGetDeviceInfo(ref info) == DisplayNative.ErrorSuccess
            ? info.ViewGdiDeviceName ?? string.Empty
            : string.Empty;
    }

    private static (string FriendlyName, string MonitorPath) ReadTargetName(DisplayNative.DisplayConfigPathTargetInfo target)
    {
        var info = new DisplayNative.DisplayConfigTargetDeviceName
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoGetTargetName,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigTargetDeviceName>(),
                target.AdapterId,
                target.Id)
        };

        if (DisplayNative.DisplayConfigGetDeviceInfo(ref info) != DisplayNative.ErrorSuccess)
            return (string.Empty, string.Empty);

        return (info.MonitorFriendlyDeviceName ?? string.Empty, info.MonitorDevicePath ?? string.Empty);
    }

    private static (uint Width, uint Height) ReadPreferredMode(DisplayNative.DisplayConfigPathTargetInfo target)
    {
        var info = new DisplayNative.DisplayConfigTargetPreferredMode
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoGetTargetPreferredMode,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigTargetPreferredMode>(),
                target.AdapterId,
                target.Id)
        };

        return DisplayNative.DisplayConfigGetDeviceInfo(ref info) == DisplayNative.ErrorSuccess
            ? (info.Width, info.Height)
            : (0u, 0u);
    }

    internal static DisplayScaleTriple GetScaleTriple(DisplayNative.DisplayConfigPathSourceInfo source)
    {
        var info = new DisplayNative.DisplayConfigGetSourceDpiScale
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoGetSourceDpiScale,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigGetSourceDpiScale>(),
                source.AdapterId,
                source.Id)
        };

        var rc = DisplayNative.DisplayConfigGetDeviceInfo(ref info);
        return rc == DisplayNative.ErrorSuccess
            ? new DisplayScaleTriple(info.MinRelativeScaleStep, info.CurRelativeScaleStep, info.MaxRelativeScaleStep, rc)
            : new DisplayScaleTriple(0, 0, 0, rc);
    }

    internal static bool TryGetSourceForMonitor(string gdiDeviceName, out DisplayNative.DisplayConfigPathSourceInfo source)
    {
        source = default;
        if (string.IsNullOrWhiteSpace(gdiDeviceName))
            return false;

        var (paths, _) = QueryActivePaths();
        foreach (var path in paths)
        {
            if ((path.Flags & DisplayNative.DisplayConfigPathActive) == 0)
                continue;

            if (string.Equals(ReadGdiDeviceName(path.SourceInfo), gdiDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                source = path.SourceInfo;
                return true;
            }
        }

        return false;
    }

    internal static string ReadMonitorDevicePath(DisplayNative.DisplayConfigPathSourceInfo source)
    {
        var (paths, _) = QueryActivePaths();
        foreach (var path in paths)
        {
            if ((path.Flags & DisplayNative.DisplayConfigPathActive) == 0)
                continue;

            if (path.SourceInfo.AdapterId.LowPart == source.AdapterId.LowPart &&
                path.SourceInfo.AdapterId.HighPart == source.AdapterId.HighPart &&
                path.SourceInfo.Id == source.Id)
            {
                return ReadTargetName(path.TargetInfo).MonitorPath;
            }
        }

        return string.Empty;
    }

    internal static int ReadScalePercent(int positionX, int positionY, uint width, uint height)
    {
        if (width == 0 || height == 0)
            return 0;

        var point = new DisplayNative.PointL
        {
            X = positionX + (int)(width / 2),
            Y = positionY + (int)(height / 2)
        };

        var monitor = DisplayNative.MonitorFromPoint(point, DisplayNative.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return 0;

        if (DisplayNative.GetDpiForMonitor(monitor, DisplayNative.MdtEffectiveDpi, out var dpiX, out _) != 0 || dpiX == 0)
            return 0;

        return (int)Math.Round(dpiX * 100.0 / 96.0);
    }
}

public sealed record DisplayScaleTriple(
    int MinRelativeScaleStep,
    int CurRelativeScaleStep,
    int MaxRelativeScaleStep,
    int ReturnCode)
{
    public bool Success => ReturnCode == DisplayNative.ErrorSuccess;
}
