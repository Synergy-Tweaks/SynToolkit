#nullable enable

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SynToolkit.Services.Mmcss
{
    /// <summary>
    /// Single source of truth for Multimedia Class Scheduler (SystemResponsiveness) presets
    /// and detection. No custom-value option.
    /// </summary>
    public static class SystemResponsivenessValues
    {
        public const uint DefaultSystemResponsiveness = 20;
        public const uint OptimizedSystemResponsiveness = 10;
        public const uint DisabledSystemResponsiveness = 100;

        public const string LabelDefault = "Default";
        public const string LabelOptimized = "Optimized";
        public const string LabelDisabled = "Disabled";
        public const string LabelUnknown = "Unknown";
        public const string LabelOther = "Other";

        public const string DisabledCaveat =
            "Disabled (100) is intended for systems with a NetAdapterCx driver and may cause audio popping.";

        public enum DetectionKind
        {
            Default,
            Preset,
            Unsupported,
            Error,
        }

        public sealed record PresetDefinition(string Label, uint Value);

        public sealed record DetectionResult(
            uint? RawValue,
            RegistryValueKind? ValueKind,
            string DisplayLabel,
            DetectionKind Kind,
            string? Warning);

        public static readonly IReadOnlyList<PresetDefinition> Presets = new[]
        {
            new PresetDefinition(LabelDefault, DefaultSystemResponsiveness),
            new PresetDefinition(LabelOptimized, OptimizedSystemResponsiveness),
            new PresetDefinition(LabelDisabled, DisabledSystemResponsiveness),
        };

        public static readonly IReadOnlyList<string> DropdownLabels = new[]
        {
            LabelDefault,
            LabelOptimized,
            LabelDisabled,
        };

        public static void EnsurePresetValuesUnique()
        {
            HashSet<uint> seen = new();
            foreach (PresetDefinition preset in Presets)
            {
                if (!seen.Add(preset.Value))
                {
                    throw new InvalidOperationException(
                        $"Duplicate SystemResponsiveness preset value {preset.Value} for '{preset.Label}'.");
                }
            }
        }

        public static uint? TryGetPresetValue(string label)
        {
            foreach (PresetDefinition preset in Presets)
            {
                if (string.Equals(preset.Label, label, StringComparison.Ordinal))
                {
                    return preset.Value;
                }
            }

            return null;
        }

        public static string FormatOtherLabel(uint rawValue) =>
            $"{LabelOther} ({rawValue.ToString(CultureInfo.InvariantCulture)})";

        /// <summary>
        /// Pure detection from a registry read result. Pass <paramref name="readSucceeded"/>=false
        /// for access-denied / unexpected read failures. Pass succeeded with null value for missing.
        /// </summary>
        public static DetectionResult DetectCurrentState(
            bool readSucceeded,
            object? registryValue,
            RegistryValueKind? valueKind)
        {
            if (!readSucceeded)
            {
                return new DetectionResult(
                    null,
                    valueKind,
                    LabelUnknown,
                    DetectionKind.Error,
                    "Couldn't read the current value.");
            }

            if (registryValue is null)
            {
                return new DetectionResult(
                    null,
                    null,
                    LabelDefault,
                    DetectionKind.Default,
                    Warning: null);
            }

            bool isDwordKind = valueKind is null or RegistryValueKind.DWord;
            bool isWrongRuntimeType = registryValue is string or byte[] or long or ulong;
            if (!isDwordKind || isWrongRuntimeType)
            {
                bool parsed = TryConvertToUInt32(registryValue, out uint shown);
                return new DetectionResult(
                    parsed ? shown : null,
                    valueKind,
                    parsed ? FormatOtherLabel(shown) : LabelOther,
                    DetectionKind.Unsupported,
                    parsed
                        ? $"Current value {shown.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write. Pick a preset to replace it."
                        : "Current value is not a REG_DWORD this tweak would write. Pick a preset to replace it.");
            }

            if (!TryConvertToUInt32(registryValue, out uint rawValue))
            {
                return new DetectionResult(
                    null,
                    valueKind,
                    LabelOther,
                    DetectionKind.Unsupported,
                    "Current value is not a REG_DWORD this tweak would write. Pick a preset to replace it.");
            }

            foreach (PresetDefinition preset in Presets)
            {
                if (preset.Value == rawValue)
                {
                    string? warning = preset.Label == LabelDisabled ? DisabledCaveat : null;
                    return new DetectionResult(
                        rawValue,
                        RegistryValueKind.DWord,
                        preset.Label,
                        DetectionKind.Preset,
                        warning);
                }
            }

            return new DetectionResult(
                rawValue,
                valueKind ?? RegistryValueKind.DWord,
                FormatOtherLabel(rawValue),
                DetectionKind.Unsupported,
                $"Current value {rawValue.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write. Pick a preset to replace it.");
        }

        /// <summary>
        /// Converts registry integer payloads to unsigned 32-bit, including signed -1 → 4294967295.
        /// </summary>
        public static bool TryConvertToUInt32(object registryValue, out uint value)
        {
            switch (registryValue)
            {
                case int intValue:
                    value = unchecked((uint)intValue);
                    return true;
                case uint uintValue:
                    value = uintValue;
                    return true;
                case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                    value = unchecked((uint)(int)longValue);
                    return true;
                case long longValue when longValue >= 0 && longValue <= uint.MaxValue:
                    value = (uint)longValue;
                    return true;
                case ulong ulongValue when ulongValue <= uint.MaxValue:
                    value = (uint)ulongValue;
                    return true;
                case short shortValue:
                    value = unchecked((uint)(int)shortValue);
                    return true;
                case ushort ushortValue:
                    value = ushortValue;
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
                case string text
                    when uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed):
                    value = parsed;
                    return true;
                case byte[] bytes when bytes.Length == 4:
                    value = BitConverter.ToUInt32(bytes, 0);
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}
