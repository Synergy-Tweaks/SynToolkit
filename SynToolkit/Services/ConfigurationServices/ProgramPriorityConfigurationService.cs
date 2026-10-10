using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS sets Win32PrioritySeparation to 36 (short, variable, foreground boost).
    /// Toggle on = optimized; off = Windows default (2).
    /// </summary>
    internal class ProgramPriorityConfigurationService : IConfigurationService
    {
        private const string KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl";
        private const string VALUE = "Win32PrioritySeparation";
        private const int OPTIMIZED = 36;
        private const int WINDOWS_DEFAULT = 2;

        private readonly ConfigurationStore _store;

        public ProgramPriorityConfigurationService(
            [FromKeyedServices("ProgramPriority")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(KEY, VALUE, WINDOWS_DEFAULT, RegistryValueKind.DWord);
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
