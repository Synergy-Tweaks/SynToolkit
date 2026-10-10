using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS sets JPEGImportQuality to 100.
    /// </summary>
    internal class WallpaperQualityConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\Control Panel\Desktop";
        private const string VALUE = "JPEGImportQuality";
        private readonly ConfigurationStore _store;

        public WallpaperQualityConfigurationService(
            [FromKeyedServices("WallpaperQuality")] ConfigurationStore store)
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
            RegistryHelper.SetValue(KEY, VALUE, 100, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => RegistryHelper.IsMatch(KEY, VALUE, 100);
    }
}
