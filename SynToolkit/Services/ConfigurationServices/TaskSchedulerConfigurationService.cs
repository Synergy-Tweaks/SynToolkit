using Microsoft.Win32;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Task Scheduler toggle. Schedule is OS-critical (Update, Defender scans,
    /// restores all stop when it is Disabled), so Disable snapshots the original
    /// startup state and Enable restores it instead of inventing Automatic.
    /// </summary>
    public class TaskSchedulerConfigurationService : IConfigurationService
    {
        private const string SCHEDULE_SERVICE_NAME = "Schedule";
        private const string SYNTOOLKIT_STORE_KEY = @"HKLM\SOFTWARE\SynToolkit\Services\TaskScheduler";

        private readonly ConfigurationStore _taskSchedulerConfigurationStore;

        public TaskSchedulerConfigurationService(
            [FromKeyedServices("TaskScheduler")] ConfigurationStore taskSchedulerConfigurationStore)
        {
            _taskSchedulerConfigurationStore = taskSchedulerConfigurationStore;
        }

        public void Disable()
        {
            CaptureOriginalState();
            // Stop first: setting Disabled while running leaves it
            // Disabled-but-running until reboot, which the UI can't show.
            ServiceHelper.StopService(SCHEDULE_SERVICE_NAME, TimeSpan.FromSeconds(15));
            ServiceHelper.SetStartupType(SCHEDULE_SERVICE_NAME, ServiceStartMode.Disabled);

            _taskSchedulerConfigurationStore.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            ServiceSnapshot snapshot = ReadSnapshotOrDefault();
            ServiceHelper.SetStartupType(SCHEDULE_SERVICE_NAME, snapshot.StartMode);
            if (snapshot.StartMode == ServiceStartMode.Automatic)
            {
                ServiceHelper.SetDelayedAutoStart(SCHEDULE_SERVICE_NAME, snapshot.DelayedAutoStart);
            }

            if (snapshot.WasRunning && snapshot.StartMode != ServiceStartMode.Disabled)
            {
                ServiceHelper.StartService(SCHEDULE_SERVICE_NAME, TimeSpan.FromSeconds(15));
            }

            ClearSnapshot();
            _taskSchedulerConfigurationStore.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return ServiceHelper.TryGetStartupType(SCHEDULE_SERVICE_NAME, out ServiceStartMode startMode) &&
                startMode != ServiceStartMode.Disabled;
        }

        private static void CaptureOriginalState()
        {
            if (RegistryHelper.IsMatch(SYNTOOLKIT_STORE_KEY, "SnapshotPresent", 1))
            {
                return;
            }

            ServiceStartMode startMode = ServiceHelper.GetStartupType(SCHEDULE_SERVICE_NAME);
            bool delayedAutoStart = startMode == ServiceStartMode.Automatic &&
                ServiceHelper.GetDelayedAutoStart(SCHEDULE_SERVICE_NAME);
            bool wasRunning = ServiceHelper.TryGetStatus(SCHEDULE_SERVICE_NAME, out ServiceControllerStatus status) &&
                status == ServiceControllerStatus.Running;

            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY, "StartMode", (int)startMode, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY, "WasRunning", wasRunning ? 1 : 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY, "DelayedAutoStart", delayedAutoStart ? 1 : 0, RegistryValueKind.DWord);
            RegistryHelper.SetValue(SYNTOOLKIT_STORE_KEY, "SnapshotPresent", 1, RegistryValueKind.DWord);
        }

        private static ServiceSnapshot ReadSnapshotOrDefault()
        {
            if (!RegistryHelper.IsMatch(SYNTOOLKIT_STORE_KEY, "SnapshotPresent", 1))
            {
                // No snapshot (never disabled via the app, or store wiped):
                // refuse to invent Automatic for an OS-critical service.
                throw new InvalidOperationException(
                    "No saved Task Scheduler state exists, so SynToolkit will not guess its startup type. Disable then re-enable through the app to record one.");
            }

            object storedMode = RegistryHelper.GetValue(SYNTOOLKIT_STORE_KEY, "StartMode");
            if (storedMode is not int modeValue || !Enum.IsDefined(typeof(ServiceStartMode), modeValue))
            {
                throw new InvalidOperationException("The saved Task Scheduler state is invalid. Nothing was changed.");
            }

            return new ServiceSnapshot(
                (ServiceStartMode)modeValue,
                RegistryHelper.IsMatch(SYNTOOLKIT_STORE_KEY, "WasRunning", 1),
                (ServiceStartMode)modeValue == ServiceStartMode.Automatic &&
                    RegistryHelper.IsMatch(SYNTOOLKIT_STORE_KEY, "DelayedAutoStart", 1));
        }

        private static void ClearSnapshot()
        {
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY, "SnapshotPresent");
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY, "StartMode");
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY, "WasRunning");
            RegistryHelper.DeleteValue(SYNTOOLKIT_STORE_KEY, "DelayedAutoStart");
        }

        private sealed record ServiceSnapshot(
            ServiceStartMode StartMode,
            bool WasRunning,
            bool DelayedAutoStart);
    }
}
