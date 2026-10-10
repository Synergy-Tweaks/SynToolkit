using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables Enhance Pointer Precision (mouse acceleration).
    /// Toggle on = acceleration enabled; off = raw-ish 1:1 (SOS).
    /// </summary>
    internal class EnhancePointerPrecisionConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\Control Panel\Mouse";
        private readonly ConfigurationStore _store;

        public EnhancePointerPrecisionConfigurationService(
            [FromKeyedServices("EnhancePointerPrecision")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, "MouseSpeed", "0", RegistryValueKind.String);
            RegistryHelper.SetValue(KEY, "MouseThreshold1", "0", RegistryValueKind.String);
            RegistryHelper.SetValue(KEY, "MouseThreshold2", "0", RegistryValueKind.String);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, "MouseSpeed", "1", RegistryValueKind.String);
            RegistryHelper.SetValue(KEY, "MouseThreshold1", "6", RegistryValueKind.String);
            RegistryHelper.SetValue(KEY, "MouseThreshold2", "10", RegistryValueKind.String);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() =>
            !RegistryHelper.IsMatch(KEY, "MouseSpeed", "0")
            || !RegistryHelper.IsMatch(KEY, "MouseThreshold1", "0");
    }
}
