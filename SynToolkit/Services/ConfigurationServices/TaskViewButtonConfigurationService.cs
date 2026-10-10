using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS hides the Task View button on the taskbar.
    /// </summary>
    internal class TaskViewButtonConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string VALUE = "ShowTaskViewButton";
        private readonly ConfigurationStore _store;

        public TaskViewButtonConfigurationService(
            [FromKeyedServices("TaskViewButton")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 0, RegistryValueKind.DWord);
            CommandPromptHelper.RestartExplorer();
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(KEY, VALUE, 1, RegistryValueKind.DWord);
            CommandPromptHelper.RestartExplorer();
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => !RegistryHelper.IsMatch(KEY, VALUE, 0);
    }
}
