using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS sets MenuShowDelay to 0 for snappier menus.
    /// Toggle on = optimized (0ms); off = Windows default (400).
    /// </summary>
    internal class InstantMenusConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\Control Panel\Desktop";
        private const string VALUE = "MenuShowDelay";
        private readonly ConfigurationStore _store;

        public InstantMenusConfigurationService(
            [FromKeyedServices("InstantMenus")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, "400", RegistryValueKind.String);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, "0", RegistryValueKind.String);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => RegistryHelper.IsMatch(KEY, VALUE, "0");
    }
}
