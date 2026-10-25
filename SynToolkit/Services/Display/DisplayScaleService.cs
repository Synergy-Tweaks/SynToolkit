#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services.Display;

public sealed record DisplayScaleResult(
    bool Success,
    string Message,
    int RequestedStep,
    int AppliedStep,
    int ReadReturnCode,
    int SetReturnCode,
    int VerifyReturnCode);

public static class DisplayScaleService
{
    internal const int VerifyAttempts = 12;

    internal const int VerifyDelayMs = 250;

    public const string AlreadyActiveMessage = "That scale is already active. Nothing changed.";

    public static string RevertFailureMessage() =>
        "Couldn't revert the scale change. Press Revert again to try.";

    internal static Task<DisplayScaleResult> SetStepAsync(
        DisplayNative.DisplayConfigPathSourceInfo source, int requestedStep, int previousStep) =>
        Task.Run(() => ExecuteScaleChange(
            requestedStep,
            previousStep,
            () => DisplayService.GetScaleTriple(source),
            step => WriteStep(source, step),
            VerifyAttempts,
            VerifyDelayMs));

    internal static DisplayScaleResult ExecuteScaleChange(
        int requestedStep,
        int previousStep,
        Func<DisplayScaleTriple> readTriple,
        Func<int, int> writeStep,
        int verifyAttempts = 1,
        int verifyDelayMs = 0)
    {
        ArgumentNullException.ThrowIfNull(readTriple);
        ArgumentNullException.ThrowIfNull(writeStep);

        var triple = readTriple();
        if (!triple.Success)
            return new DisplayScaleResult(false, "Couldn't read the current scale. Nothing changed.", requestedStep, triple.CurRelativeScaleStep, triple.ReturnCode, 0, 0);

        if (triple.MaxRelativeScaleStep < triple.MinRelativeScaleStep)
            return new DisplayScaleResult(false, "Couldn't read the current scale (the reported range was empty). Nothing changed.", requestedStep, triple.CurRelativeScaleStep, triple.ReturnCode, 0, 0);

        var clamped = DisplayScaleOptions.ClampStep(requestedStep, triple.MinRelativeScaleStep, triple.MaxRelativeScaleStep);
        if (clamped == triple.CurRelativeScaleStep)
            return new DisplayScaleResult(true, AlreadyActiveMessage, requestedStep, clamped, 0, 0, 0);

        var setRc = writeStep(clamped);
        if (setRc != DisplayNative.ErrorSuccess)
            return new DisplayScaleResult(false, "Couldn't change the scale. No further change was attempted.", requestedStep, triple.CurRelativeScaleStep, 0, setRc, 0);

        var verify = ReadUntilStep(readTriple, clamped, verifyAttempts, verifyDelayMs);
        if (verify.Success && verify.CurRelativeScaleStep == clamped)
            return new DisplayScaleResult(true, "Scale changed.", requestedStep, clamped, 0, 0, 0);

        var seen = verify.Success ? verify.CurRelativeScaleStep : triple.CurRelativeScaleStep;
        var verifyRc = verify.Success ? 0 : verify.ReturnCode;
        writeStep(previousStep);
        var reverify = ReadUntilStep(readTriple, previousStep, verifyAttempts, verifyDelayMs);
        var restored = reverify.Success && reverify.CurRelativeScaleStep == previousStep;
        var applied = reverify.Success ? reverify.CurRelativeScaleStep : previousStep;
        var tail = restored
            ? "The previous step was written back and confirmed."
            : "The previous step was written back, but it also didn't read back. Check Settings on the Display page.";
        return new DisplayScaleResult(false, $"The scale didn't stay at the requested step (read back {seen} instead of {clamped}). {tail}", requestedStep, applied, 0, 0, verifyRc);
    }

    internal static DisplayScaleTriple ReadUntilStep(
        Func<DisplayScaleTriple> readTriple,
        int expectedStep,
        int attempts,
        int delayMs)
    {
        var last = readTriple();
        for (var i = 1; i < attempts; i++)
        {
            if (last.Success && last.CurRelativeScaleStep == expectedStep)
                break;

            if (delayMs > 0)
                Thread.Sleep(delayMs);
            last = readTriple();
        }

        return last;
    }

    internal static int WriteStep(DisplayNative.DisplayConfigPathSourceInfo source, int step)
    {
        var packet = CreateSetPacket(source, step);
        return DisplayNative.DisplayConfigSetDeviceInfo(ref packet);
    }

    internal static DisplayNative.DisplayConfigSetSourceDpiScale CreateSetPacket(
        DisplayNative.DisplayConfigPathSourceInfo source, int step) => new()
        {
            Header = DisplayNative.CreateHeader(
                DisplayNative.DeviceInfoSetSourceDpiScale,
                DisplayNative.SizeOf<DisplayNative.DisplayConfigSetSourceDpiScale>(),
                source.AdapterId,
                source.Id),
            RelativeScaleStep = step
        };
}
