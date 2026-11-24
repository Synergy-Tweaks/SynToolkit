using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Text.RegularExpressions;

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
            // Values per fsutil: 0 = updates enabled, 1 = disabled, 2 = system managed with
            // updates enabled, 3 = system managed with updates disabled. Only 1 and 3 mean off.
            int lastAccess = ReadBehaviorValue("disablelastaccess");
            int eightDotThree = ReadBehaviorValue("disable8dot3");
            return lastAccess is 1 or 3 && eightDotThree is 1 or 3;
        }

        private static int ReadBehaviorValue(string behaviorName) =>
            ParseBehaviorValue(CommandPromptHelper.RunCommand($"fsutil behavior query {behaviorName}"));

        private static int ParseBehaviorValue(string output)
        {
            // fsutil prints "DisableLastAccess = 1 (...)" but "The registry state is: 1 (...)"
            // for disable8dot3, so accept both separators.
            Match match = Regex.Match(output, @"[=:]\s*(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : -1;
        }

        static NtfsOptimizationConfigurationService()
        {
            System.Diagnostics.Debug.Assert(
                ParseBehaviorValue("DisableLastAccess = 2  (System Managed, Last Access Updates Enabled)") == 2);
            System.Diagnostics.Debug.Assert(
                ParseBehaviorValue("The registry state is: 1 (8dot3 name creation is DISABLED on all volumes)") == 1);
            System.Diagnostics.Debug.Assert(ParseBehaviorValue("unexpected output") == -1);
        }
    }
}
