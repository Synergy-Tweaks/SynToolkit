using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS hides the Recommended section in Start.
    /// Toggle on = recommended shown; off = hidden (SOS).
    /// </summary>
    internal class StartRecommendedConfigurationService : IConfigurationService
    {
        private const string POLICY_KEY = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Explorer";
        private const string VALUE = "HideRecommendedSection";
        private readonly ConfigurationStore _store;

        public StartRecommendedConfigurationService(
            [FromKeyedServices("StartRecommended")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(POLICY_KEY, VALUE, 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(POLICY_KEY, VALUE);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(POLICY_KEY, VALUE, 1);
    }
}
