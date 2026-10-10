using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS sets bcdedit disabledynamictick yes.
    /// Toggle on = dynamic tick enabled (Windows default); off = disabled (SOS).
    /// </summary>
    internal class DynamicTickConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _store;

        public DynamicTickConfigurationService(
            [FromKeyedServices("DynamicTick")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CommandPromptHelper.RunCommand("bcdedit /set disabledynamictick yes");
            App.ContentDialogCaller("restart");
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            CommandPromptHelper.RunCommand("bcdedit /deletevalue disabledynamictick");
            App.ContentDialogCaller("restart");
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            string output = CommandPromptHelper.RunCommand("bcdedit /enum {current}");
            // When disabledynamictick is Yes, dynamic tick is off.
            foreach (string line in output.Split('\n'))
            {
                if (line.IndexOf("disabledynamictick", System.StringComparison.OrdinalIgnoreCase) >= 0
                    && line.IndexOf("Yes", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
