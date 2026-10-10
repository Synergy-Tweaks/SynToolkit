using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS privacy/telemetry reduction (policies + services). Does not remove AppX packages.
    /// Toggle on = reductions applied.
    /// </summary>
    internal class TelemetryOptimizationsConfigurationService : IConfigurationService
    {
        private const string DATA_COLLECTION = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection";
        private const string ADVERTISING = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo";
        private const string ACTIVITY = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";
        private const string SQM = @"HKLM\SOFTWARE\Policies\Microsoft\SQMClient\Windows";
        private const string STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\TelemetryOptimizations";

        private readonly ConfigurationStore _store;

        public TelemetryOptimizationsConfigurationService(
            [FromKeyedServices("TelemetryOptimizations")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            RegistryHelper.DeleteValue(DATA_COLLECTION, "AllowTelemetry");
            RegistryHelper.DeleteValue(DATA_COLLECTION, "DoNotShowFeedbackNotifications");
            RegistryHelper.DeleteValue(DATA_COLLECTION, "LimitDiagnosticLogCollection");
            RegistryHelper.DeleteValue(ADVERTISING, "DisabledByGroupPolicy");
            RegistryHelper.DeleteValue(ACTIVITY, "EnableActivityFeed");
            RegistryHelper.DeleteValue(ACTIVITY, "PublishUserActivities");
            RegistryHelper.DeleteValue(ACTIVITY, "UploadUserActivities");
            RegistryHelper.DeleteValue(SQM, "CEIPEnable");
            try
            {
                ServiceHelper.SetStartupType("DiagTrack", ServiceStartMode.Automatic);
            }
            catch
            {
                // Service may be absent on hardened images.
            }
            RegistryHelper.SetValue(STORE_KEY, "state", 0, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            RegistryHelper.SetValue(DATA_COLLECTION, "AllowTelemetry", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DATA_COLLECTION, "DoNotShowFeedbackNotifications", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(DATA_COLLECTION, "LimitDiagnosticLogCollection", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(ADVERTISING, "DisabledByGroupPolicy", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(ACTIVITY, "EnableActivityFeed", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(ACTIVITY, "PublishUserActivities", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(ACTIVITY, "UploadUserActivities", 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SQM, "CEIPEnable", 0, RegistryValueKind.DWord);
            try
            {
                ServiceHelper.StopService("DiagTrack");
                ServiceHelper.SetStartupType("DiagTrack", ServiceStartMode.Disabled);
            }
            catch
            {
                // Service may be absent on hardened images.
            }
            RegistryHelper.SetValue(STORE_KEY, "state", 1, RegistryValueKind.DWord);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() =>
            RegistryHelper.IsMatch(DATA_COLLECTION, "AllowTelemetry", 0)
            && RegistryHelper.IsMatch(STORE_KEY, "state", 1);
    }
}
