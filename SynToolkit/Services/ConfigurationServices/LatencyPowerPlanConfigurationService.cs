#nullable enable

using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Latency-oriented AC values for the active power scheme, matching the SynergyOS
    /// "Revert Latency Tweaks.bat" defaults. The values present before the first enable are
    /// stored in a small JSON file, and disabling restores those instead of assuming defaults.
    /// </summary>
    public sealed class LatencyPowerPlanConfigurationService : IConfigurationService
    {
        private sealed record PowerSetting(string SubgroupId, string SettingId, uint OnValue, uint DefaultValue);

        private static readonly PowerSetting[] Settings =
        [
            // SUB_PCIEXPRESS / ASPM
            new("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0, 0),
            // USB selective suspend
            new("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, 1),
            // SUB_PROCESSOR / core parking min cores
            new("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100, 0),
            // SUB_PROCESSOR / processor performance boost mode
            new("54533251-82be-4824-96c1-47b60b740d00", "be337238-0d82-4146-a960-4f3749d470c7", 2, 1),
            // SUB_PROCESSOR / minimum processor state
            new("54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", 20, 5),
        ];

        private static readonly Regex AcIndexPattern =
            new(@"Current AC Power Setting Index:\s*0x([0-9A-Fa-f]+)", RegexOptions.Compiled);

        private readonly ConfigurationStore _store;

        public LatencyPowerPlanConfigurationService(
            [FromKeyedServices("LatencyPowerPlan")] ConfigurationStore store)
        {
            _store = store;
        }

        private static string BackupPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SynToolkit",
            "LatencyPowerBackup.json");

        public bool IsEnabled()
        {
            bool anyRead = false;
            foreach (PowerSetting setting in Settings)
            {
                if (!TryReadAc(setting, out uint value))
                {
                    // Not exposed on this CPU - leave it out of the decision.
                    continue;
                }

                anyRead = true;
                if (value != setting.OnValue)
                {
                    _store.CurrentSetting = false;
                    return false;
                }
            }

            _store.CurrentSetting = anyRead;
            return anyRead;
        }

        public void Enable()
        {
            CaptureBackupIfMissing();

            foreach (PowerSetting setting in Settings)
            {
                // Some CPUs do not expose CPMINCORES/PERFBOOSTMODE; a rejected write is fine.
                WriteAc(setting.SettingId, setting.SubgroupId, setting.OnValue);
            }

            ApplyActiveScheme();
            _store.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            Dictionary<string, uint>? backup = ReadBackup();

            foreach (PowerSetting setting in Settings)
            {
                uint value = setting.DefaultValue;
                if (backup is not null && backup.TryGetValue(setting.SettingId, out uint saved))
                {
                    value = saved;
                }

                WriteAc(setting.SettingId, setting.SubgroupId, value);
            }

            ApplyActiveScheme();
            DeleteBackup();
            _store.CurrentSetting = IsEnabled();
        }

        private static bool TryReadAc(PowerSetting setting, out uint value)
        {
            value = 0;
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powercfg.exe",
                ["/query", "SCHEME_CURRENT", setting.SubgroupId, setting.SettingId],
                timeoutMilliseconds: 20_000);

            if (!result.Succeeded)
            {
                return false;
            }

            Match match = AcIndexPattern.Match(result.StandardOutput);
            if (!match.Success)
            {
                return false;
            }

            return uint.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static void WriteAc(string settingId, string subgroupId, uint value)
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powercfg.exe",
                ["/setacvalueindex", "SCHEME_CURRENT", subgroupId, settingId, value.ToString(CultureInfo.InvariantCulture)],
                timeoutMilliseconds: 20_000);

            if (!result.Succeeded)
            {
                App.logger.Info(
                    "[Latency] powercfg rejected {Subgroup}\\{Setting} = {Value}; the CPU does not expose it.",
                    subgroupId,
                    settingId,
                    value);
            }
        }

        private static void ApplyActiveScheme()
        {
            CommandPromptHelper.RunProcessResult(
                "powercfg.exe",
                ["/setactive", "SCHEME_CURRENT"],
                timeoutMilliseconds: 20_000);
        }

        private static void CaptureBackupIfMissing()
        {
            if (File.Exists(BackupPath))
            {
                return;
            }

            Dictionary<string, uint> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (PowerSetting setting in Settings)
            {
                if (TryReadAc(setting, out uint current))
                {
                    values[setting.SettingId] = current;
                }
            }

            if (values.Count == 0)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
                File.WriteAllText(BackupPath, JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Latency] Could not store the power-plan backup.");
            }
        }

        private static Dictionary<string, uint>? ReadBackup()
        {
            try
            {
                if (!File.Exists(BackupPath))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<Dictionary<string, uint>>(File.ReadAllText(BackupPath));
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Latency] Could not read the power-plan backup; using Windows defaults.");
                return null;
            }
        }

        private static void DeleteBackup()
        {
            try
            {
                if (File.Exists(BackupPath))
                {
                    File.Delete(BackupPath);
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Latency] Could not remove the power-plan backup.");
            }
        }
    }
}
