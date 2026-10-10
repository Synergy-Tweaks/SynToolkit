#nullable enable

using Microsoft.Win32;
using System.Globalization;

namespace SynToolkit.Services.Mpo
{
    /// <summary>
    /// Multi-Plane Overlay (MPO) detection: OverlayTestMode under
    /// HKLM\SOFTWARE\Microsoft\Windows\Dwm. 5 = disabled; missing = enabled (Windows default).
    /// Restoring default deletes the value.
    /// </summary>
    public static class MultiPlaneOverlayValues
    {
        public const string DwmKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm";
        public const string OverlayTestModeValueName = "OverlayTestMode";
        public const uint DisableMpoValue = 5;

        public enum DetectionKind
        {
            Enabled,
            Disabled,
            Unsupported,
            Error,
        }

        public sealed record DetectionResult(
            uint? RawValue,
            DetectionKind Kind,
            string DisplayLabel,
            string? Warning);

        public static DetectionResult DetectCurrentState(
            bool readSucceeded,
            object? registryValue,
            RegistryValueKind? valueKind)
        {
            if (!readSucceeded)
            {
                return new DetectionResult(
                    null,
                    DetectionKind.Error,
                    "Unknown",
                    "Couldn't read the current value.");
            }

            if (registryValue is null)
            {
                return new DetectionResult(null, DetectionKind.Enabled, "Enabled", null);
            }

            bool isDwordKind = valueKind is null or RegistryValueKind.DWord;
            bool isWrongRuntimeType = registryValue is string or byte[] or long or ulong;
            if (!isDwordKind
                || isWrongRuntimeType
                || !HagsDetection.TryConvertToUInt32(registryValue, out uint raw))
            {
                bool parsed = HagsDetection.TryConvertToUInt32(registryValue, out uint shown);
                return new DetectionResult(
                    parsed ? shown : null,
                    DetectionKind.Unsupported,
                    parsed ? $"Other ({shown.ToString(CultureInfo.InvariantCulture)})" : "Other",
                    parsed
                        ? $"Current value {shown.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write."
                        : "Current value is not a REG_DWORD this tweak would write.");
            }

            if (raw == DisableMpoValue)
            {
                return new DetectionResult(raw, DetectionKind.Disabled, "Disabled", null);
            }

            return new DetectionResult(
                raw,
                DetectionKind.Unsupported,
                $"Other ({raw.ToString(CultureInfo.InvariantCulture)})",
                $"Current value {raw.ToString(CultureInfo.InvariantCulture)} is not one this tweak would write.");
        }
    }
}
