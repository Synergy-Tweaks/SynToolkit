using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables Aero Shake (DisallowShaking=1).
    /// Toggle on = shake minimize enabled; off = disabled (SOS).
    /// </summary>
    internal class AeroShakeConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string VALUE = "DisallowShaking";
        private readonly ConfigurationStore _store;

        public AeroShakeConfigurationService(
            [FromKeyedServices("AeroShake")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(KEY, VALUE, 1);
    }
}
