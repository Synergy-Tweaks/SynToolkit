using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS sets DmaRemappingCompatible=0 under device services.
    /// Toggle on = remapping allowed; off = forced incompatible (SOS).
    /// </summary>
    internal class DmaRemappingConfigurationService : IConfigurationService
    {
        private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\DmaRemapping";
        private readonly ConfigurationStore _store;

        public DmaRemappingConfigurationService(
            [FromKeyedServices("DmaRemapping")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CommandPromptHelper.RunCommand(
                "powershell.exe -NoProfile -Command \"Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Services' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.GetValue('DmaRemappingCompatible') -ne $null } | ForEach-Object { Set-ItemProperty -Path $_.PSPath -Name DmaRemappingCompatible -Value 0 -Type DWord -Force }\"");
            RegistryHelper.SetValue(STORE_KEY, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(STORE_KEY, "state");
            // Restoring per-device defaults is not reliable; leave values and mark enabled via store absence + no forced 0 state.
            RegistryHelper.SetValue(STORE_KEY, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(STORE_KEY, "state", 0);
    }
}
