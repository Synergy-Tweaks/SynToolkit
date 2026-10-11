#nullable enable

using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Services
{
    /// <summary>
    /// Full Screen Optimizations state classifications.
    /// </summary>
    public enum FsoState
    {
        Enabled,
        Disabled,
        Modified,
        Error
    }

    /// <summary>
    /// Detection result for Full Screen Optimizations.
    /// </summary>
    public readonly record struct FsoDetectionResult(FsoState State, string? Warning);

    /// <summary>
    /// Pure detection logic for Full Screen Optimizations. Testable without touching the registry.
    /// Owns seven registry states:
    /// - Six HKCU DWORDs under System\GameConfigStore
    /// - HKLM Environment\__COMPAT_LAYER (machine-wide)
    /// 
    /// Does NOT touch GameDVR_Enabled, AppCaptureEnabled, UseNexusForGameBarEnabled,
    /// AllowGameDVR, or any other Xbox Game Bar / HAGS / MPO values.
    /// </summary>
    public static class FsoDetection
    {
        /// <summary>
        /// The six HKCU GameConfigStore values managed by the FSO toggle.
        /// </summary>
        public static readonly string[] GameConfigStoreValueNames =
        [
            "GameDVR_EFSEFeatureFlags",
            "GameDVR_FSEBehaviorMode",
            "GameDVR_FSEBehavior",
            "GameDVR_DSEBehavior",
            "GameDVR_DXGIHonorFSEWindowsCompatible",
            "GameDVR_HonorUserFSEBehaviorMode"
        ];

        public const string GameConfigStoreKeyPath = @"HKCU\System\GameConfigStore";
        public const string EnvironmentKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        public const string EnvironmentSubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        public const string CompatLayerValueName = "__COMPAT_LAYER";
        public const uint EnabledValue = 0;
        public const uint DisabledModeValue = 2;

        /// <summary>
        /// Classifies FSO state from provided registry values without reading the registry.
        /// Each HKCU value should be a tuple of (rawValue, kind, readSucceeded).
        /// </summary>
        public static FsoDetectionResult DetectFromValues(
            IReadOnlyDictionary<string, (object? Value, RegistryValueKind? Kind, bool ReadSucceeded)> hkcuValues,
            (object? Value, RegistryValueKind? Kind, bool ReadSucceeded) compatLayer)
        {
            List<string> mismatchedValues = [];

            // Check all six HKCU values
            foreach (string valueName in GameConfigStoreValueNames)
            {
                if (!hkcuValues.TryGetValue(valueName, out var entry))
                {
                    // Missing from input = assume read error
                    return new FsoDetectionResult(FsoState.Error, $"Missing input for {valueName}");
                }

                var check = CheckHkcuValuePure(valueName, entry.Value, entry.Kind, entry.ReadSucceeded);
                if (check == FsoValueCheckResult.ReadError)
                {
                    return new FsoDetectionResult(FsoState.Error, "Registry read failed.");
                }
                if (check == FsoValueCheckResult.Mismatch)
                {
                    mismatchedValues.Add(valueName);
                }
            }

            // Check __COMPAT_LAYER
            var compatCheck = CheckCompatLayerPure(compatLayer.Value, compatLayer.Kind, compatLayer.ReadSucceeded);
            if (compatCheck == FsoValueCheckResult.ReadError)
            {
                return new FsoDetectionResult(FsoState.Error, "Registry read failed for __COMPAT_LAYER.");
            }
            if (compatCheck == FsoValueCheckResult.Mismatch)
            {
                mismatchedValues.Add(CompatLayerValueName);
            }

            // If all seven match the Enabled position
            if (mismatchedValues.Count == 0)
            {
                return new FsoDetectionResult(FsoState.Enabled, null);
            }

            // Check if this is the Disabled position (only GameDVR_FSEBehaviorMode = 2)
            if (hkcuValues.TryGetValue("GameDVR_FSEBehaviorMode", out var fseEntry) &&
                IsDisabledValue(fseEntry.Value, fseEntry.Kind))
            {
                return new FsoDetectionResult(FsoState.Disabled, null);
            }

            // Modified state
            string warning = mismatchedValues.Count <= 3
                ? $"Modified: {string.Join(", ", mismatchedValues)}"
                : $"Modified: {string.Join(", ", mismatchedValues.Take(3))} and {mismatchedValues.Count - 3} more";

            return new FsoDetectionResult(FsoState.Modified, warning);
        }

        private static FsoValueCheckResult CheckHkcuValuePure(
            string valueName,
            object? raw,
            RegistryValueKind? kind,
            bool readSucceeded)
        {
            if (!readSucceeded)
            {
                return FsoValueCheckResult.ReadError;
            }

            // Missing is treated as 0 (matches enabled)
            if (raw is null)
            {
                return FsoValueCheckResult.Match;
            }

            // Must be DWORD
            if (kind is not null and not RegistryValueKind.DWord)
            {
                return FsoValueCheckResult.Mismatch;
            }

            // Convert and check value
            if (!HagsDetection.TryConvertToUInt32(raw, out uint value))
            {
                return FsoValueCheckResult.Mismatch;
            }

            return value == EnabledValue ? FsoValueCheckResult.Match : FsoValueCheckResult.Mismatch;
        }

        private static FsoValueCheckResult CheckCompatLayerPure(
            object? value,
            RegistryValueKind? kind,
            bool readSucceeded)
        {
            if (!readSucceeded)
            {
                return FsoValueCheckResult.ReadError;
            }

            // Missing = matches enabled; any value present = mismatch
            return value is null ? FsoValueCheckResult.Match : FsoValueCheckResult.Mismatch;
        }

        private static bool IsDisabledValue(object? raw, RegistryValueKind? kind)
        {
            if (raw is null || kind is not null and not RegistryValueKind.DWord)
            {
                return false;
            }

            return HagsDetection.TryConvertToUInt32(raw, out uint value) && value == DisabledModeValue;
        }

        private enum FsoValueCheckResult
        {
            Match,
            Mismatch,
            ReadError
        }
    }
}
