#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SynToolkit.Services.Win32Priority
{
    /// <summary>
    /// Single source of truth for Foreground App Boost (Win32PrioritySeparation) presets,
    /// custom-allowed values, detection, and validation.
    /// </summary>
    public static class Win32PrioritySeparationValues
    {
        /// <summary>Windows 10/11 client default.</summary>
        public const int DefaultWin32PrioritySeparation = 2;

        /// <summary>
        /// Explicit "No Boost" encoding (all scheduling fields left at zero / equal).
        /// SynToolKit had no prior No Boost write; 0 is the documented equal-priority encoding.
        /// </summary>
        public const int NoBoostValue = 0;

        public static readonly IReadOnlyList<int> CustomAllowedValues = new[]
        {
            20, 21, 22, 24, 25, 26, 36, 37, 38, 40, 41, 42,
        };

        public const string LabelDefault = "Default";
        public const string LabelNoBoost = "No Boost";
        public const string LabelDoubleBoost = "Double Boost";
        public const string LabelTripleBoost = "Triple Boost";
        public const string LabelLongQuantum = "Long Quantum";
        public const string LabelShortQuantum = "Short Quantum";
        public const string LabelVariableQuantum = "Variable Quantum";
        public const string LabelCustomValue = "Custom Value";

        public enum DetectionKind
        {
            Default,
            Preset,
            Custom,
            Unsupported,
        }

        public sealed record PresetDefinition(string Label, int Value);

        public sealed record DetectionResult(
            int? RawValue,
            string PresetLabel,
            DetectionKind Kind,
            string? Warning);

        /// <summary>
        /// Preset table in dropdown order. Custom Value is not included (no fixed decimal).
        /// </summary>
        public static readonly IReadOnlyList<PresetDefinition> Presets = new[]
        {
            new PresetDefinition(LabelDefault, DefaultWin32PrioritySeparation),
            new PresetDefinition(LabelNoBoost, NoBoostValue),
            new PresetDefinition(LabelDoubleBoost, 21),
            new PresetDefinition(LabelTripleBoost, 22),
            new PresetDefinition(LabelLongQuantum, 24),
            new PresetDefinition(LabelShortQuantum, 40),
            new PresetDefinition(LabelVariableQuantum, 36),
        };

        public static readonly IReadOnlyList<string> DropdownLabels = new[]
        {
            LabelDefault,
            LabelNoBoost,
            LabelDoubleBoost,
            LabelTripleBoost,
            LabelLongQuantum,
            LabelShortQuantum,
            LabelVariableQuantum,
            LabelCustomValue,
        };

        public static int CustomOptionIndex => DropdownLabels.Count - 1;

        public static string BuildCustomAllowedValuesHelperText()
        {
            string list = string.Join(", ", CustomAllowedValues);
            return "Type a whole number for Win32PrioritySeparation. SynToolkit accepts only "
                + list
                + ". Anything outside that set is ignored or rewritten by Windows, so the result would not match what you entered.";
        }

        public static string FormatCustomLabel(int rawValue) =>
            $"{LabelCustomValue} ({rawValue.ToString(CultureInfo.InvariantCulture)})";

        public static bool IsCustomLabel(string? label) =>
            !string.IsNullOrWhiteSpace(label)
            && label.StartsWith(LabelCustomValue, StringComparison.Ordinal);

        public static int? TryGetPresetValue(string label)
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

        /// <summary>
        /// Detects UI state from a registry read. <paramref name="registryValue"/> is the raw
        /// object from the registry (null when missing), not a coerced int.
        /// </summary>
        public static DetectionResult DetectCurrentState(object? registryValue)
        {
            if (registryValue is null)
            {
                return new DetectionResult(
                    null,
                    LabelDefault,
                    DetectionKind.Default,
                    Warning: null);
            }

            if (!TryConvertToInt32(registryValue, out int rawValue))
            {
                return new DetectionResult(
                    null,
                    LabelCustomValue,
                    DetectionKind.Unsupported,
                    "Current value is not a REG_DWORD this tweak understands. Pick a preset or Default to replace it.");
            }

            foreach (PresetDefinition preset in Presets)
            {
                if (preset.Value == rawValue)
                {
                    DetectionKind kind = preset.Value == DefaultWin32PrioritySeparation
                        ? DetectionKind.Default
                        : DetectionKind.Preset;
                    return new DetectionResult(rawValue, preset.Label, kind, Warning: null);
                }
            }

            if (CustomAllowedValues.Contains(rawValue))
            {
                return new DetectionResult(
                    rawValue,
                    FormatCustomLabel(rawValue),
                    DetectionKind.Custom,
                    Warning: null);
            }

            return new DetectionResult(
                rawValue,
                FormatCustomLabel(rawValue),
                DetectionKind.Unsupported,
                $"Current value {rawValue.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write. Pick a preset or Default to replace it.");
        }

        public static bool TryValidateCustomInput(string? input, out int value, out string? error)
        {
            value = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Add a whole number first.";
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.Contains('.', StringComparison.Ordinal)
                || trimmed.Contains(',', StringComparison.Ordinal))
            {
                error = "Only whole numbers are allowed.";
                return false;
            }

            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && !int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            {
                error = "That does not look like a whole number.";
                return false;
            }

            if (value < 0)
            {
                error = "Negative numbers are not supported.";
                return false;
            }

            if (value == DefaultWin32PrioritySeparation)
            {
                error = "Pick Default in the dropdown to restore 2.";
                return false;
            }

            if (!CustomAllowedValues.Contains(value))
            {
                error = "That number is outside the accepted set.";
                return false;
            }

            return true;
        }

        public static int PrefillCustomDialogValue(DetectionResult detection)
        {
            if (detection.RawValue is int raw && CustomAllowedValues.Contains(raw))
            {
                return raw;
            }

            return 36;
        }

        public static void EnsurePresetValuesUnique()
        {
            HashSet<int> seen = new();
            foreach (PresetDefinition preset in Presets)
            {
                if (!seen.Add(preset.Value))
                {
                    throw new InvalidOperationException(
                        $"Duplicate Win32PrioritySeparation preset value {preset.Value} for '{preset.Label}'.");
                }
            }
        }

        private static bool TryConvertToInt32(object registryValue, out int value)
        {
            switch (registryValue)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case uint uintValue when uintValue <= int.MaxValue:
                    value = (int)uintValue;
                    return true;
                case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                    value = (int)longValue;
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case string text
                    when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                    value = parsed;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}
