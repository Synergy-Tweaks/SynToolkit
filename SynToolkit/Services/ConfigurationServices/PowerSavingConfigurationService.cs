using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables USB/device selective-suspend style power saving flags.
    /// Toggle on = power saving allowed; off = forced off (SOS).
    /// </summary>
    internal class PowerSavingConfigurationService : IConfigurationService
    {
        private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\PowerSaving";
        private const string USB_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\usbflags";
        private const string STORAGE_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Storage";
        private const string NVME_KEY = @"HKLM\SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device";

        private readonly ConfigurationStore _store;

        public PowerSavingConfigurationService(
            [FromKeyedServices("PowerSaving")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(USB_KEY, "DisableHCS0Idle", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(STORAGE_KEY, "StorageD3InModernStandby", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(NVME_KEY, "IdlePowerMode", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(STORE_KEY, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(USB_KEY, "DisableHCS0Idle");
            RegistryHelper.DeleteValue(STORAGE_KEY, "StorageD3InModernStandby");
            RegistryHelper.DeleteValue(NVME_KEY, "IdlePowerMode");
            RegistryHelper.SetValue(STORE_KEY, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(STORE_KEY, "state", 0);
    }
}
