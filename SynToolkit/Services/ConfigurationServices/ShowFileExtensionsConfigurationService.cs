using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS shows file extensions in Explorer (HideFileExt=0).
    /// </summary>
    internal class ShowFileExtensionsConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string VALUE = "HideFileExt";
        private readonly ConfigurationStore _store;

        public ShowFileExtensionsConfigurationService(
            [FromKeyedServices("ShowFileExtensions")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 1, RegistryValueKind.DWord);
            CommandPromptHelper.RestartExplorer();
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 0, RegistryValueKind.DWord);
            CommandPromptHelper.RestartExplorer();
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => RegistryHelper.IsMatch(KEY, VALUE, 0);
    }
}
