#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Full Screen Optimizations toggle. Owns seven registry states:
    /// - Six HKCU DWORDs under System\GameConfigStore
    /// - HKLM Environment\__COMPAT_LAYER (machine-wide, admin required)
    /// 
    /// Does NOT touch GameDVR_Enabled, AppCaptureEnabled, UseNexusForGameBarEnabled,
    /// AllowGameDVR, or any other Xbox Game Bar / HAGS / MPO values.
    /// 
    /// Polarity: Enable() = FSO on (Windows default), Disable() = FSO off.
    /// </summary>
    public sealed class FullScreenOptimizationsConfigurationService : IConfigurationService
    {
        #region Registry Constants

        private const string SynToolkitStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\FullScreenOptimizations";
        private const string GameConfigStoreKey = @"HKCU\System\GameConfigStore";
        private const string EnvironmentKeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
        private const string EnvironmentSubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

        // The six HKCU GameConfigStore values managed by this toggle
        private static readonly string[] GameConfigStoreValueNames =
        [
            "GameDVR_EFSEFeatureFlags",
            "GameDVR_FSEBehaviorMode",
            "GameDVR_FSEBehavior",
            "GameDVR_DSEBehavior",
            "GameDVR_DXGIHonorFSEWindowsCompatible",
            "GameDVR_HonorUserFSEBehaviorMode"
        ];

        private const string CompatLayerValueName = "__COMPAT_LAYER";
        private const uint EnabledValue = 0;
        private const uint DisabledModeValue = 2;

        // Backup keys
        private const string StateValue = "state";
        private const string BackupCapturedValue = "BackupCaptured";
        private const string BackupVersionValue = "BackupVersion";
        // 2 = every GameConfigStore value is captured with its own kind. Version 1 backups
        // (written by older builds) only held GameDVR_FSEBehaviorMode, so they are re-captured.
        private const int BackupVersion = 2;
        private const string CompatLayerBackupExists = "BackupCompatLayerExists";
        private const string CompatLayerBackupKind = "BackupCompatLayerKind";
        private const string CompatLayerBackupValue = "BackupCompatLayerValue";

        #endregion

        private readonly ConfigurationStore _store;

        public FullScreenOptimizationsConfigurationService(
            [FromKeyedServices("FullScreenOptimizations")] ConfigurationStore store)
        {
            _store = store;
        }

        /// <summary>
        /// FSO Enabled = Windows default. Writes all six HKCU values to 0 and deletes __COMPAT_LAYER.
        /// </summary>
        public void Enable()
        {
            FsoDetectionResult before = DetectCurrentState();

            // Already enabled? Don't write redundantly.
            if (before.State == FsoState.Enabled)
            {
                _store.CurrentSetting = true;
                return;
            }

            // The six values may hold per-app settings the user made by hand; keep them so a
            // failed toggle can put them back instead of leaving a half-applied state.
            CaptureOriginalSettings();

            List<string> failedSteps = [];

            // Step 1: Write six HKCU values to 0
            foreach (string valueName in GameConfigStoreValueNames)
            {
                try
                {
                    RegistryHelper.SetValue(GameConfigStoreKey, valueName, 0, RegistryValueKind.DWord);
                }
                catch (Exception ex) when (IsRegistryException(ex))
                {
                    App.logger.Warn(ex, "[FSO] Failed to write {ValueName}", valueName);
                    failedSteps.Add(valueName);
                }
            }

            // Step 2: Backup and delete __COMPAT_LAYER (requires admin)
            string? compatLayerMessage = null;
            try
            {
                compatLayerMessage = BackupAndDeleteCompatLayer();
            }
            catch (Exception ex) when (IsRegistryException(ex))
            {
                App.logger.Warn(ex, "[FSO] Failed to delete __COMPAT_LAYER");
                failedSteps.Add(CompatLayerValueName);
            }

            // Step 3: Broadcast environment change
            BroadcastEnvironmentChange();

            // Step 4: Read back and verify
            FsoDetectionResult after = DetectCurrentState();
            _store.CurrentSetting = after.State == FsoState.Enabled;
            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, after.State == FsoState.Enabled ? 1 : 0, RegistryValueKind.DWord);

            if (failedSteps.Count > 0)
            {
                RollbackToCapturedSettings();
                throw new InvalidOperationException(
                    $"Full Screen Optimizations: failed to write {string.Join(", ", failedSteps)}.");
            }

            if (after.State != FsoState.Enabled)
            {
                RollbackToCapturedSettings();
                throw new InvalidOperationException(
                    "Full Screen Optimizations could not be verified. The registry did not match what was written.");
            }

            if (!string.IsNullOrEmpty(compatLayerMessage))
            {
                App.logger.Info("[FSO] {Message}", compatLayerMessage);
            }
        }

        /// <summary>
        /// FSO Disabled. Writes GameDVR_FSEBehaviorMode = 2 (current behavior; other values unchanged).
        /// </summary>
        public void Disable()
        {
            FsoDetectionResult before = DetectCurrentState();

            // Already disabled? Don't write redundantly.
            if (before.State == FsoState.Disabled)
            {
                _store.CurrentSetting = false;
                return;
            }

            CaptureOriginalSettings();

            try
            {
                RegistryHelper.SetValue(GameConfigStoreKey, "GameDVR_FSEBehaviorMode", DisabledModeValue, RegistryValueKind.DWord);
            }
            catch (Exception ex) when (IsRegistryException(ex))
            {
                App.logger.Error(ex, "[FSO] Failed to write GameDVR_FSEBehaviorMode");
                RollbackToCapturedSettings();
                throw new InvalidOperationException("Full Screen Optimizations: failed to write GameDVR_FSEBehaviorMode.", ex);
            }

            FsoDetectionResult after = DetectCurrentState();
            _store.CurrentSetting = after.State == FsoState.Enabled;
            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 0, RegistryValueKind.DWord);

            if (after.State != FsoState.Disabled)
            {
                RollbackToCapturedSettings();
                throw new InvalidOperationException(
                    "Full Screen Optimizations could not be disabled. The registry value did not match.");
            }
        }

        public bool IsEnabled()
        {
            FsoDetectionResult result = DetectCurrentState();
            _store.CurrentSetting = result.State == FsoState.Enabled;

            return result.State switch
            {
                FsoState.Enabled => true,
                FsoState.Disabled => false,
                FsoState.Modified => throw new NotSupportedException(
                    $"Full Screen Optimizations is in a modified state: {result.Warning}"),
                FsoState.Error => throw new InvalidOperationException(
                    $"Couldn't read Full Screen Optimizations state: {result.Warning}"),
                _ => throw new InvalidOperationException("Unknown FSO state.")
            };
        }

        #region Detection

        public FsoDetectionResult DetectCurrentState()
        {
            List<string> mismatchedValues = [];
            bool hasReadError = false;

            // Check all six HKCU values
            foreach (string valueName in GameConfigStoreValueNames)
            {
                FsoValueCheckResult check = CheckHkcuValue(valueName);
                if (check == FsoValueCheckResult.ReadError)
                {
                    hasReadError = true;
                    break;
                }
                if (check == FsoValueCheckResult.Mismatch)
                {
                    mismatchedValues.Add(valueName);
                }
            }

            if (hasReadError)
            {
                return new FsoDetectionResult(FsoState.Error, "Registry read failed.");
            }

            // Check __COMPAT_LAYER
            FsoValueCheckResult compatCheck = CheckCompatLayer();
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
            if (IsDisabledState())
            {
                return new FsoDetectionResult(FsoState.Disabled, null);
            }

            // Modified state
            string warning = mismatchedValues.Count <= 3
                ? $"Modified: {string.Join(", ", mismatchedValues)}"
                : $"Modified: {string.Join(", ", mismatchedValues[..3])} and {mismatchedValues.Count - 3} more";

            return new FsoDetectionResult(FsoState.Modified, warning);
        }

        private static FsoValueCheckResult CheckHkcuValue(string valueName)
        {
            if (!RegistryHelper.TryReadValueWithKind(GameConfigStoreKey, valueName, out object? raw, out RegistryValueKind? kind))
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

        private static FsoValueCheckResult CheckCompatLayer()
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, GetRegistryView());
                using RegistryKey? envKey = baseKey.OpenSubKey(EnvironmentSubKey, writable: false);
                if (envKey is null)
                {
                    // Key missing = value missing = matches enabled
                    return FsoValueCheckResult.Match;
                }

                object? value = envKey.GetValue(CompatLayerValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                // Missing = matches enabled; any value present = mismatch
                return value is null ? FsoValueCheckResult.Match : FsoValueCheckResult.Mismatch;
            }
            catch (Exception ex) when (IsRegistryException(ex))
            {
                App.logger.Warn(ex, "[FSO] Failed to read __COMPAT_LAYER");
                return FsoValueCheckResult.ReadError;
            }
        }

        private static bool IsDisabledState()
        {
            if (!RegistryHelper.TryReadValueWithKind(GameConfigStoreKey, "GameDVR_FSEBehaviorMode", out object? raw, out RegistryValueKind? kind))
            {
                return false;
            }

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

        #endregion

        #region Backup & Restore __COMPAT_LAYER

        /// <summary>
        /// Backs up and deletes __COMPAT_LAYER. Returns a message if something was removed.
        /// </summary>
        private static string? BackupAndDeleteCompatLayer()
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, GetRegistryView());
            using RegistryKey? envKey = baseKey.OpenSubKey(EnvironmentSubKey, writable: true);
            if (envKey is null)
            {
                return null;
            }

            object? existingValue = envKey.GetValue(CompatLayerValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (existingValue is null)
            {
                return null;
            }

            // Back up kind and data
            RegistryValueKind existingKind;
            try
            {
                existingKind = envKey.GetValueKind(CompatLayerValueName);
            }
            catch (IOException)
            {
                existingKind = RegistryValueKind.String;
            }

            // Store backup in SynToolkit store
            RegistryHelper.SetValue(SynToolkitStoreKey, CompatLayerBackupExists, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SynToolkitStoreKey, CompatLayerBackupKind, (int)existingKind, RegistryValueKind.DWord);

            // Store the value based on its kind
            if (existingValue is string stringValue)
            {
                RegistryHelper.SetValue(SynToolkitStoreKey, CompatLayerBackupValue, stringValue, RegistryValueKind.String);
            }
            else if (existingValue is byte[] byteValue)
            {
                RegistryHelper.SetValue(SynToolkitStoreKey, CompatLayerBackupValue, byteValue, RegistryValueKind.Binary);
            }
            else
            {
                // For other types, store as string representation
                RegistryHelper.SetValue(SynToolkitStoreKey, CompatLayerBackupValue, existingValue.ToString() ?? "", RegistryValueKind.String);
            }

            // Delete the value
            envKey.DeleteValue(CompatLayerValueName, throwOnMissingValue: false);
            App.logger.Info("[FSO] Deleted __COMPAT_LAYER (backed up kind={Kind})", existingKind);

            return "An existing system-wide compatibility setting (__COMPAT_LAYER) was removed. This applies to newly started programs.";
        }

        /// <summary>
        /// Restores __COMPAT_LAYER from backup if available.
        /// </summary>
        public static void RestoreCompatLayerFromBackup()
        {
            if (!RegistryHelper.IsMatch(SynToolkitStoreKey, CompatLayerBackupExists, 1))
            {
                return;
            }

            object? kindObj = RegistryHelper.GetValue(SynToolkitStoreKey, CompatLayerBackupKind);
            object? valueObj = RegistryHelper.GetValue(SynToolkitStoreKey, CompatLayerBackupValue);

            if (kindObj is not int kindInt || valueObj is null)
            {
                App.logger.Warn("[FSO] __COMPAT_LAYER backup is incomplete, cannot restore.");
                return;
            }

            RegistryValueKind kind = (RegistryValueKind)kindInt;

            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, GetRegistryView());
                using RegistryKey? envKey = baseKey.OpenSubKey(EnvironmentSubKey, writable: true)
                    ?? baseKey.CreateSubKey(EnvironmentSubKey, writable: true);

                if (envKey is null)
                {
                    throw new InvalidOperationException("Cannot open Environment registry key for writing.");
                }

                envKey.SetValue(CompatLayerValueName, valueObj, kind);
                App.logger.Info("[FSO] Restored __COMPAT_LAYER from backup (kind={Kind})", kind);

                BroadcastEnvironmentChange();

                // Clear backup
                ClearCompatLayerBackup();
            }
            catch (Exception ex) when (IsRegistryException(ex))
            {
                App.logger.Error(ex, "[FSO] Failed to restore __COMPAT_LAYER");
                throw;
            }
        }

        private static void ClearCompatLayerBackup()
        {
            RegistryHelper.DeleteValue(SynToolkitStoreKey, CompatLayerBackupExists);
            RegistryHelper.DeleteValue(SynToolkitStoreKey, CompatLayerBackupKind);
            RegistryHelper.DeleteValue(SynToolkitStoreKey, CompatLayerBackupValue);
        }

        #endregion

        #region Original Settings Backup (for Disable position)

        /// <summary>
        /// Captures all six GameConfigStore values, with their kind, before the first write.
        /// Throws before anything is changed when a value cannot be read, so the toggle never
        /// starts on a machine whose state it cannot put back.
        /// </summary>
        private static void CaptureOriginalSettings()
        {
            if (RegistryHelper.IsMatch(SynToolkitStoreKey, BackupVersionValue, BackupVersion))
            {
                return;
            }

            foreach (string valueName in GameConfigStoreValueNames)
            {
                CaptureValue(valueName);
            }

            RegistryHelper.SetValue(SynToolkitStoreKey, BackupCapturedValue, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SynToolkitStoreKey, BackupVersionValue, BackupVersion, RegistryValueKind.DWord);
        }

        private static void CaptureValue(string valueName)
        {
            string prefix = $"Backup{valueName}";

            if (!RegistryHelper.TryReadValueWithKind(GameConfigStoreKey, valueName, out object? value, out RegistryValueKind? kind))
            {
                throw new InvalidOperationException(
                    $"Could not read the existing {valueName} setting. Nothing was changed.");
            }

            RegistryHelper.SetValue(SynToolkitStoreKey, prefix + "Exists", value is null ? 0 : 1, RegistryValueKind.DWord);

            if (value is null)
            {
                RegistryHelper.DeleteValue(SynToolkitStoreKey, prefix + "Kind");
                RegistryHelper.DeleteValue(SynToolkitStoreKey, prefix + "Value");
                return;
            }

            RegistryValueKind valueKind = kind ?? RegistryValueKind.DWord;
            RegistryHelper.SetValue(SynToolkitStoreKey, prefix + "Kind", (int)valueKind, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SynToolkitStoreKey, prefix + "Value", value, valueKind);
        }

        private static void RestoreOriginalSettings()
        {
            if (!RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1))
            {
                return;
            }

            foreach (string valueName in GameConfigStoreValueNames)
            {
                string prefix = $"Backup{valueName}";
                bool existed = RegistryHelper.IsMatch(SynToolkitStoreKey, prefix + "Exists", 1);
                if (!existed)
                {
                    RegistryHelper.DeleteValue(GameConfigStoreKey, valueName);
                    continue;
                }

                object? kindObj = RegistryHelper.GetValue(SynToolkitStoreKey, prefix + "Kind");
                object? valueObj = RegistryHelper.GetValue(SynToolkitStoreKey, prefix + "Value");
                if (kindObj is not int kindInt || valueObj is null)
                {
                    App.logger.Warn("[FSO] Backup for {ValueName} is incomplete, leaving it as is.", valueName);
                    continue;
                }

                RegistryHelper.SetValue(GameConfigStoreKey, valueName, valueObj, (RegistryValueKind)kindInt);
            }

            ClearOriginalSettingsBackup();
        }

        private static void ClearOriginalSettingsBackup()
        {
            RegistryHelper.DeleteValue(SynToolkitStoreKey, BackupCapturedValue);
            RegistryHelper.DeleteValue(SynToolkitStoreKey, BackupVersionValue);

            foreach (string valueName in GameConfigStoreValueNames)
            {
                string prefix = $"Backup{valueName}";
                RegistryHelper.DeleteValue(SynToolkitStoreKey, prefix + "Exists");
                RegistryHelper.DeleteValue(SynToolkitStoreKey, prefix + "Kind");
                RegistryHelper.DeleteValue(SynToolkitStoreKey, prefix + "Value");
            }
        }

        /// <summary>
        /// Puts the machine back to the captured state after a failed toggle. Failures here are
        /// logged, not thrown, so the original error reaches the user.
        /// </summary>
        private static void RollbackToCapturedSettings()
        {
            try
            {
                RestoreOriginalSettings();
                RestoreCompatLayerFromBackup();
                BroadcastEnvironmentChange();
                App.logger.Info("[FSO] Rolled back to the captured settings.");
            }
            catch (Exception ex)
            {
                App.logger.Error(ex, "[FSO] Rolling back to the captured settings failed.");
            }
        }

        #endregion

        #region WM_SETTINGCHANGE Broadcast

        private static void BroadcastEnvironmentChange()
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
                    "Environment",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out _);
                App.logger.Info("[FSO] Broadcast WM_SETTINGCHANGE for Environment");
            }
            catch (Exception ex)
            {
                App.logger.Warn(ex, "[FSO] WM_SETTINGCHANGE broadcast failed.");
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

        #endregion

        #region Helpers

        private static RegistryView GetRegistryView() =>
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

        private static bool IsRegistryException(Exception ex) =>
            ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException;

        #endregion
    }
}
