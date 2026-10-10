using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS: fsutil disablelastaccess 1 and disable8dot3 1.
    /// Toggle on = optimizations applied.
    /// </summary>
    internal class NtfsOptimizationConfigurationService : IConfigurationService
    {
        private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\NtfsOptimization";
        private readonly ConfigurationStore _store;

        public NtfsOptimizationConfigurationService(
            [FromKeyedServices("NtfsOptimization")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CommandPromptHelper.RunCommand("fsutil behavior set disablelastaccess 0");
            CommandPromptHelper.RunCommand("fsutil behavior set disable8dot3 0");
            RegistryHelper.SetValue(STORE_KEY, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            CommandPromptHelper.RunCommand("fsutil behavior set disablelastaccess 1");
            CommandPromptHelper.RunCommand("fsutil behavior set disable8dot3 1");
            RegistryHelper.SetValue(STORE_KEY, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            string lastAccess = CommandPromptHelper.RunCommand("fsutil behavior query disablelastaccess");
            string eightDotThree = CommandPromptHelper.RunCommand("fsutil behavior query disable8dot3");
            bool lastAccessOff = lastAccess.Contains("1") || lastAccess.Contains("= 2") || lastAccess.Contains("= 3");
            bool eightOff = eightDotThree.Contains("1") || eightDotThree.Contains("= 1") || eightDotThree.Contains("= 2");
            return lastAccessOff && eightOff;
        }
    }
}
