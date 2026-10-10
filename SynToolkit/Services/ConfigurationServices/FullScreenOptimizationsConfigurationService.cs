#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Fullscreen Optimizations only. Owns GameDVR_FSEBehaviorMode under
    /// HKCU\System\GameConfigStore. Does not touch Game Bar / Game DVR capture values.
    /// Polarity matches the former combined tweak: Disable writes mode=2; Enable restores
    /// the saved snapshot or deletes the value (Windows default).
    /// </summary>
    public sealed class FullScreenOptimizationsConfigurationService : IConfigurationService
    {
        private const string SynToolkitStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\FullScreenOptimizations";
        private const string GameConfigStoreKey = @"HKCU\System\GameConfigStore";
        private const string FseBehaviorModeValue = "GameDVR_FSEBehaviorMode";
        private const string StateValue = "state";
        private const string BackupCapturedValue = "BackupCaptured";
        private const int DisabledMode = 2;

        private readonly ConfigurationStore _store;

        public FullScreenOptimizationsConfigurationService(
            [FromKeyedServices("FullScreenOptimizations")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CaptureOriginalSettings();
            RegistryHelper.SetValue(GameConfigStoreKey, FseBehaviorModeValue, DisabledMode, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 0, RegistryValueKind.DWord);

            bool detected = IsEnabled();
            _store.CurrentSetting = detected;
            if (detected)
            {
                throw new InvalidOperationException(
                    "Windows did not accept the Fullscreen Optimizations change.");
            }
        }

        public void Enable()
        {
            bool hasSnapshot = RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1);
            if (hasSnapshot)
            {
                RestoreSnapshot(ReadSnapshot("FsoBehavior"));
            }
            else
            {
                RegistryHelper.DeleteValue(GameConfigStoreKey, FseBehaviorModeValue);
            }

            RegistryHelper.SetValue(SynToolkitStoreKey, StateValue, 1, RegistryValueKind.DWord);
            bool detected = IsEnabled();
            _store.CurrentSetting = detected;
            if (!detected)
            {
                throw new InvalidOperationException(
                    "Windows did not restore Fullscreen Optimizations. The saved state was retained.");
            }

            if (hasSnapshot)
            {
                ClearSnapshots();
            }
        }

        public bool IsEnabled()
        {
            if (!RegistryHelper.TryReadValueWithKind(
                    GameConfigStoreKey,
                    FseBehaviorModeValue,
                    out object? raw,
                    out RegistryValueKind? kind))
            {
                throw new InvalidOperationException("Couldn't read Fullscreen Optimizations state.");
            }

            if (raw is null)
            {
                // Missing = Windows default (FSO not forcibly disabled).
                return true;
            }

            if (kind is not null and not RegistryValueKind.DWord
                || !HagsDetection.TryConvertToUInt32(raw, out uint mode))
            {
                throw new NotSupportedException(
                    "Fullscreen Optimizations has an unexpected registry type/value.");
            }

            if (mode == DisabledMode)
            {
                return false;
            }

            if (mode is 0 or 1)
            {
                return true;
            }

            throw new NotSupportedException(
                $"Fullscreen Optimizations has an unsupported value ({mode}).");
        }

        private static void CaptureOriginalSettings()
        {
            if (RegistryHelper.IsMatch(SynToolkitStoreKey, BackupCapturedValue, 1))
            {
                return;
            }

            CaptureDword(GameConfigStoreKey, FseBehaviorModeValue, "FsoBehavior");
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
                    "A saved Fullscreen Optimizations setting is invalid.");
        }

        private static void RestoreSnapshot(DwordSnapshot snapshot)
        {
            if (snapshot.Existed)
            {
                RegistryHelper.SetValue(
                    GameConfigStoreKey,
                    FseBehaviorModeValue,
                    snapshot.Value,
                    RegistryValueKind.DWord);
            }
            else
            {
                RegistryHelper.DeleteValue(GameConfigStoreKey, FseBehaviorModeValue);
            }
        }

        private static void ClearSnapshots()
        {
            RegistryHelper.DeleteValue(SynToolkitStoreKey, "BackupFsoBehaviorExists");
            RegistryHelper.DeleteValue(SynToolkitStoreKey, "BackupFsoBehaviorValue");
            RegistryHelper.DeleteValue(SynToolkitStoreKey, BackupCapturedValue);
        }

        private sealed record DwordSnapshot(bool Existed, int Value);
    }
}
