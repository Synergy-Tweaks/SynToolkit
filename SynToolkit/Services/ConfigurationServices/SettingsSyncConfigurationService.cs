using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables Windows Settings Sync.
    /// Toggle on = sync allowed; off = disabled (SOS).
    /// </summary>
    internal class SettingsSyncConfigurationService : IConfigurationService
    {
        private const string POLICY_KEY = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\SettingSync";
        private const string USER_KEY = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync";

        private readonly ConfigurationStore _store;

        public SettingsSyncConfigurationService(
            [FromKeyedServices("SettingsSync")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(POLICY_KEY, "DisableSettingSync", 2, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POLICY_KEY, "DisableSettingSyncUserOverride", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(USER_KEY, "SyncPolicy", 5, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(POLICY_KEY, "DisableSettingSync");
            RegistryHelper.DeleteValue(POLICY_KEY, "DisableSettingSyncUserOverride");
            RegistryHelper.DeleteValue(USER_KEY, "SyncPolicy");
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() =>
            !RegistryHelper.IsMatch(POLICY_KEY, "DisableSettingSync", 2)
            && !RegistryHelper.IsMatch(USER_KEY, "SyncPolicy", 5);
    }
}
