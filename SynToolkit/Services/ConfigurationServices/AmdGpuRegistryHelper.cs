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
            foreach (AmdRegistryValue value in values)
            {
                RegistryHelper.SetValue(keyPath, value.Name, value.Data, value.Kind);
            }
        }

        public static void RevertValues(IReadOnlyList<AmdRegistryValue> values)
        {
            string keyPath = RequireAmdAdapterKeyPath();
            foreach (AmdRegistryValue value in values)
            {
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

            // Marker check: first value must match; avoids brittle all-or-nothing on partial packs.
            AmdRegistryValue marker = values[0];
            return RegistryHelper.IsMatch(keyPath, marker.Name, marker.Data);
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
