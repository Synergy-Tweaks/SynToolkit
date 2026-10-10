#nullable enable

using Microsoft.Win32;
using System.Globalization;

namespace SynToolkit.Services
{
    /// <summary>
    /// Requested HAGS states derived from HwSchMode. Missing is Windows/driver default,
    /// not the same as Off, and not evidence that the GPU lacks support.
    /// </summary>
    public enum HagsSupportState
    {
        On,
        Off,
        Default,
        UnsupportedHardware,
        UnsupportedValue,
        Error,
        NotSupportedByWindowsVersion,
    }

    public readonly record struct HagsDetectionResult(
        HagsSupportState State,
        uint? HwSchMode,
        int WindowsBuild,
        bool RestartRequired = false);

    /// <summary>
    /// Classifies Hardware-accelerated GPU scheduling (HAGS) from the OS build and the
    /// HwSchMode DWORD under HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers.
    /// 2 = enabled (requested), 1 = disabled (requested), missing = Default (system decides).
    /// Effective driver state requires a restart to match; optional caps can refine display.
    /// </summary>
    public static class HagsDetection
    {
        public const int MinimumWindowsBuild = 19041;

        public const string GraphicsDriversKeyPath =
            @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public const string GraphicsDriversSubKey =
            @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

        public const string HwSchModeValueName = "HwSchMode";

        public const uint HwSchModeOff = 1;
        public const uint HwSchModeOn = 2;

        /// <summary>
        /// Pure detection from a registry read. Pass <paramref name="readSucceeded"/>=false
        /// for access-denied / unexpected failures. Pass succeeded with null value for missing.
        /// Optional <paramref name="hardwareSupported"/> / <paramref name="effectiveEnabled"/>
        /// come from a driver-capability query when available.
        /// </summary>
        public static HagsDetectionResult DetectCurrentState(
            int windowsBuild,
            bool readSucceeded,
            object? registryValue,
            RegistryValueKind? valueKind,
            bool? hardwareSupported = null,
            bool? effectiveEnabled = null,
            bool wroteThisSession = false)
        {
            if (windowsBuild < MinimumWindowsBuild)
            {
                return new HagsDetectionResult(
                    HagsSupportState.NotSupportedByWindowsVersion,
                    null,
                    windowsBuild);
            }

            if (!readSucceeded)
            {
                return new HagsDetectionResult(HagsSupportState.Error, null, windowsBuild);
            }

            if (hardwareSupported == false)
            {
                return new HagsDetectionResult(
                    HagsSupportState.UnsupportedHardware,
                    null,
                    windowsBuild);
            }

            if (registryValue is null)
            {
                if (effectiveEnabled is bool effective)
                {
                    HagsSupportState fromEffective = effective
                        ? HagsSupportState.On
                        : HagsSupportState.Off;
                    return new HagsDetectionResult(
                        fromEffective,
                        null,
                        windowsBuild,
                        RestartRequired: false);
                }

                return new HagsDetectionResult(
                    HagsSupportState.Default,
                    null,
                    windowsBuild,
                    RestartRequired: wroteThisSession);
            }

            bool isDwordKind = valueKind is null or RegistryValueKind.DWord;
            bool isWrongRuntimeType = registryValue is string or byte[] or long or ulong;
            if (!isDwordKind || isWrongRuntimeType || !TryConvertToUInt32(registryValue, out uint rawValue))
            {
                uint? shown = TryConvertToUInt32(registryValue, out uint parsed) ? parsed : null;
                return new HagsDetectionResult(
                    HagsSupportState.UnsupportedValue,
                    shown,
                    windowsBuild);
            }

            HagsSupportState state = rawValue switch
            {
                HwSchModeOff => HagsSupportState.Off,
                HwSchModeOn => HagsSupportState.On,
                _ => HagsSupportState.UnsupportedValue,
            };

            if (state == HagsSupportState.UnsupportedValue)
            {
                return new HagsDetectionResult(state, rawValue, windowsBuild);
            }

            bool restartRequired = wroteThisSession;
            if (effectiveEnabled is bool live)
            {
                bool requestedOn = state == HagsSupportState.On;
                restartRequired = requestedOn != live;
            }

            return new HagsDetectionResult(state, rawValue, windowsBuild, restartRequired);
        }

        /// <summary>
        /// Legacy classify helper used by older call sites/tests. Prefer
        /// <see cref="DetectCurrentState"/>.
        /// </summary>
        public static HagsSupportState Classify(
            int windowsBuild,
            int? hwSchMode,
            bool registryReadFailed = false)
        {
            uint? unsigned = hwSchMode is null
                ? null
                : unchecked((uint)hwSchMode.Value);

            return DetectCurrentState(
                windowsBuild,
                readSucceeded: !registryReadFailed,
                registryValue: unsigned,
                valueKind: unsigned is null ? null : RegistryValueKind.DWord).State;
        }

        public static string GetStatusText(HagsSupportState state, uint? hwSchMode = null, bool restartRequired = false)
        {
            string baseText = state switch
            {
                HagsSupportState.NotSupportedByWindowsVersion => "Not supported by your Windows version.",
                HagsSupportState.UnsupportedHardware => "Not supported on this GPU/driver.",
                HagsSupportState.On => "On",
                HagsSupportState.Off => "Off",
                HagsSupportState.Default => "Default (system decides)",
                HagsSupportState.UnsupportedValue => hwSchMode.HasValue
                    ? $"Other ({hwSchMode.Value.ToString(CultureInfo.InvariantCulture)}). Current value is not one this tweak would write."
                    : "Other. Current value is not a REG_DWORD this tweak would write.",
                HagsSupportState.Error => "Unknown. Couldn't read the current value.",
                _ => "Unknown.",
            };

            if (restartRequired
                && state is HagsSupportState.On or HagsSupportState.Off or HagsSupportState.Default)
            {
                return $"{baseText} — Restart required.";
            }

            return baseText;
        }

        public static string GetStatusText(HagsDetectionResult result) =>
            GetStatusText(result.State, result.HwSchMode, result.RestartRequired);

        public static bool CanToggle(HagsSupportState state) =>
            state is HagsSupportState.On or HagsSupportState.Off or HagsSupportState.Default;

        public static bool TryConvertToUInt32(object? registryValue, out uint value)
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
                    value = System.BitConverter.ToUInt32(bytes, 0);
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}
