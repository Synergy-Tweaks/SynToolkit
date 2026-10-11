using Microsoft.Win32;
using SynToolkit.Services;

namespace SynToolkit.SystemInformationTests;

/// <summary>
/// Tests for Full Screen Optimizations detection logic without touching real registry.
/// </summary>
internal static class FsoDetectionTests
{
    public static void AllSixZeroAndCompatLayerMissing_IsEnabled()
    {
        var hkcu = CreateHkcuValues(0, 0, 0, 0, 0, 0);
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Enabled, result.State, "All six = 0 and __COMPAT_LAYER missing → Enabled");
        True(result.Warning is null, "No warning for clean enabled state");
    }

    public static void AllSixMissingAndCompatLayerMissing_IsEnabled()
    {
        var hkcu = CreateHkcuValues(null, null, null, null, null, null);
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Enabled, result.State, "All six missing and __COMPAT_LAYER missing → Enabled");
    }

    public static void FseBehaviorMode2_IsDisabled()
    {
        // Only GameDVR_FSEBehaviorMode = 2, others at 0 or missing
        var hkcu = CreateHkcuValues(0, 2, 0, 0, 0, 0);
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Disabled, result.State, "GameDVR_FSEBehaviorMode = 2 → Disabled");
    }

    public static void EachValueNonZero_IsModified()
    {
        string[] names = FsoDetection.GameConfigStoreValueNames;

        for (int i = 0; i < names.Length; i++)
        {
            var values = new uint?[] { 0, 0, 0, 0, 0, 0 };
            values[i] = 1; // Set one value to non-zero (but not 2 for FSEBehaviorMode)

            // Skip FSEBehaviorMode since 2 means Disabled
            if (names[i] == "GameDVR_FSEBehaviorMode")
            {
                values[i] = 3; // Use 3 instead of 2 to avoid Disabled detection
            }

            var hkcu = CreateHkcuValues(values[0], values[1], values[2], values[3], values[4], values[5]);
            var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

            var result = FsoDetection.DetectFromValues(hkcu, compat);

            Equal(FsoState.Modified, result.State, $"{names[i]} non-zero → Modified");
            True(result.Warning?.Contains(names[i]) == true, $"Warning should mention {names[i]}");
        }
    }

    public static void CompatLayerPresent_IsModified()
    {
        var hkcu = CreateHkcuValues(0, 0, 0, 0, 0, 0);
        var compat = (
            Value: (object?)"~ DISABLEDXMAXIMIZEDWINDOWEDMODE SomeOtherFlag",
            Kind: (RegistryValueKind?)RegistryValueKind.String,
            ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Modified, result.State, "__COMPAT_LAYER present → Modified");
        True(result.Warning?.Contains("__COMPAT_LAYER") == true, "Warning should mention __COMPAT_LAYER");
    }

    public static void WrongKindString_IsModified()
    {
        // REG_SZ "0" instead of REG_DWORD 0
        var hkcu = new Dictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)>
        {
            ["GameDVR_EFSEFeatureFlags"] = ("0", RegistryValueKind.String, true),
            ["GameDVR_FSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_FSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DXGIHonorFSEWindowsCompatible"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_HonorUserFSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
        };
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Modified, result.State, "REG_SZ '0' → Modified (wrong kind)");
    }

    public static void WrongKindQWord_IsModified()
    {
        var hkcu = new Dictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)>
        {
            ["GameDVR_EFSEFeatureFlags"] = (0L, RegistryValueKind.QWord, true),
            ["GameDVR_FSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_FSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DXGIHonorFSEWindowsCompatible"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_HonorUserFSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
        };
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Modified, result.State, "REG_QWORD → Modified (wrong kind)");
    }

    public static void SignedNegativeOne_IsModified()
    {
        // int -1 should convert to uint 4294967295, which is not 0
        var hkcu = new Dictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)>
        {
            ["GameDVR_EFSEFeatureFlags"] = (-1, RegistryValueKind.DWord, true),
            ["GameDVR_FSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_FSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DXGIHonorFSEWindowsCompatible"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_HonorUserFSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
        };
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Modified, result.State, "int -1 → uint 4294967295 → Modified");
    }

    public static void AccessDeniedOnRead_IsError()
    {
        var hkcu = new Dictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)>
        {
            ["GameDVR_EFSEFeatureFlags"] = (null, null, false), // Read failed
            ["GameDVR_FSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_FSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DSEBehavior"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_DXGIHonorFSEWindowsCompatible"] = (0, RegistryValueKind.DWord, true),
            ["GameDVR_HonorUserFSEBehaviorMode"] = (0, RegistryValueKind.DWord, true),
        };
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Error, result.State, "Access denied on read → Error");
    }

    public static void CompatLayerReadFailed_IsError()
    {
        var hkcu = CreateHkcuValues(0, 0, 0, 0, 0, 0);
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: false);

        var result = FsoDetection.DetectFromValues(hkcu, compat);

        Equal(FsoState.Error, result.State, "__COMPAT_LAYER read failed → Error");
    }

    public static void FreshReadEveryCall()
    {
        // First call: all zeros
        var hkcu1 = CreateHkcuValues(0, 0, 0, 0, 0, 0);
        var compat = (Value: (object?)null, Kind: (RegistryValueKind?)null, ReadSucceeded: true);
        var result1 = FsoDetection.DetectFromValues(hkcu1, compat);
        Equal(FsoState.Enabled, result1.State, "First read: Enabled");

        // Second call: FSEBehaviorMode = 2
        var hkcu2 = CreateHkcuValues(0, 2, 0, 0, 0, 0);
        var result2 = FsoDetection.DetectFromValues(hkcu2, compat);
        Equal(FsoState.Disabled, result2.State, "Second read: Disabled");

        // Third call: Modified
        var hkcu3 = CreateHkcuValues(1, 0, 0, 0, 0, 0);
        var result3 = FsoDetection.DetectFromValues(hkcu3, compat);
        Equal(FsoState.Modified, result3.State, "Third read: Modified");
    }

    public static void ConstantsAreCorrect()
    {
        Equal(6, FsoDetection.GameConfigStoreValueNames.Length, "Exactly 6 HKCU values");
        Equal(0u, FsoDetection.EnabledValue, "Enabled value is 0");
        Equal(2u, FsoDetection.DisabledModeValue, "Disabled mode value is 2");
        Equal("__COMPAT_LAYER", FsoDetection.CompatLayerValueName, "__COMPAT_LAYER name");
    }

    #region Helpers

    private static Dictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)> CreateHkcuValues(
        uint? v0, uint? v1, uint? v2, uint? v3, uint? v4, uint? v5)
    {
        var names = FsoDetection.GameConfigStoreValueNames;
        var values = new uint?[] { v0, v1, v2, v3, v4, v5 };
        var result = new Dictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)>();

        for (int i = 0; i < names.Length; i++)
        {
            if (values[i] is uint val)
            {
                result[names[i]] = (unchecked((int)val), RegistryValueKind.DWord, true);
            }
            else
            {
                result[names[i]] = (null, null, true);
            }
        }

        return result;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} — Expected: {expected}, Actual: {actual}");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    #endregion
}
