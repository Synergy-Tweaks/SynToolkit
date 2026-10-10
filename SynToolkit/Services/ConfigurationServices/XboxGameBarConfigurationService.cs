#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Xbox Game Bar / Game DVR capture only. Owns:
    /// - HKCU\System\GameConfigStore\GameDVR_Enabled
    /// - HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled
    /// - HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR
    /// Does not touch GameDVR_FSEBehaviorMode (Fullscreen Optimizations owns that).
    /// </summary>
    public sealed class XboxGameBarConfigurationService : IConfigurationService
    {
        private const string SynToolkitStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\XboxGameBar";
        private const string GameConfigStoreKey = @"HKCU\System\GameConfigStore";
        private const string GameDvrKey = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";
        private const string GameDvrPolicyKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR";
        private const string StateValue = "state";
        private const string BackupCapturedValue = "BackupCaptured";

        private readonly ConfigurationStore _store;

        public XboxGameBarConfigurationService(
            [FromKeyedServices("XboxGameBar")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CaptureOriginalSettings();
            RegistryHelper.SetValue(GameConfigStoreKey, "GameDVR_Enabled", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(GameDvrKey, "AppCaptureEnabled", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(GameDvrPolicyKey, "AllowGameDVR", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 0, RegistryValueKind.DWord);

            bool detected = IsEnabled();
            _store.CurrentSetting = detected;
            if (detected)
            {
                throw new InvalidOperationException(
                    "Windows did not accept all requested Xbox Game Bar changes.");
            }
        }

        public void Enable()
        {
            bool hasSnapshot = RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1);
            if (hasSnapshot)
            {
                RestoreSnapshot(GameConfigStoreKey, "GameDVR_Enabled", ReadSnapshot("GameDvrEnabled"));
                RestoreSnapshot(GameDvrKey, "AppCaptureEnabled", ReadSnapshot("AppCaptureEnabled"));
                RestoreSnapshot(GameDvrPolicyKey, "AllowGameDVR", ReadSnapshot("AllowGameDvr"));
            }
            else
            {
                RegistryHelper.DeleteValue(GameConfigStoreKey, "GameDVR_Enabled");
                RegistryHelper.DeleteValue(GameDvrKey, "AppCaptureEnabled");
                RegistryHelper.DeleteValue(GameDvrPolicyKey, "AllowGameDVR");
            }

            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 1, RegistryValueKind.DWord);
            bool detected = IsEnabled();
            _store.CurrentSetting = detected;
            if (!detected)
            {
                throw new InvalidOperationException(
                    "Windows did not restore Xbox Game Bar settings. The saved state was retained.");
            }

            if (hasSnapshot)
            {
                ClearSnapshots();
            }
        }

        public bool IsEnabled()
        {
            if (!TryReadDword(GameConfigStoreKey, "GameDVR_Enabled", out uint? gameDvrEnabled)
                || !TryReadDword(GameDvrKey, "AppCaptureEnabled", out uint? appCapture)
                || !TryReadDword(GameDvrPolicyKey, "AllowGameDVR", out uint? policy))
            {
                throw new InvalidOperationException("Couldn't read Xbox Game Bar state.");
            }

            // Missing values retain Windows defaults (enabled). Explicit 0 disables.
            return gameDvrEnabled != 0 && appCapture != 0 && policy != 0;
        }

        private static bool TryReadDword(string keyPath, string valueName, out uint? value)
        {
            value = null;
            if (!RegistryHelper.TryReadValueWithKind(keyPath, valueName, out object? raw, out RegistryValueKind? kind))
            {
                return false;
            }

            if (raw is null)
            {
                return true;
            }

            if (kind is not null and not RegistryValueKind.DWord
                || !HagsDetection.TryConvertToUInt32(raw, out uint parsed))
            {
                throw new NotSupportedException(
                    $"Xbox Game Bar value {valueName} has an unexpected type/value.");
            }

            value = parsed;
            return true;
        }

        private static void CaptureOriginalSettings()
        {
            if (RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1))
            {
                return;
            }

            CaptureDword(GameConfigStoreKey, "GameDVR_Enabled", "GameDvrEnabled");
            CaptureDword(GameDvrKey, "AppCaptureEnabled", "AppCaptureEnabled");
            CaptureDword(GameDvrPolicyKey, "AllowGameDVR", "AllowGameDvr");
            RegistryHelper.SetValue(SynToolkitStoreKey, BackupCapturedValue, 1, RegistryValueKind.DWord);
        }

        private static void CaptureDword(string keyPath, string valueName, string backupName)
        {
            object? value = RegistryHelper.GetValue(keyPath, valueName);
            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                $"Backup{backupName}Exists",
                value is null ? 0 : 1,
                RegistryValueKind.DWord);

            if (value is null)
            {
                RegistryHelper.DeleteValue(SynToolkitStoreKey, $"Backup{backupName}Value");
                return;
            }

            if (!HagsDetection.TryConvertToUInt32(value, out uint dwordValue))
            {
                throw new InvalidOperationException(
                    $"The existing {valueName} setting is not a DWORD. SynToolkit left it unchanged.");
            }

            RegistryHelper.SetValue(
                SynToolkitStoreKey,
                $"Backup{backupName}Value",
                unchecked((int)dwordValue),
                RegistryValueKind.DWord);
        }

        private static DwordSnapshot ReadSnapshot(string backupName)
        {
            bool existed = RegistryHelper.IsMatch(SynToolkitStoreKey, $"Backup{backupName}Exists", 1);
            if (!existed)
            {
                return new DwordSnapshot(false, 0);
            }

            object? value = RegistryHelper.GetValue(SynToolkitStoreKey, $"Backup{backupName}Value");
            return value is int dwordValue
                ? new DwordSnapshot(true, dwordValue)
                : throw new InvalidOperationException(
                    "A saved Xbox Game Bar setting is invalid.");
        }

        private static void RestoreSnapshot(string keyPath, string valueName, DwordSnapshot snapshot)
        {
            if (snapshot.Existed)
            {
                RegistryHelper.SetValue(keyPath, valueName, snapshot.Value, RegistryValueKind.DWord);
            }
            else
            {
                RegistryHelper.DeleteValue(keyPath, valueName);
            }
        }

        private static void ClearSnapshots()
        {
            foreach (string backupName in new[] { "GameDvrEnabled", "AppCaptureEnabled", "AllowGameDvr" })
            {
                RegistryHelper.DeleteValue(SynToolkitStoreKey, $"Backup{backupName}Exists");
                RegistryHelper.DeleteValue(SynToolkitStoreKey, $"Backup{backupName}Value");
            }

            RegistryHelper.DeleteValue(SynToolkitStoreKey, BackupCapturedValue);
        }

        private sealed record DwordSnapshot(bool Existed, int Value);
    }
}
