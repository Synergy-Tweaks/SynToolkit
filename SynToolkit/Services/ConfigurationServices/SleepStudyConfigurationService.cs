using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables Sleep Study and related power telemetry.
    /// Toggle on = Sleep Study allowed; off = disabled (SOS).
    /// The event channel and scheduled task don't exist on machines without
    /// S0 standby: those steps are skipped (logged) instead of throwing.
    /// </summary>
    internal class SleepStudyConfigurationService : IConfigurationService
    {
        private const string POWER_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Power";
        private const string SESSION_POWER_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private const string WDF_KEY = @"HKLM\SYSTEM\CurrentControlSet\Control\Wdf";

        private const string SLEEP_STUDY_CHANNEL = "Microsoft-Windows-SleepStudy/Diagnostic";
        private const string CHANNEL_REG_KEY =
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT\Channels\Microsoft-Windows-SleepStudy/Diagnostic";
        private const string POWER_EFFICIENCY_TASK = @"\Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem";

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
            SetEventChannelEnabled(false);
            SetEfficiencyTaskEnabled(false);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.DeleteValue(POWER_KEY, "SleepStudyDisabled");
            RegistryHelper.DeleteValue(POWER_KEY, "SleepstudyAccountingEnabled");
            RegistryHelper.DeleteValue(POWER_KEY, "FxAccountingTelemetryDisabled");
            RegistryHelper.DeleteValue(SESSION_POWER_KEY, "SleepStudyDisabled");
            RegistryHelper.DeleteValue(WDF_KEY, "WdfGlobalSleepStudyDisabled");
            SetEventChannelEnabled(true);
            SetEfficiencyTaskEnabled(true);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() =>
            !RegistryHelper.IsMatch(POWER_KEY, "SleepStudyDisabled", 1)
            && !RegistryHelper.IsMatch(POWER_KEY, "SleepstudyAccountingEnabled", 0)
            && !RegistryHelper.IsMatch(POWER_KEY, "FxAccountingTelemetryDisabled", 1)
            && !RegistryHelper.IsMatch(SESSION_POWER_KEY, "SleepStudyDisabled", 1)
            && !RegistryHelper.IsMatch(WDF_KEY, "WdfGlobalSleepStudyDisabled", 1);

        private static void SetEventChannelEnabled(bool enabled)
        {
            // wevtutil prompts on Analytic channels and rejects set-log with
            // 15000 when there is no console: the Enabled flag is the same
            // switch it flips, so write it directly.
            if (!RegistryHelper.KeyExists(CHANNEL_REG_KEY))
            {
                App.logger.Info($"[SleepStudy] Event channel '{SLEEP_STUDY_CHANNEL}' not present; skipping.");
                return;
            }

            RegistryHelper.SetValue(CHANNEL_REG_KEY, "Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
        }

        private static void SetEfficiencyTaskEnabled(bool enabled)
        {
            if (!EfficiencyTaskExists())
            {
                App.logger.Info($"[SleepStudy] Scheduled task '{POWER_EFFICIENCY_TASK}' not present; skipping.");
                return;
            }

            // Direct spawn, not cmd.exe: the task path contains spaces and
            // cmd's quote-stripping splits it (schtasks sees 'Efficiency').
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "schtasks.exe",
                ["/Change", "/TN", POWER_EFFICIENCY_TASK, enabled ? "/Enable" : "/Disable"]);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"schtasks failed with exit code {result.ExitCode}: {result.CombinedOutput}");
            }
        }

        /// <summary>Exit code is locale-independent; error text is not.</summary>
        private static bool EfficiencyTaskExists()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "schtasks.exe",
                ["/Query", "/TN", POWER_EFFICIENCY_TASK]);
            return result.Succeeded;
        }
    }
}
