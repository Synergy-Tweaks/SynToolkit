using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS hides the taskbar search box.
    /// Toggle on = search box shown; off = hidden (SOS).
    /// </summary>
    internal class SearchBoxTaskbarConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Search";
        private const string VALUE = "SearchboxTaskbarMode";
        private readonly ConfigurationStore _store;

        public SearchBoxTaskbarConfigurationService(
            [FromKeyedServices("SearchBoxTaskbar")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 0, RegistryValueKind.DWord);
            CommandPromptHelper.RestartExplorer();
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 1, RegistryValueKind.DWord);
            CommandPromptHelper.RestartExplorer();
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(KEY, VALUE, 0);
    }
}
