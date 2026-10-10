using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables memory compression for steadier latency under load.
    /// </summary>
    internal class MemoryCompressionConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _store;

        public MemoryCompressionConfigurationService(
            [FromKeyedServices("MemoryCompression")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            CommandPromptHelper.RunCommand(
                "powershell.exe -NoProfile -Command \"Disable-MMAgent -MemoryCompression\"");
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            CommandPromptHelper.RunCommand(
                "powershell.exe -NoProfile -Command \"Enable-MMAgent -MemoryCompression\"");
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            string output = CommandPromptHelper.RunCommand(
                "powershell.exe -NoProfile -Command \"(Get-MMAgent).MemoryCompression\"");
            return output.Trim().Equals("True", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
