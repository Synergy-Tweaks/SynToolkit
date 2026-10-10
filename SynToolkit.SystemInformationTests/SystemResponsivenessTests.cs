using Microsoft.Win32;
using SynToolkit.Services.Mmcss;

namespace SynToolkit.SystemInformationTests;

internal static class SystemResponsivenessTests
{
    public static void PresetValuesAreUnique()
    {
        SystemResponsivenessValues.EnsurePresetValuesUnique();
    }

    public static void PresetMappingAndHex()
    {
        Equal(20u, SystemResponsivenessValues.DefaultSystemResponsiveness, "Default decimal.");
        Equal(10u, SystemResponsivenessValues.OptimizedSystemResponsiveness, "Optimized decimal.");
        Equal(100u, SystemResponsivenessValues.DisabledSystemResponsiveness, "Disabled decimal.");
        Equal(0x14u, SystemResponsivenessValues.DefaultSystemResponsiveness, "Default hex 0x14.");
        Equal(0xAu, SystemResponsivenessValues.OptimizedSystemResponsiveness, "Optimized hex 0xA.");
        Equal(0x64u, SystemResponsivenessValues.DisabledSystemResponsiveness, "Disabled hex 0x64.");
        Equal(20u, SystemResponsivenessValues.TryGetPresetValue("Default"), "Default label.");
        Equal(10u, SystemResponsivenessValues.TryGetPresetValue("Optimized"), "Optimized label.");
        Equal(100u, SystemResponsivenessValues.TryGetPresetValue("Disabled"), "Disabled label.");
    }

    public static void DropdownHasExactlyThreeItemsInOrder()
    {
        string[] expected = { "Default", "Optimized", "Disabled" };
        Equal(3, SystemResponsivenessValues.DropdownLabels.Count, "Exactly three items.");
        for (int i = 0; i < expected.Length; i++)
        {
            Equal(expected[i], SystemResponsivenessValues.DropdownLabels[i], $"Label {i}.");
            True(
                !string.Equals(expected[i], "Other", StringComparison.Ordinal)
                && !string.Equals(expected[i], "Unknown", StringComparison.Ordinal),
                "Other/Unknown must not be choosable.");
        }
    }

    public static void DetectionMissingAndPresets()
    {
        AssertDetection(true, null, null, "Default", SystemResponsivenessValues.DetectionKind.Default);
        AssertDetection(true, 20, RegistryValueKind.DWord, "Default", SystemResponsivenessValues.DetectionKind.Preset);
        AssertDetection(true, 10, RegistryValueKind.DWord, "Optimized", SystemResponsivenessValues.DetectionKind.Preset);
        AssertDetection(true, 100, RegistryValueKind.DWord, "Disabled", SystemResponsivenessValues.DetectionKind.Preset);
        True(
            !string.IsNullOrWhiteSpace(
                SystemResponsivenessValues.DetectCurrentState(true, 100, RegistryValueKind.DWord).Warning),
            "Disabled should surface the NetAdapterCx caveat.");
    }

    public static void DetectionUnsupportedNumbers()
    {
        object[] cases =
        {
            0,
            1,
            15,
            99,
            101,
            255,
            unchecked((int)4294967295u), // signed -1 / uint max
        };

        foreach (object boxed in cases)
        {
            var result = SystemResponsivenessValues.DetectCurrentState(true, boxed, RegistryValueKind.DWord);
            Equal(SystemResponsivenessValues.DetectionKind.Unsupported, result.Kind, $"Unsupported {boxed}.");
            True(result.DisplayLabel.StartsWith("Other", StringComparison.Ordinal), $"Other label for {boxed}.");
            True(!string.IsNullOrWhiteSpace(result.Warning), $"Warning for {boxed}.");
        }
    }

    public static void DetectionSignedMinusOneNormalizes()
    {
        var result = SystemResponsivenessValues.DetectCurrentState(true, -1, RegistryValueKind.DWord);
        Equal(4294967295u, result.RawValue, "Signed -1 becomes 4294967295.");
        Equal(SystemResponsivenessValues.DetectionKind.Unsupported, result.Kind, "Kind unsupported.");
    }

    public static void DetectionWrongTypesDoNotThrow()
    {
        var sz = SystemResponsivenessValues.DetectCurrentState(true, "20", RegistryValueKind.String);
        Equal(SystemResponsivenessValues.DetectionKind.Unsupported, sz.Kind, "REG_SZ unsupported.");

        var qword = SystemResponsivenessValues.DetectCurrentState(true, 20L, RegistryValueKind.QWord);
        Equal(SystemResponsivenessValues.DetectionKind.Unsupported, qword.Kind, "REG_QWORD unsupported.");

        var binary = SystemResponsivenessValues.DetectCurrentState(
            true,
            new byte[] { 20, 0, 0, 0 },
            RegistryValueKind.Binary);
        Equal(SystemResponsivenessValues.DetectionKind.Unsupported, binary.Kind, "REG_BINARY unsupported.");
    }

    public static void DetectionAccessDeniedIsErrorNotDefault()
    {
        var result = SystemResponsivenessValues.DetectCurrentState(false, null, null);
        Equal(SystemResponsivenessValues.DetectionKind.Error, result.Kind, "Access denied → error.");
        Equal("Unknown", result.DisplayLabel, "Unknown label.");
        True(result.Warning!.Contains("Couldn't read", StringComparison.Ordinal), "Read failure warning.");
        True(result.Kind != SystemResponsivenessValues.DetectionKind.Default, "Must not look like Default.");
    }

    public static void DetectionAlwaysUsesFreshInput()
    {
        var first = SystemResponsivenessValues.DetectCurrentState(true, 10, RegistryValueKind.DWord);
        Equal("Optimized", first.DisplayLabel, "First read Optimized.");

        var second = SystemResponsivenessValues.DetectCurrentState(true, 100, RegistryValueKind.DWord);
        Equal("Disabled", second.DisplayLabel, "Second read reflects new value.");
        True(first.DisplayLabel != second.DisplayLabel, "Fresh read must not reuse prior result.");
    }

    public static void UIntConversionHandlesSignedNegativeOne()
    {
        True(SystemResponsivenessValues.TryConvertToUInt32(-1, out uint value), "Convert -1.");
        Equal(4294967295u, value, "Unchecked cast.");
    }

    private static void AssertDetection(
        bool readSucceeded,
        object? raw,
        RegistryValueKind? kind,
        string expectedLabel,
        SystemResponsivenessValues.DetectionKind expectedKind)
    {
        var result = SystemResponsivenessValues.DetectCurrentState(readSucceeded, raw, kind);
        Equal(expectedLabel, result.DisplayLabel, $"Label for {raw ?? "null"}.");
        Equal(expectedKind, result.Kind, $"Kind for {raw ?? "null"}.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{message} Expected <{expected}>, actual <{actual}>.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
