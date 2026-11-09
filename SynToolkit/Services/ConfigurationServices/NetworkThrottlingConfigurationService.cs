#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// NetworkThrottlingIndex. On = 0xFFFFFFFF (throttling off), off = 10 (Windows default).
    /// </summary>
    public sealed class NetworkThrottlingConfigurationService : IConfigurationService
    {
        private const string KeyPath =
            @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const string ValueName = "NetworkThrottlingIndex";
        private const int ThrottlingDisabled = -1; // 0xFFFFFFFF
        private const int WindowsDefault = 10;

        private readonly ConfigurationStore _store;

        public NetworkThrottlingConfigurationService(
            [FromKeyedServices("NetworkThrottling")] ConfigurationStore store)
        {
            _store = store;
        }

        public bool IsEnabled()
        {
            // A missing value is the Windows default (throttling on), so this toggle is off.
            bool enabled = RegistryHelper.IsMatch(KeyPath, ValueName, ThrottlingDisabled);
            _store.CurrentSetting = enabled;
            return enabled;
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KeyPath, ValueName, ThrottlingDisabled, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KeyPath, ValueName, WindowsDefault, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }
    }
}
