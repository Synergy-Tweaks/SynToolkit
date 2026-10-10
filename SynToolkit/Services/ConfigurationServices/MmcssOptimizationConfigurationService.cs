using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS MMCSS profile: SystemResponsiveness=10, NoLazyMode=1.
    /// </summary>
    internal class MmcssOptimizationConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private readonly ConfigurationStore _store;

        public MmcssOptimizationConfigurationService(
            [FromKeyedServices("MmcssOptimization")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, "SystemResponsiveness", 20, RegistryValueKind.DWord);
            RegistryHelper.DeleteValue(KEY, "NoLazyMode");
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, "SystemResponsiveness", 10, RegistryValueKind.DWord);
            RegistryHelper.SetValue(KEY, "NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
            RegistryHelper.SetValue(KEY, "NoLazyMode", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() =>
            RegistryHelper.IsMatch(KEY, "SystemResponsiveness", 10)
            && RegistryHelper.IsMatch(KEY, "NoLazyMode", 1);
    }
}
