using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS enables AutoEndTasks and shortens hung-app timeouts.
    /// </summary>
    internal class AutoEndTasksConfigurationService : IConfigurationService
    {
        private const string DESKTOP_KEY = @"HKCU\Control Panel\Desktop";
        private const string CONTROL_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control";
        private readonly ConfigurationStore _store;

        public AutoEndTasksConfigurationService(
            [FromKeyedServices("AutoEndTasks")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.DeleteValue(DESKTOP_KEY, "AutoEndTasks");
            RegistryHelper.DeleteValue(DESKTOP_KEY, "HungAppTimeout");
            RegistryHelper.DeleteValue(DESKTOP_KEY, "WaitToKillTimeout");
            RegistryHelper.DeleteValue(CONTROL_KEY, "WaitToKillServiceTimeout");
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(DESKTOP_KEY, "AutoEndTasks", "1", RegistryValueKind.String);
            RegistryHelper.SetValue(DESKTOP_KEY, "HungAppTimeout", "1500", RegistryValueKind.String);
            RegistryHelper.SetValue(DESKTOP_KEY, "WaitToKillTimeout", "2500", RegistryValueKind.String);
            RegistryHelper.SetValue(CONTROL_KEY, "WaitToKillServiceTimeout", "2500", RegistryValueKind.String);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => RegistryHelper.IsMatch(DESKTOP_KEY, "AutoEndTasks", "1");
    }
}
