#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// MMCSS scheduling profile for the Games task, matching the SynergyOS latency set.
    /// On = high priority scheduling, off = the Windows defaults (see
    /// "Revert Latency Tweaks.bat" in the SynergyOS playbook).
    /// </summary>
    public sealed class MmcssGamesTaskConfigurationService : IConfigurationService
    {
        private const string KeyPath =
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

        private readonly ConfigurationStore _store;

        public MmcssGamesTaskConfigurationService(
            [FromKeyedServices("MmcssGamesTask")] ConfigurationStore store)
        {
            _store = store;
        }

        public bool IsEnabled()
        {
            bool enabled =
                RegistryHelper.IsMatch(KeyPath, "GPU Priority", 8) &&
                RegistryHelper.IsMatch(KeyPath, "Priority", 6) &&
                RegistryHelper.IsMatch(KeyPath, "Scheduling Category", "High") &&
                RegistryHelper.IsMatch(KeyPath, "SFIO Priority", "High") &&
                RegistryHelper.IsMatch(KeyPath, "Latency Sensitive", "True");

            _store.CurrentSetting = enabled;
            return enabled;
        }

        public void Enable() => Apply(gpuPriority: 8, priority: 6, "High", "High", "True");

        public void Disable() => Apply(gpuPriority: 8, priority: 2, "Medium", "Normal", "False");

        private void Apply(int gpuPriority, int priority, string schedulingCategory, string sfioPriority, string latencySensitive)
        {
            try
            {
                RegistryHelper.SetValue(KeyPath, "GPU Priority", gpuPriority, RegistryValueKind.DWord);
                RegistryHelper.SetValue(KeyPath, "Priority", priority, RegistryValueKind.DWord);
                RegistryHelper.SetValue(KeyPath, "Scheduling Category", schedulingCategory, RegistryValueKind.String);
                RegistryHelper.SetValue(KeyPath, "SFIO Priority", sfioPriority, RegistryValueKind.String);
                RegistryHelper.SetValue(KeyPath, "Latency Sensitive", latencySensitive, RegistryValueKind.String);
            }
            catch (Exception exception)
            {
                // Defender can lock this key on hardened installs; report, do not crash the page.
                App.logger.Warn(exception, "[Latency] MMCSS Games task could not be written.");
                throw new InvalidOperationException(
                    "Windows did not allow the MMCSS Games task to be changed. Security software may be protecting it.",
                    exception);
            }

            _store.CurrentSetting = IsEnabled();
        }
    }
}
