using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Reflects and toggles Microsoft Defender real-time protection via the officially
    /// supported Set-MpPreference cmdlet. Registry-based disabling is blocked by Tamper
    /// Protection on modern Windows, so this shells out to PowerShell exactly like the
    /// existing "Toggle Defender" button (toggleDefender.cmd) does, but exposes real
    /// current-state detection so the Security tab shows an accurate on/off toggle
    /// instead of a stateless button.
    /// </summary>
    internal class DefenderRealtimeProtectionConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _defenderConfigurationStore;

        public DefenderRealtimeProtectionConfigurationService(
            [FromKeyedServices("DefenderRealtimeProtection")] ConfigurationStore defenderConfigurationStore)
        {
            _defenderConfigurationStore = defenderConfigurationStore;
        }

        public void Disable() => SetRealtimeProtection(disabled: true);

        public void Enable() => SetRealtimeProtection(disabled: false);

        public bool IsEnabled()
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "(Get-MpPreference).DisableRealtimeMonitoring"]);

            if (!result.Succeeded || !bool.TryParse(result.StandardOutput.Trim(), out bool disabled))
            {
                throw new InvalidOperationException(
                    $"Unable to read Microsoft Defender real-time protection state. {result.CombinedOutput}".Trim());
            }

            return !disabled;
        }

        private void SetRealtimeProtection(bool disabled)
        {
            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powershell.exe",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $"Set-MpPreference -DisableRealtimeMonitoring ${(disabled ? "true" : "false")}"],
                timeoutMilliseconds: 30_000);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Microsoft Defender rejected the change. Check Tamper Protection and administrator permissions. {result.CombinedOutput}".Trim());
            }

            _defenderConfigurationStore.CurrentSetting = IsEnabled();
        }
    }
}
