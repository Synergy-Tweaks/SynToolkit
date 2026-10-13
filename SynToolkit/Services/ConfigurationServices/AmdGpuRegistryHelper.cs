#nullable enable

using SynToolkit.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Locates the AMD display adapter class key and applies/reverts DWORD packs from
    /// imribiy's amd-gpu-tweaks script. Aborts when no AMD adapter is present.
    /// </summary>
    internal static class AmdGpuRegistryHelper
    {
        private const string DisplayClassRelativePath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private const string DisplayClassKey =
            @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private const string SnapshotStoreKey = @"HKLM\SOFTWARE\SynToolkit\Services\AmdGpu";

        private const string AmdProviderFragment = "Advanced Micro Devices";

        public static void EnsureAmdGpuPresent()
        {
            if (!GpuDetectionService.HasAmdGpu() || FindAmdAdapterKeyPath() is null)
            {
                throw new NotSupportedException(App.GetValueFromItemList("AmdGpuRequired"));
            }
        }

        public static string RequireAmdAdapterKeyPath()
        {
            EnsureAmdGpuPresent();
            return FindAmdAdapterKeyPath()
                ?? throw new NotSupportedException(App.GetValueFromItemList("AmdGpuRequired"));
        }

        public static string? FindAmdAdapterKeyPath()
        {
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem
                    ? RegistryView.Registry64
                    : RegistryView.Registry32;
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? classKey = baseKey.OpenSubKey(DisplayClassRelativePath);
                if (classKey is null)
                {
                    return null;
                }

                foreach (string subName in classKey.GetSubKeyNames())
                {
                    if (subName.Length != 4 || !subName.All(char.IsDigit))
                    {
                        continue;
                    }

                    string path = $"{DisplayClassKey}\\{subName}";
                    object? provider = RegistryHelper.GetValue(path, "ProviderName");
                    if (provider is string providerName
                        && providerName.Contains(AmdProviderFragment, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[AMD] Unable to enumerate display class adapters.");
            }

            return null;
        }

        public static void ApplyValues(IReadOnlyList<AmdRegistryValue> values)
        {
            string keyPath = RequireAmdAdapterKeyPath();
            string adapter = AdapterSubName(keyPath);
            foreach (AmdRegistryValue value in values)
            {
                SnapshotOnce(adapter, keyPath, value.Name);
                RegistryHelper.SetValue(keyPath, value.Name, value.Data, value.Kind);
            }
        }

        public static void RevertValues(IReadOnlyList<AmdRegistryValue> values)
        {
            string keyPath = RequireAmdAdapterKeyPath();
            string adapter = AdapterSubName(keyPath);
            foreach (AmdRegistryValue value in values)
            {
                if (RestoreSnapshot(adapter, keyPath, value.Name))
                {
                    continue;
                }

                // No snapshot (written by an older build): legacy delete.
                RegistryHelper.DeleteValue(keyPath, value.Name);
            }
        }

        public static bool AreValuesApplied(IReadOnlyList<AmdRegistryValue> values)
        {
            if (!GpuDetectionService.HasAmdGpu())
            {
                return false;
            }

            string? keyPath = FindAmdAdapterKeyPath();
            if (keyPath is null || values.Count == 0)
            {
                return false;
            }

            // Every value must match: a partial pack is not "applied".
            return values.All(value => RegistryHelper.IsMatch(keyPath, value.Name, value.Data));
        }

        private static string AdapterSubName(string keyPath) =>
            keyPath.Split('\\').Last();

        private sealed record SnapshotEntry(int Kind, string? Data, bool WasAbsent);

        private static string SnapshotValueName(string adapter, string valueName) => $"{adapter}_{valueName}";

        /// <summary>
        /// Records the pre-tweak (kind, data) once; later applies must not
        /// overwrite the original with our own values.
        /// </summary>
        private static void SnapshotOnce(string adapter, string keyPath, string valueName)
        {
            string snapshotName = SnapshotValueName(adapter, valueName);
            if (RegistryHelper.GetValue(SnapshotStoreKey, snapshotName) is string)
            {
                return;
            }

            object? current = RegistryHelper.GetValue(keyPath, valueName);
            SnapshotEntry entry = current switch
            {
                int i => new SnapshotEntry((int)RegistryValueKind.DWord, i.ToString(), false),
                string s => new SnapshotEntry((int)RegistryValueKind.String, s, false),
                null => new SnapshotEntry(0, null, true),
                _ => new SnapshotEntry(0, null, true), // unknown kind: restore = delete
            };

            RegistryHelper.SetValue(
                SnapshotStoreKey,
                snapshotName,
                Newtonsoft.Json.JsonConvert.SerializeObject(entry));
        }

        /// <summary>Restores a snapshotted value. Returns false when no snapshot exists.</summary>
        private static bool RestoreSnapshot(string adapter, string keyPath, string valueName)
        {
            string snapshotName = SnapshotValueName(adapter, valueName);
            if (RegistryHelper.GetValue(SnapshotStoreKey, snapshotName) is not string json)
            {
                return false;
            }

            try
            {
                SnapshotEntry? entry = Newtonsoft.Json.JsonConvert.DeserializeObject<SnapshotEntry>(json);
                if (entry is null)
                {
                    return false;
                }

                if (entry.WasAbsent || entry.Data is null)
                {
                    RegistryHelper.DeleteValue(keyPath, valueName);
                }
                else if (entry.Kind == (int)RegistryValueKind.String)
                {
                    RegistryHelper.SetValue(keyPath, valueName, entry.Data, RegistryValueKind.String);
                }
                else
                {
                    RegistryHelper.SetValue(keyPath, valueName, int.Parse(entry.Data), RegistryValueKind.DWord);
                }

                RegistryHelper.DeleteValue(SnapshotStoreKey, snapshotName);
                return true;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, $"[AMD] Could not restore snapshot for {valueName}; leaving current value.");
                return true; // snapshot unusable: do not fall through to delete
            }
        }
    }

    internal readonly record struct AmdRegistryValue(string Name, object Data, RegistryValueKind Kind)
    {
        public static AmdRegistryValue Dword(string name, int data) =>
            new(name, data, RegistryValueKind.DWord);

        public static AmdRegistryValue String(string name, string data) =>
            new(name, data, RegistryValueKind.String);
    }
}
