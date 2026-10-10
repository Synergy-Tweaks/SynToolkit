#nullable enable

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SynToolkit.Services.Taskbar
{
    /// <summary>
    /// Windows 11 taskbar alignment (TaskbarAl). Constants are the single source of truth
    /// for UI, writer, detection, and tests.
    /// </summary>
    public static class TaskbarAlignmentValues
    {
        public const uint TASKBAR_ALIGN_CENTERED = 1;
        public const uint TASKBAR_ALIGN_LEFT = 0;

        public const string KeyPath =
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        public const string ValueName = "TaskbarAl";

        public const string LabelCentered = "Centered";
        public const string LabelLeft = "Left";
        public const string LabelUnknown = "Unknown";
        public const string LabelOther = "Other";

        public const int MinimumWindows11Build = 22000;

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
            new PresetDefinition(LabelCentered, TASKBAR_ALIGN_CENTERED),
            new PresetDefinition(LabelLeft, TASKBAR_ALIGN_LEFT),
        };

        public static readonly IReadOnlyList<string> DropdownLabels = new[]
        {
            LabelCentered,
            LabelLeft,
        };

        public static void EnsurePresetValuesUnique()
        {
            HashSet<uint> seen = new();
            foreach (PresetDefinition preset in Presets)
            {
                if (!seen.Add(preset.Value))
                {
                    throw new InvalidOperationException(
                        $"Duplicate TaskbarAl preset value {preset.Value} for '{preset.Label}'.");
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

        public static bool IsWindows11OrLater(int windowsBuild) =>
            windowsBuild >= MinimumWindows11Build;

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
                    LabelCentered,
                    DetectionKind.Default,
                    Warning: null);
            }

            bool isDwordKind = valueKind is null or RegistryValueKind.DWord;
            // REG_SZ "1" and other non-DWORD payloads are unsupported even if numeric-looking.
            bool isWrongRuntimeType = registryValue is string or byte[] or long or ulong;
            if (!isDwordKind || isWrongRuntimeType)
            {
                bool parsed = HagsDetection.TryConvertToUInt32(registryValue, out uint shown);
                return new DetectionResult(
                    parsed ? shown : null,
                    valueKind,
                    parsed ? FormatOtherLabel(shown) : LabelOther,
                    DetectionKind.Unsupported,
                    parsed
                        ? $"Current value {shown.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write. Pick an option to replace it."
                        : "Current value is not a REG_DWORD this tweak would write. Pick an option to replace it.");
            }

            if (!HagsDetection.TryConvertToUInt32(registryValue, out uint rawValue))
            {
                return new DetectionResult(
                    null,
                    valueKind,
                    LabelOther,
                    DetectionKind.Unsupported,
                    "Current value is not a REG_DWORD this tweak would write. Pick an option to replace it.");
            }

            foreach (PresetDefinition preset in Presets)
            {
                if (preset.Value == rawValue)
                {
                    // Explicit DWORD is Preset; missing alone is Default (handled above).
                    return new DetectionResult(
                        rawValue,
                        RegistryValueKind.DWord,
                        preset.Label,
                        DetectionKind.Preset,
                        Warning: null);
                }
            }

            return new DetectionResult(
                rawValue,
                valueKind ?? RegistryValueKind.DWord,
                FormatOtherLabel(rawValue),
                DetectionKind.Unsupported,
                $"Current value {rawValue.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write. Pick an option to replace it.");
        }
    }
}
