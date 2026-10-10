using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS blocks Windows Update driver search / delivery.
    /// Toggle on = driver updates allowed; off = excluded (SOS).
    /// </summary>
    public class DriverUpdatesConfigurationService : IConfigurationService
    {
        private const string DEVICE_UPDATE_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\PolicyManager\current\device\Update";
        private const string DEFAULT_UPDATE_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\PolicyManager\default\Update";
        private const string SETTINGS_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        private const string WINDOWS_UPDATE_KEY_NAME = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\PolicyManager\default\Update\ExcludeWUDriversInQualityUpdate";
        private const string DEVICE_METADATA_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Metadata";
        private const string DRIVER_SEARCHING_KEY_NAME = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching";

        private const string EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME = "ExcludeWUDriversInQualityUpdate";
        private const string VALUE_VALUE_NAME = "value";
        private const string PREVENT_DEVICE_METADATA_FROM_NETWORK_VALUE_NAME = "PreventDeviceMetadataFromNetwork";
        private const string SEARCH_ORDER_CONFIG_VALUE_NAME = "SearchOrderConfig";
        private const string DONT_SEARCH_WINDOWS_UPDATE_VALUE_NAME = "DontSearchWindowsUpdate";

        private readonly ConfigurationStore _driverUpdatesConfigurationStore;

        public DriverUpdatesConfigurationService(
            [FromKeyedServices("DriverUpdates")] ConfigurationStore driverUpdatesConfigurationStore)
        {
            _driverUpdatesConfigurationStore = driverUpdatesConfigurationStore;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(DEVICE_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DEFAULT_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SETTINGS_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(WINDOWS_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_KEY_NAME, VALUE_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DEVICE_METADATA_KEY_NAME, PREVENT_DEVICE_METADATA_FROM_NETWORK_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DRIVER_SEARCHING_KEY_NAME, SEARCH_ORDER_CONFIG_VALUE_NAME, 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DRIVER_SEARCHING_KEY_NAME, DONT_SEARCH_WINDOWS_UPDATE_VALUE_NAME, 1, RegistryValueKind.DWord);

            _driverUpdatesConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(DEVICE_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME);
            RegistryHelper.DeleteValue(DEFAULT_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME);
            RegistryHelper.DeleteValue(SETTINGS_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME);
            RegistryHelper.DeleteValue(WINDOWS_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME);
            RegistryHelper.SetValue(EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_KEY_NAME, VALUE_VALUE_NAME, 0, RegistryValueKind.DWord);
            RegistryHelper.DeleteValue(DEVICE_METADATA_KEY_NAME, PREVENT_DEVICE_METADATA_FROM_NETWORK_VALUE_NAME);
            RegistryHelper.SetValue(DRIVER_SEARCHING_KEY_NAME, SEARCH_ORDER_CONFIG_VALUE_NAME, 1, RegistryValueKind.DWord);
            RegistryHelper.DeleteValue(DRIVER_SEARCHING_KEY_NAME, DONT_SEARCH_WINDOWS_UPDATE_VALUE_NAME);

            _driverUpdatesConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return !RegistryHelper.IsMatch(WINDOWS_UPDATE_KEY_NAME, EXCLUDE_WU_DRIVERS_IN_QUALITY_UPDATE_VALUE_NAME, 1)
                && !RegistryHelper.IsMatch(DRIVER_SEARCHING_KEY_NAME, DONT_SEARCH_WINDOWS_UPDATE_VALUE_NAME, 1);
        }
    }
}
