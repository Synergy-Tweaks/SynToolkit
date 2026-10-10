using Microsoft.Win32;
using SynToolkit.Services;

namespace SynToolkit.SystemInformationTests;

internal static class HagsDetectionTests
{
    private const int Win11 = 26100;
    private const int Win2004 = HagsDetection.MinimumWindowsBuild;
    private const int Win1909 = 18363;

    public static void ClassificationRules()
    {
        AssertState(Win11, true, 2, RegistryValueKind.DWord, HagsSupportState.On, "2 → on");
        AssertState(Win11, true, 1, RegistryValueKind.DWord, HagsSupportState.Off, "1 → off");
        AssertState(Win11, true, null, null, HagsSupportState.Default, "missing → default");
        AssertState(Win11, true, 0, RegistryValueKind.DWord, HagsSupportState.UnsupportedValue, "0 → unsupported");
        AssertState(Win11, true, 3, RegistryValueKind.DWord, HagsSupportState.UnsupportedValue, "3 → unsupported");
        AssertState(Win11, true, unchecked((int)4294967295), RegistryValueKind.DWord, HagsSupportState.UnsupportedValue, "4294967295 → unsupported");
        AssertState(Win11, true, -1, RegistryValueKind.DWord, HagsSupportState.UnsupportedValue, "signed -1 → unsupported");

        var sz = HagsDetection.DetectCurrentState(Win11, true, "2", RegistryValueKind.String);
        Equal(HagsSupportState.UnsupportedValue, sz.State, "REG_SZ → unsupported-value");

        var qword = HagsDetection.DetectCurrentState(Win11, true, 2L, RegistryValueKind.QWord);
        Equal(HagsSupportState.UnsupportedValue, qword.State, "REG_QWORD → unsupported-value");

        var binary = HagsDetection.DetectCurrentState(
            Win11, true, new byte[] { 2, 0, 0, 0 }, RegistryValueKind.Binary);
        Equal(HagsSupportState.UnsupportedValue, binary.State, "REG_BINARY → unsupported-value");

        var error = HagsDetection.DetectCurrentState(Win11, false, null, null);
        Equal(HagsSupportState.Error, error.State, "access denied → error");

        var hw = HagsDetection.DetectCurrentState(
            Win11, true, 2, RegistryValueKind.DWord, hardwareSupported: false);
        Equal(HagsSupportState.UnsupportedHardware, hw.State, "unsupported hardware");

        var restart = HagsDetection.DetectCurrentState(
            Win11, true, 2, RegistryValueKind.DWord, effectiveEnabled: false);
        True(restart.RestartRequired, "requested ≠ effective → restart required");

        var sessionRestart = HagsDetection.DetectCurrentState(
            Win11, true, 2, RegistryValueKind.DWord, wroteThisSession: true);
        True(sessionRestart.RestartRequired, "session write → restart required");

        Equal(
            HagsSupportState.NotSupportedByWindowsVersion,
            HagsDetection.DetectCurrentState(Win1909, true, null, null).State,
            "pre-2004 OS");

        True(HagsDetection.CanToggle(HagsSupportState.Default), "default is toggleable");
        True(HagsDetection.CanToggle(HagsSupportState.On), "on is toggleable");
        True(!HagsDetection.CanToggle(HagsSupportState.UnsupportedValue), "unsupported not toggleable");
        True(!HagsDetection.CanToggle(HagsSupportState.Error), "error not toggleable");

        // Fresh read: changing inputs between calls changes the result.
        var first = HagsDetection.DetectCurrentState(Win11, true, 2, RegistryValueKind.DWord);
        Equal(HagsSupportState.On, first.State, "first read on");
        var second = HagsDetection.DetectCurrentState(Win11, true, 1, RegistryValueKind.DWord);
        Equal(HagsSupportState.Off, second.State, "second read off");

        // Legacy Classify wrapper
        Equal(HagsSupportState.Default, HagsDetection.Classify(Win2004, null), "legacy missing → default");
        Equal(HagsSupportState.On, HagsDetection.Classify(Win11, 2), "legacy 2 → on");
        Equal(HagsSupportState.Off, HagsDetection.Classify(Win11, 1), "legacy 1 → off");
    }

    public static void WriteTargetsAreOneAndTwo()
    {
        Equal(1u, HagsDetection.HwSchModeOff, "OFF writes 1");
        Equal(2u, HagsDetection.HwSchModeOn, "ON writes 2");
    }

    private static void AssertState(
        int build,
        bool readSucceeded,
        object? raw,
        RegistryValueKind? kind,
        HagsSupportState expected,
        string message)
    {
        var result = HagsDetection.DetectCurrentState(build, readSucceeded, raw, kind);
        Equal(expected, result.State, message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected: {expected}, Actual: {actual}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
