using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables Sleep Study and related power telemetry.
    /// Toggle on = Sleep Study allowed; off = disabled (SOS).
    /// </summary>
    internal class SleepStudyConfigurationService : IConfigurationService
    {
        private const string POWER_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
        private const string SESSION_POWER_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private const string WDF_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Wdf";

        private readonly ConfigurationStore _store;

        public SleepStudyConfigurationService(
            [FromKeyedServices("SleepStudy")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.SetValue(POWER_KEY, "SleepStudyDisabled", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POWER_KEY, "SleepstudyAccountingEnabled", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(POWER_KEY, "FxAccountingTelemetryDisabled", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SESSION_POWER_KEY, "SleepStudyDisabled", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(WDF_KEY, "WdfGlobalSleepStudyDisabled", 1, RegistryValueKind.DWord);
            CommandPromptHelper.RunCommand("wevtutil.exe set-log \"Microsoft-Windows-SleepStudy/Diagnostic\" /e:false");
            CommandPromptHelper.RunCommand("schtasks /Change /TN \"\\Microsoft\\Windows\\Power Efficiency Diagnostics\\AnalyzeSystem\" /Disable");
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(POWER_KEY, "SleepStudyDisabled");
            RegistryHelper.DeleteValue(POWER_KEY, "SleepstudyAccountingEnabled");
            RegistryHelper.DeleteValue(POWER_KEY, "FxAccountingTelemetryDisabled");
            RegistryHelper.DeleteValue(SESSION_POWER_KEY, "SleepStudyDisabled");
            RegistryHelper.DeleteValue(WDF_KEY, "WdfGlobalSleepStudyDisabled");
            CommandPromptHelper.RunCommand("wevtutil.exe set-log \"Microsoft-Windows-SleepStudy/Diagnostic\" /e:true");
            CommandPromptHelper.RunCommand("schtasks /Change /TN \"\\Microsoft\\Windows\\Power Efficiency Diagnostics\\AnalyzeSystem\" /Enable");
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() =>
            !RegistryHelper.IsMatch(POWER_KEY, "SleepStudyDisabled", 1)
            && !RegistryHelper.IsMatch(SESSION_POWER_KEY, "SleepStudyDisabled", 1);
    }
}
