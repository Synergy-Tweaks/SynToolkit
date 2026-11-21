#nullable enable

using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SynToolkit.Services.ConfigurationServices
{
    public class DarkTitlebarsConfigurationService : IConfigurationService
    {
        private const string DWM_KEY_NAME = @"HKCU\SOFTWARE\Microsoft\Windows\DWM";
        private const string ACCENT_KEY_NAME = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const string STORE_KEY_NAME = @"HKLM\SOFTWARE\SynToolkit\Services\DarkTitlebars";
        private const string BACKUP_VALUE_NAME = "OriginalColours";

        private const string COLORIZATION_COLOR_VALUE_NAME = "ColorizationColor";
        private const string COLORIZATION_AFTERGLOW_VALUE_NAME = "ColorizationAfterglow";
        private const string ACCENT_COLOR_VALUE_NAME = "AccentColor";
        private const string COLOR_PREVALENCE_VALUE_NAME = "ColorPrevalence";
        private const string ACCENT_COLOR_INACTIVE_VALUE_NAME = "AccentColorInactive";
        private const string ACCENT_COLOR_MENU_VALUE_NAME = "AccentColorMenu";

        // Every value this service ever writes. Captured before the first write so Disable can
        // put the user's own accent back instead of a canned palette.
        private static readonly (string KeyPath, string ValueName)[] ManagedValues =
        {
            (DWM_KEY_NAME, COLORIZATION_COLOR_VALUE_NAME),
            (DWM_KEY_NAME, COLORIZATION_AFTERGLOW_VALUE_NAME),
            (DWM_KEY_NAME, ACCENT_COLOR_VALUE_NAME),
            (DWM_KEY_NAME, COLOR_PREVALENCE_VALUE_NAME),
            (DWM_KEY_NAME, ACCENT_COLOR_INACTIVE_VALUE_NAME),
            (ACCENT_KEY_NAME, ACCENT_COLOR_MENU_VALUE_NAME),
        };

        private readonly ConfigurationStore _darkTitlebarsConfigurationStore;

        public DarkTitlebarsConfigurationService(
            [FromKeyedServices("DarkTitlebars")] ConfigurationStore darkTitlebarsConfigurationStore)
        {
            _darkTitlebarsConfigurationStore = darkTitlebarsConfigurationStore;
        }

        public void Disable()
        {
            if (HasBackup())
            {
                RestoreOriginalSettings();
            }
            else if (IsEnabled())
            {
                // Enabled by an older build that kept no backup, so the original colours are
                // unknown. Keep the previous fixed palette rather than doing nothing.
                ApplyLegacyPalette();
            }

            _darkTitlebarsConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            CaptureOriginalSettings();

            RegistryHelper.SetValue(DWM_KEY_NAME, COLORIZATION_COLOR_VALUE_NAME, 0xc4000000, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DWM_KEY_NAME, COLORIZATION_AFTERGLOW_VALUE_NAME, 0xc4000000, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DWM_KEY_NAME, ACCENT_COLOR_VALUE_NAME, 0xff000000, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DWM_KEY_NAME, COLOR_PREVALENCE_VALUE_NAME, 1);
            RegistryHelper.SetValue(DWM_KEY_NAME, ACCENT_COLOR_INACTIVE_VALUE_NAME, 0xff2b2b2b, RegistryValueKind.DWord);
            RegistryHelper.SetValue(ACCENT_KEY_NAME, ACCENT_COLOR_MENU_VALUE_NAME, 0xc4000000, RegistryValueKind.DWord);

            _darkTitlebarsConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            bool[] checks =
            {
                RegistryHelper.IsMatch(DWM_KEY_NAME, COLORIZATION_COLOR_VALUE_NAME, 0xc4000000),
                RegistryHelper.IsMatch(DWM_KEY_NAME, COLORIZATION_AFTERGLOW_VALUE_NAME, 0xc4000000),
                RegistryHelper.IsMatch(DWM_KEY_NAME, ACCENT_COLOR_VALUE_NAME, 0xff000000),
                RegistryHelper.IsMatch(DWM_KEY_NAME, COLOR_PREVALENCE_VALUE_NAME, 1),
                RegistryHelper.IsMatch(DWM_KEY_NAME, ACCENT_COLOR_INACTIVE_VALUE_NAME, 0xff2b2b2b),
                RegistryHelper.IsMatch(ACCENT_KEY_NAME, ACCENT_COLOR_MENU_VALUE_NAME, 0xc4000000)
            };

            return checks.All(x => x);
        }

        private static void ApplyLegacyPalette()
        {
            RegistryHelper.SetValue(DWM_KEY_NAME, COLORIZATION_COLOR_VALUE_NAME, 0xc40078d7, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DWM_KEY_NAME, COLORIZATION_AFTERGLOW_VALUE_NAME, 0xc40078d7, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DWM_KEY_NAME, ACCENT_COLOR_VALUE_NAME, 0xffd77800, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DWM_KEY_NAME, COLOR_PREVALENCE_VALUE_NAME, 0x0, RegistryValueKind.DWord);
            RegistryHelper.DeleteValue(DWM_KEY_NAME, ACCENT_COLOR_INACTIVE_VALUE_NAME);
            RegistryHelper.SetValue(ACCENT_KEY_NAME, ACCENT_COLOR_MENU_VALUE_NAME, 0xffd77800, RegistryValueKind.DWord);
        }

        private static bool HasBackup() =>
            RegistryHelper.GetValue(STORE_KEY_NAME, BACKUP_VALUE_NAME) is string json &&
            !string.IsNullOrWhiteSpace(json);

        private static void CaptureOriginalSettings()
        {
            if (HasBackup())
            {
                return;
            }

            List<CapturedColour> captured = new();
            foreach ((string keyPath, string valueName) in ManagedValues)
            {
                if (!RegistryHelper.TryReadValueWithKind(keyPath, valueName, out object? value, out RegistryValueKind? kind))
                {
                    // Refuse to start on a machine whose colours we could not put back.
                    throw new InvalidOperationException(
                        $"Could not read the current {valueName} colour. Nothing was changed.");
                }

                if (value is null)
                {
                    captured.Add(new CapturedColour { KeyPath = keyPath, ValueName = valueName });
                    continue;
                }

                if (kind != RegistryValueKind.DWord || value is not int dword)
                {
                    throw new NotSupportedException(
                        $"The {valueName} value is not a DWORD, so SynToolkit cannot restore it.");
                }

                captured.Add(new CapturedColour { KeyPath = keyPath, ValueName = valueName, Value = dword });
            }

            RegistryHelper.SetValue(STORE_KEY_NAME, BACKUP_VALUE_NAME, JsonConvert.SerializeObject(captured), RegistryValueKind.String);
        }

        private static void RestoreOriginalSettings()
        {
            if (RegistryHelper.GetValue(STORE_KEY_NAME, BACKUP_VALUE_NAME) is not string json ||
                JsonConvert.DeserializeObject<List<CapturedColour>>(json) is not { } captured)
            {
                return;
            }

            foreach (CapturedColour entry in captured)
            {
                if (entry.Value is int value)
                {
                    RegistryHelper.SetValue(entry.KeyPath, entry.ValueName, value, RegistryValueKind.DWord);
                }
                else
                {
                    RegistryHelper.DeleteValue(entry.KeyPath, entry.ValueName);
                }
            }

            RegistryHelper.DeleteValue(STORE_KEY_NAME, BACKUP_VALUE_NAME);
        }

        private sealed class CapturedColour
        {
            public string KeyPath { get; set; } = string.Empty;
            public string ValueName { get; set; } = string.Empty;
            // null = the value did not exist before SynToolkit wrote it.
            public int? Value { get; set; }
        }
    }
}
