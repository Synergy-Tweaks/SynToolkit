#nullable enable

using SynToolkit.Services.Mmcss;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Collections.Generic;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Multimedia Class Scheduler dropdown for SystemResponsiveness only.
    /// Does not touch NetworkThrottlingIndex, Tasks\*, or the MMCSS service.
    /// </summary>
    internal sealed class MmcssOptimizationConfigurationService :
        IMultiOptionConfigurationServices,
        IWarnedMultiOptionConfigurationService
    {
        private const string KeyPath =
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string ValueName = "SystemResponsiveness";

        private readonly MultiOptionConfigurationStore _store;
        private readonly List<string> _options;

        public MmcssOptimizationConfigurationService(
            [FromKeyedServices("MmcssOptimization")] MultiOptionConfigurationStore store)
        {
            SystemResponsivenessValues.EnsurePresetValuesUnique();

            _store = store;
            _options = new List<string>(SystemResponsivenessValues.DropdownLabels);
            _store.Options = _options;
        }

        public void ChangeStatus(int status)
        {
            if (status < 0 || status >= SystemResponsivenessValues.Presets.Count)
            {
                // "Other (N)" / "Unknown" are display-only; re-detect instead of writing.
                SystemResponsivenessValues.DetectionResult current = DetectCurrentState();
                _store.CurrentSetting = current.DisplayLabel;
                return;
            }

            ApplyRawValue(SystemResponsivenessValues.Presets[status].Value);
        }

        public string Status()
        {
            SystemResponsivenessValues.DetectionResult detection = DetectCurrentState();
            _store.CurrentSetting = detection.DisplayLabel;
            return detection.DisplayLabel;
        }

        public string GetStatusWarning() => DetectCurrentState().Warning ?? string.Empty;

        public SystemResponsivenessValues.DetectionResult DetectCurrentState()
        {
            bool readSucceeded = RegistryHelper.TryReadValueWithKind(
                KeyPath,
                ValueName,
                out object? raw,
                out RegistryValueKind? kind);
            return SystemResponsivenessValues.DetectCurrentState(readSucceeded, raw, kind);
        }

        public void ApplyRawValue(uint value)
        {
            SystemResponsivenessValues.DetectionResult before = DetectCurrentState();

            // Skip redundant write when the detected Default/Preset already matches.
            if (before.Kind is SystemResponsivenessValues.DetectionKind.Default
                    or SystemResponsivenessValues.DetectionKind.Preset)
            {
                if (before.RawValue == value
                    || (before.Kind == SystemResponsivenessValues.DetectionKind.Default
                        && before.RawValue is null
                        && value == SystemResponsivenessValues.DefaultSystemResponsiveness))
                {
                    _store.CurrentSetting = before.DisplayLabel;
                    return;
                }
            }

            RegistryHelper.SetValue(KeyPath, ValueName, unchecked((int)value), RegistryValueKind.DWord);

            SystemResponsivenessValues.DetectionResult after = DetectCurrentState();
            if (after.RawValue != value
                || after.Kind is not (SystemResponsivenessValues.DetectionKind.Default
                    or SystemResponsivenessValues.DetectionKind.Preset))
            {
                throw new InvalidOperationException(
                    "SystemResponsiveness write could not be verified. The registry value did not match what was written.");
            }

            _store.CurrentSetting = after.DisplayLabel;
            App.ReportConfigurationActionSuccess(
                "Multimedia Class Scheduler updated. New multimedia sessions pick this up immediately; a sign-out can help stubborn apps.");
        }
    }
}
