#nullable enable

using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS sets DmaRemappingCompatible=0 under device services.
    /// Toggle on = remapping allowed; off = forced incompatible (SOS).
    /// Disable snapshots every overwritten value so Enable restores exactly.
    /// </summary>
    internal class DmaRemappingConfigurationService : IConfigurationService
    {
        private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\DmaRemapping";
        private const string SERVICES_ROOT = @"SYSTEM\CurrentControlSet\Services";
        private const string BACKUP_VALUE = "backup";
        private const string TARGET_VALUE = "DmaRemappingCompatible";

        private readonly ConfigurationStore _store;

        public DmaRemappingConfigurationService(
            [FromKeyedServices("DmaRemapping")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            // Never snapshot twice: a second Disable must not record our own 0s as originals.
            List<BackupEntry> backup = LoadBackup() ?? SnapshotCurrentValues();

            foreach (BackupEntry entry in backup)
            {
                using RegistryKey? key = OpenServicesSubKey(entry.Key, writable: true);
                key?.SetValue(TARGET_VALUE, 0, RegistryValueKind.DWord);
            }

            SaveBackup(backup);
            RegistryHelper.SetValue(STORE_KEY, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            List<BackupEntry>? backup = LoadBackup();
            if (backup is null)
            {
                // Disabled by an older build that kept no backup: nothing safe to
                // restore (a 0 may be OEM-set), so leave values and mark enabled.
                App.logger.Warn("[DmaRemapping] No backup found; leaving device values untouched.");
            }
            else
            {
                foreach (BackupEntry entry in backup)
                {
                    using RegistryKey? key = OpenServicesSubKey(entry.Key, writable: true);
                    key?.SetValue(TARGET_VALUE, entry.Value, RegistryValueKind.DWord);
                }

                RegistryHelper.DeleteValue(STORE_KEY, BACKUP_VALUE);
            }

            RegistryHelper.SetValue(STORE_KEY, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(STORE_KEY, "state", 0);

        private sealed record BackupEntry(string Key, int Value);

        private static List<BackupEntry> SnapshotCurrentValues()
        {
            List<BackupEntry> backup = new();
            using RegistryKey? root = OpenServicesSubKey(null, writable: false);
            if (root is null)
            {
                return backup;
            }

            CollectValues(root, string.Empty, backup);
            return backup;
        }

        private static void CollectValues(RegistryKey key, string relativePath, List<BackupEntry> backup)
        {
            object? raw = key.GetValue(TARGET_VALUE);
            if (raw is not null)
            {
                try
                {
                    backup.Add(new BackupEntry(relativePath, System.Convert.ToInt32(raw)));
                }
                catch (System.Exception exception)
                {
                    // Non-numeric vendor value: leave it alone entirely.
                    App.logger.Warn(exception, $"[DmaRemapping] Skipping non-numeric value at {relativePath}.");
                }
            }

            foreach (string subName in key.GetSubKeyNames())
            {
                using RegistryKey? sub = key.OpenSubKey(subName, writable: false);
                if (sub is not null)
                {
                    CollectValues(sub, string.IsNullOrEmpty(relativePath) ? subName : relativePath + "\\" + subName, backup);
                }
            }
        }

        private static RegistryKey? OpenServicesSubKey(string? relativePath, bool writable)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                System.Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            using RegistryKey? services = baseKey.OpenSubKey(SERVICES_ROOT, writable);
            if (services is null || string.IsNullOrEmpty(relativePath))
            {
                return services;
            }

            return services.OpenSubKey(relativePath, writable);
        }

        private static List<BackupEntry>? LoadBackup()
        {
            object? raw = RegistryHelper.GetValue(STORE_KEY, BACKUP_VALUE);
            if (raw is not string json || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<List<BackupEntry>>(json);
            }
            catch (System.Exception exception)
            {
                App.logger.Warn(exception, "[DmaRemapping] Stored backup is corrupt; ignoring it.");
                return null;
            }
        }

        private static void SaveBackup(List<BackupEntry> backup)
        {
            RegistryHelper.SetValue(STORE_KEY, BACKUP_VALUE, JsonConvert.SerializeObject(backup));
        }
    }
}
