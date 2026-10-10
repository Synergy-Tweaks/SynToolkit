#nullable enable

using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Globalization;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Hardware-accelerated GPU scheduling (HAGS) for Tweaks → Performance.
    /// Single read/write path: HwSchMode DWORD under
    /// HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers via RegistryHelper
    /// (RegistryView.Registry64). 2 = on, 1 = off. Missing = Default (system decides).
    /// A restart is required for the GPU scheduler to apply the requested DWORD.
    /// </summary>
    public class HagsConfigurationService : IConfigurationService
    {
        private static bool _wroteThisSession;

        private readonly ConfigurationStore _hagsConfigurationStore;

        public HagsConfigurationService(
            [FromKeyedServices("Hags")] ConfigurationStore hagsConfigurationStore)
        {
            _hagsConfigurationStore = hagsConfigurationStore;
        }

        public static bool IsSupported() => HagsDetection.CanToggle(Detect().State);

        public static HagsDetectionResult Detect() => DetectCurrentState();

        /// <summary>
        /// Fresh registry read every call. Only code path that infers HAGS state.
        /// </summary>
        public static HagsDetectionResult DetectCurrentState()
        {
            int windowsBuild = ReadWindowsBuildNumber();
            bool readSucceeded = RegistryHelper.TryReadValueWithKind(
                HagsDetection.GraphicsDriversKeyPath,
                HagsDetection.HwSchModeValueName,
                out object? raw,
                out RegistryValueKind? kind);

            // Driver effective-state query (D3DKMTQueryAdapterInfo) is not wired —
            // registry-only detection with session restart feedback after writes.
            HagsDetectionResult result = HagsDetection.DetectCurrentState(
                windowsBuild,
                readSucceeded,
                raw,
                kind,
                hardwareSupported: null,
                effectiveEnabled: null,
                wroteThisSession: _wroteThisSession);

            if (!readSucceeded)
            {
                App.logger.Warn(
                    "[HAGS] Registry read failed while inspecting HwSchMode on Windows build {0}.",
                    windowsBuild);
            }
            else if (result.State == HagsSupportState.UnsupportedValue)
            {
                App.logger.Warn(
                    "[HAGS] Unexpected HwSchMode value {0} ({1}) on Windows build {2}.",
                    result.HwSchMode?.ToString(CultureInfo.InvariantCulture) ?? "(unparsed)",
                    kind?.ToString() ?? "none",
                    windowsBuild);
            }

            return result;
        }

        public void Disable() => WriteHwSchMode(enabled: false);

        public void Enable() => WriteHwSchMode(enabled: true);

        public bool IsEnabled()
        {
            HagsDetectionResult result = DetectCurrentState();
            if (!HagsDetection.CanToggle(result.State))
            {
                throw new NotSupportedException(HagsDetection.GetStatusText(result));
            }

            // Default is not On — show toggle Off with status text from GetDetectionStatus.
            return result.State == HagsSupportState.On;
        }

        /// <summary>
        /// Status line for default / restart-required while the toggle remains interactive.
        /// </summary>
        public string? GetDetectionStatus()
        {
            HagsDetectionResult result = DetectCurrentState();
            if (result.State == HagsSupportState.Default || result.RestartRequired)
            {
                return HagsDetection.GetStatusText(result);
            }

            return null;
        }

        private void WriteHwSchMode(bool enabled)
        {
            HagsDetectionResult before = DetectCurrentState();
            if (!HagsDetection.CanToggle(before.State))
            {
                throw new NotSupportedException(HagsDetection.GetStatusText(before));
            }

            uint target = enabled ? HagsDetection.HwSchModeOn : HagsDetection.HwSchModeOff;
            if (before.State == HagsSupportState.On && enabled)
            {
                _hagsConfigurationStore.CurrentSetting = true;
                return;
            }

            if (before.State == HagsSupportState.Off && !enabled)
            {
                _hagsConfigurationStore.CurrentSetting = false;
                return;
            }

            RegistryHelper.SetValue(
                HagsDetection.GraphicsDriversKeyPath,
                HagsDetection.HwSchModeValueName,
                unchecked((int)target),
                RegistryValueKind.DWord);

            _wroteThisSession = true;

            HagsDetectionResult after = DetectCurrentState();
            if (after.HwSchMode != target
                || after.State is not (HagsSupportState.On or HagsSupportState.Off))
            {
                _hagsConfigurationStore.CurrentSetting = after.State == HagsSupportState.On;
                throw new InvalidOperationException(
                    "HwSchMode write could not be verified. The registry value did not match what was written.");
            }

            _hagsConfigurationStore.CurrentSetting = enabled;
            App.ContentDialogCaller("restart");
        }

        private static int ReadWindowsBuildNumber()
        {
            if (RegistryHelper.TryReadValue(
                    @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuildNumber",
                    out object? buildValue)
                && TryParseInt32(buildValue, out int build))
            {
                return build;
            }

            if (RegistryHelper.TryReadValue(
                    @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "CurrentBuild",
                    out object? fallbackValue)
                && TryParseInt32(fallbackValue, out int fallbackBuild))
            {
                return fallbackBuild;
            }

            return Environment.OSVersion.Version.Build;
        }

        private static bool TryParseInt32(object? value, out int mode)
        {
            switch (value)
            {
                case int intValue:
                    mode = intValue;
                    return true;
                case uint unsignedValue when unsignedValue <= int.MaxValue:
                    mode = (int)unsignedValue;
                    return true;
                case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                    mode = (int)longValue;
                    return true;
                case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                    mode = parsed;
                    return true;
                default:
                    mode = 0;
                    return false;
            }
        }
    }
}
