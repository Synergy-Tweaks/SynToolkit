using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables transparency effects.
    /// Toggle on = transparency enabled; off = disabled (SOS).
    /// </summary>
    internal class TransparencyEffectsConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string VALUE = "EnableTransparency";
        private readonly ConfigurationStore _store;

        public TransparencyEffectsConfigurationService(
            [FromKeyedServices("TransparencyEffects")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(KEY, VALUE, 0);
    }
}
