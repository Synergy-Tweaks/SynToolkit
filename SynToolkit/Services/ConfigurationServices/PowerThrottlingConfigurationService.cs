#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// EcoQoS / power throttling. On = PowerThrottlingOff 1, off = the value is removed so
    /// Windows throttles background work again (the SynergyOS default).
    /// </summary>
    public sealed class PowerThrottlingConfigurationService : IConfigurationService
    {
        private const string KeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
        private const string ValueName = "PowerThrottlingOff";

        private readonly ConfigurationStore _store;

        public PowerThrottlingConfigurationService(
            [FromKeyedServices("PowerThrottling")] ConfigurationStore store)
        {
            _store = store;
        }

        public bool IsEnabled()
        {
            bool enabled = RegistryHelper.IsMatch(KeyPath, ValueName, 1);
            _store.CurrentSetting = enabled;
            return enabled;
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KeyPath, ValueName, 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            RegistryHelper.DeleteValue(KeyPath, ValueName);
            _store.CurrentSetting = IsEnabled();
        }
    }
}
