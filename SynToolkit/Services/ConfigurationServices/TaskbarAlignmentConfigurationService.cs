#nullable enable

using SynToolkit.Services.Taskbar;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Windows 11 taskbar alignment dropdown (Centered / Left) for Tweaks → Interface.
    /// </summary>
    internal sealed class TaskbarAlignmentConfigurationService :
        IMultiOptionConfigurationServices,
        IWarnedMultiOptionConfigurationService
    {
        private readonly MultiOptionConfigurationStore _store;
        private readonly List<string> _options;

        public TaskbarAlignmentConfigurationService(
            [FromKeyedServices("TaskbarAlignment")] MultiOptionConfigurationStore store)
        {
            TaskbarAlignmentValues.EnsurePresetValuesUnique();
            _store = store;
            _options = new List<string>(TaskbarAlignmentValues.DropdownLabels);
            _store.Options = _options;
        }

        public void ChangeStatus(int status)
        {
            if (!IsWindows11())
            {
                throw new NotSupportedException("Requires Windows 11");
            }

            if (status < 0 || status >= TaskbarAlignmentValues.Presets.Count)
            {
                // "Other (N)" / "Unknown" are display-only; re-detect instead of writing.
                TaskbarAlignmentValues.DetectionResult current = DetectCurrentState();
                _store.CurrentSetting = current.DisplayLabel;
                return;
            }

            ApplyRawValue(TaskbarAlignmentValues.Presets[status].Value);
        }

        public string Status()
        {
            if (!IsWindows11())
            {
                throw new NotSupportedException("Requires Windows 11");
            }

            TaskbarAlignmentValues.DetectionResult detection = DetectCurrentState();
            _store.CurrentSetting = detection.DisplayLabel;
            return detection.DisplayLabel;
        }

        public string GetStatusWarning()
        {
            if (!IsWindows11())
            {
                return "Requires Windows 11";
            }

            return DetectCurrentState().Warning ?? string.Empty;
        }

        public TaskbarAlignmentValues.DetectionResult DetectCurrentState()
        {
            bool readSucceeded = RegistryHelper.TryReadValueWithKind(
                TaskbarAlignmentValues.KeyPath,
                TaskbarAlignmentValues.ValueName,
                out object? raw,
                out RegistryValueKind? kind);
            return TaskbarAlignmentValues.DetectCurrentState(readSucceeded, raw, kind);
        }

        public void ApplyRawValue(uint value)
        {
            if (!IsWindows11())
            {
                throw new NotSupportedException("Requires Windows 11");
            }

            TaskbarAlignmentValues.DetectionResult before = DetectCurrentState();

            // Selecting the matching explicit preset must not write again.
            if (before.Kind == TaskbarAlignmentValues.DetectionKind.Preset
                && before.RawValue == value)
            {
                _store.CurrentSetting = before.DisplayLabel;
                return;
            }

            // Default (missing) maps to Centered visually, but selecting Centered still
            // writes 1 explicitly so the state is detectable.
            RegistryHelper.SetValue(
                TaskbarAlignmentValues.KeyPath,
                TaskbarAlignmentValues.ValueName,
                unchecked((int)value),
                RegistryValueKind.DWord);

            TaskbarAlignmentValues.DetectionResult after = DetectCurrentState();
            if (after.RawValue != value
                || after.Kind != TaskbarAlignmentValues.DetectionKind.Preset)
            {
                _store.CurrentSetting = after.DisplayLabel;
                throw new InvalidOperationException(
                    "TaskbarAl write could not be verified. The registry value did not match what was written.");
            }

            _store.CurrentSetting = after.DisplayLabel;
            NotifyShellSettingChanged();
            App.ContentDialogCaller("restartExplorer");
        }

        private static bool IsWindows11() =>
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, TaskbarAlignmentValues.MinimumWindows11Build);

        private static void NotifyShellSettingChanged()
        {
            try
            {
                const int HWND_BROADCAST = 0xffff;
                const uint WM_SETTINGCHANGE = 0x001A;
                const uint SMTO_ABORTIFHUNG = 0x0002;
                _ = SendMessageTimeout(
                    new IntPtr(HWND_BROADCAST),
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "TraySettings",
                    SMTO_ABORTIFHUNG,
                    2000,
                    out _);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[TaskbarAlignment] WM_SETTINGCHANGE broadcast failed.");
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);
    }
}
