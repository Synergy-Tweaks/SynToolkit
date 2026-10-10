using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS enables Win32 long paths.
    /// </summary>
    internal class LongPathsConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem";
        private const string VALUE = "LongPathsEnabled";
        private readonly ConfigurationStore _store;

        public LongPathsConfigurationService(
            [FromKeyedServices("LongPaths")] ConfigurationStore store)
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

        public bool IsEnabled() => RegistryHelper.IsMatch(KEY, VALUE, 1);
    }
}
