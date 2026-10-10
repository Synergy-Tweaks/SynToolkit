using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS raises SvcHostSplitThresholdInKB so service hosts split more freely.
    /// </summary>
    internal class SvcHostSplitConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKLM\SYSTEM\CurrentControlSet\Control";
        private const string VALUE = "SvcHostSplitThresholdInKB";
        private const int OPTIMIZED = unchecked((int)4294967295);

        private readonly ConfigurationStore _store;

        public SvcHostSplitConfigurationService(
            [FromKeyedServices("SvcHostSplit")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.DeleteValue(KEY, VALUE);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, OPTIMIZED, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => RegistryHelper.IsMatch(KEY, VALUE, OPTIMIZED);
    }
}
