#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services.Display;

public sealed record DisplayHdrState(
    bool Supported,
    bool Enabled,
    bool WideColorEnforced,
    bool ForceDisabled)
{
    public static DisplayHdrState Decode(uint value)
    {
        var wideColorEnforced = (value & 0b100) != 0;
        var supported = (value & 0b1) != 0 && !wideColorEnforced;
        return new DisplayHdrState(
            supported,
            supported && (value & 0b10) != 0,
            wideColorEnforced,
            (value & 0b1000) != 0);
    }

    public static DisplayHdrState DecodeHdr(uint value, uint activeColorMode) => new(
        (value & 0x10) != 0,
        (value & 0x20) != 0 || activeColorMode == 2,
        false,
        false);
}

public sealed record DisplayHdrRead(bool Success, DisplayHdrState? State, int ReturnCode);

internal sealed record DisplayHdrTarget(
    DisplayNative.DisplayConfigPathTargetInfo Target,
    string MonitorPath);

public sealed record DisplayHdrResult(
    bool Success,
    string Message,
    int ReadReturnCode,
    int SetReturnCode,
    int VerifyReturnCode);

public static class DisplayHdrService
{
    internal const int VerifyAttempts = 12;
    internal const int VerifyDelayMs = 250;

    public static string StagedConflictMessage() =>
        "Apply or cancel the staged arrangement first. HDR cannot change while an arrangement is waiting to be applied.";

    public static string ReadFailureMessage() =>
        "Couldn't read the HDR state. Nothing changed.";

    public const string AlreadyActiveMessage = "That HDR setting is already active. Nothing changed.";

    public static string ApplyReadFailureMessage(bool confirmedApplied) => confirmedApplied
        ? "The HDR change was applied, but the follow-up check failed. Keep it or revert it below."
        : "Couldn't confirm the HDR change. The settings on screen may be out of date. Change HDR again to try.";

    public static string RevertFailureMessage() =>
        "Couldn't revert the HDR change. Press Revert again to try.";

    public static string RevertReadFailureMessage() =>
        "Couldn't read the current HDR state. Nothing changed.";

    public static string KeepMessage(bool stateReadable, bool stateMatches) => stateMatches
        ? "HDR change confirmed on screen."
        : stateReadable
            ? "The HDR change was kept, but the current HDR state is not what was requested. Change it again to try."
            : "Couldn't confirm the HDR change. Change it again to try.";

    public static string RevertMismatchMessage(bool stateReadable) => stateReadable
        ? "The HDR setting was reverted, but the current HDR state is not the previous one. Change it again to try."
        : "Couldn't confirm the HDR revert. Change it again to try.";

    public static string SupportText(DisplayHdrState? state) => state switch
    {
        null => "Couldn't read whether this display supports HDR.",
        { Supported: true } => "This display supports HDR.",
        _ => "This display doesn't support HDR."
    };

    public static string? ToggleDisabledReason(DisplayHdrState? state) => state switch
    {
        null => "Couldn't read the HDR state.",
        { Supported: false, WideColorEnforced: true } => "This display is using wide colour, so HDR can't be turned on here.",
        { Supported: false } => "This display doesn't support HDR.",
        { ForceDisabled: true } => "HDR is turned off by system policy on this display.",
        _ => null
    };

    internal static DisplayHdrRead ReadStateFor(string gdiDeviceName)
    {
        if (!TryGetTargetForMonitor(gdiDeviceName, out var target))
            return new DisplayHdrRead(false, null, -1);

        return ReadState(target);
    }

    internal static bool TryGetTargetForMonitor(string gdiDeviceName, out DisplayHdrTarget target)
    {
        target = default!;
        if (!DisplayService.TryGetTargetForMonitor(gdiDeviceName, out var targetInfo, out var monitorPath))
            return false;

        target = new DisplayHdrTarget(targetInfo, monitorPath);
        return true;
    }

    internal static DisplayHdrRead ReadState(DisplayHdrTarget target) =>
        ReadStateWith(
            () => ReadAdvancedColorInfo2(target.Target),
            () => ReadAdvancedColorInfo(target.Target),
            () => ReadEdidSupport(target.MonitorPath));

    internal static DisplayHdrRead ReadStateWith(
        Func<DisplayHdrRead> readHdrInfo,
        Func<DisplayHdrRead> readAdvancedColorInfo,
        Func<bool> edidSupportsHdr)
    {
        ArgumentNullException.ThrowIfNull(readHdrInfo);
        ArgumentNullException.ThrowIfNull(readAdvancedColorInfo);
        ArgumentNullException.ThrowIfNull(edidSupportsHdr);

        var read = readHdrInfo();
        if (!read.Success)
            read = readAdvancedColorInfo();

        var state = read.State;
        if (!read.Success || state is null)
            return read;

        if (state.Supported || state.WideColorEnforced)
            return read;

        return edidSupportsHdr()
            ? read with { State = state with { Supported = true } }
            : read;
    }

    internal static Task<DisplayHdrResult> SetEnabledAsync(
        DisplayHdrTarget target,
        bool requestedState,
        bool previousState) =>
        Task.Run(() => ExecuteHdrChange(
            requestedState,
            previousState,
            () => ReadState(target),
            enable => WriteEnabled(target, enable),
            VerifyAttempts,
            VerifyDelayMs));

    internal static DisplayHdrResult ExecuteHdrChange(
        bool requestedState,
        bool previousState,
        Func<DisplayHdrRead> read,
        Func<bool, int> write,
        int verifyAttempts = 1,
        int verifyDelayMs = 0)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

        var before = read();
        if (!before.Success || before.State is null)
            return new DisplayHdrResult(false, ReadFailureMessage(), before.ReturnCode, 0, 0);

        if (!before.State.Supported)
            return new DisplayHdrResult(false, "This display doesn't support HDR. Nothing changed.", before.ReturnCode, 0, 0);

        if (before.State.Enabled == requestedState)
            return new DisplayHdrResult(true, AlreadyActiveMessage, 0, 0, 0);

        if (before.State.ForceDisabled && requestedState)
            return new DisplayHdrResult(false, "HDR is turned off by system policy on this display, so it can't be changed here. Nothing changed.", before.ReturnCode, 0, 0);

        var setRc = write(requestedState);
        if (setRc != DisplayNative.ErrorSuccess)
            return new DisplayHdrResult(false, "Couldn't change HDR. No further change was attempted.", 0, setRc, 0);

        var verify = ReadUntilEnabled(read, requestedState, verifyAttempts, verifyDelayMs);
        if (verify.Success && verify.State is not null && verify.State.Enabled == requestedState)
            return new DisplayHdrResult(true, requestedState ? "HDR turned on." : "HDR turned off.", 0, 0, 0);

        var seen = verify.Success && verify.State is not null ? verify.State.Enabled : (bool?)null;
        var verifyRc = verify.Success ? 0 : verify.ReturnCode;
        write(previousState);
        var reverify = ReadUntilEnabled(read, previousState, verifyAttempts, verifyDelayMs);
        var reverifyRc = reverify.Success ? 0 : reverify.ReturnCode;
        var restored = reverify.Success && reverify.State is not null && reverify.State.Enabled == previousState;
        var tail = restored
            ? "The previous setting was written back and confirmed."
            : "The previous setting was written back, but it also didn't read back. Check the HDR setting in Windows Settings.";
        var detail = seen is null
            ? "Couldn't confirm the HDR change."
            : $"HDR read back {(seen.Value ? "on" : "off")} instead of {(requestedState ? "on" : "off")}.";
        return new DisplayHdrResult(false, $"{detail} {tail}", 0, 0, verifyRc != 0 ? verifyRc : reverifyRc);
    }

    internal static DisplayHdrRead ReadUntilEnabled(
        Func<DisplayHdrRead> read,
        bool expected,
        int attempts,
        int delayMs)
    {
        ArgumentNullException.ThrowIfNull(read);

        var last = read();
        for (var i = 1; i < attempts; i++)
        {
            if (last.Success && last.State is not null && last.State.Enabled == expected)
                break;

            if (delayMs > 0)
                Thread.Sleep(delayMs);
            last = read();
        }

        return last;
    }

    internal static int WriteEnabled(DisplayHdrTarget target, bool enable) =>
        WriteEnabledWith(
            () => WriteHdrState(target, enable),
            () => WriteAdvancedColorState(target, enable));

    internal static int WriteEnabledWith(Func<int> writeHdrState, Func<int> writeAdvancedColorState)
    {
        ArgumentNullException.ThrowIfNull(writeHdrState);
        ArgumentNullException.ThrowIfNull(writeAdvancedColorState);

        var hdrRc = writeHdrState();
        return hdrRc == DisplayNative.ErrorSuccess ? hdrRc : writeAdvancedColorState();
    }

    internal static int WriteHdrState(DisplayHdrTarget target, bool enable)
    {
        var packet = CreateHdrSetPacket(target, enable);
        return DisplayNative.DisplayConfigSetDeviceInfo(ref packet);
    }

    internal static int WriteAdvancedColorState(DisplayHdrTarget target, bool enable)
    {
        var packet = CreateSetPacket(target, enable);
        return DisplayNative.DisplayConfigSetDeviceInfo(ref packet);
    }

    internal static DisplayNative.DisplayConfigSetHdrState CreateHdrSetPacket(
        DisplayHdrTarget target, bool enable) => new()
    {
        Header = DisplayNative.CreateHeader(
            DisplayNative.DeviceInfoSetHdrState,
            DisplayNative.SizeOf<DisplayNative.DisplayConfigSetHdrState>(),
            target.Target.AdapterId,
            target.Target.Id),
        Value = enable ? 1u : 0u
    };

    internal static DisplayNative.DisplayConfigSetAdvancedColorState CreateSetPacket(
        DisplayHdrTarget target, bool enable) => new()
    {
        Header = DisplayNative.CreateHeader(
            DisplayNative.DeviceInfoSetAdvancedColorState,
            DisplayNative.SizeOf<DisplayNative.DisplayConfigSetAdvancedColorState>(),
            target.Target.AdapterId,
            target.Target.Id),
        Value = enable ? 1u : 0u
    };

    private static DisplayHdrRead ReadAdvancedColorInfo2(DisplayNative.DisplayConfigPathTargetInfo target)
    {
        var packet = new DisplayNative.DisplayConfigGetAdvancedColorInfo2
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoGetAdvancedColorInfo2,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigGetAdvancedColorInfo2>(),
                target.AdapterId,
                target.Id)
        };
        var rc = DisplayNative.DisplayConfigGetDeviceInfo(ref packet);
        if (rc != DisplayNative.ErrorSuccess)
            return new DisplayHdrRead(false, null, rc);

        return new DisplayHdrRead(true, DisplayHdrState.DecodeHdr(packet.Value, packet.ActiveColorMode), 0);
    }

    private static DisplayHdrRead ReadAdvancedColorInfo(DisplayNative.DisplayConfigPathTargetInfo target)
    {
        var packet = new DisplayNative.DisplayConfigGetAdvancedColorInfo
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoGetAdvancedColorInfo,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigGetAdvancedColorInfo>(),
                target.AdapterId,
                target.Id)
        };
        var rc = DisplayNative.DisplayConfigGetDeviceInfo(ref packet);
        if (rc != DisplayNative.ErrorSuccess)
            return new DisplayHdrRead(false, null, rc);

        return new DisplayHdrRead(true, DisplayHdrState.Decode(packet.Value), 0);
    }

    private static bool ReadEdidSupport(string monitorPath)
    {
        try
        {
            return DisplayHdrCapabilityService.IsHdrSupportedByEdid(monitorPath);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
