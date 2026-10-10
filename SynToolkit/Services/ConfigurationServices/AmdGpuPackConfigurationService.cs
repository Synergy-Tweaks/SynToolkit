#nullable enable

using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Shared AMD DWORD-pack toggle. Enable = apply pack; Disable = revert (delete values).
    /// Greys out when no AMD GPU is detected.
    /// </summary>
    internal abstract class AmdGpuPackConfigurationService : IConfigurationService
    {
        private readonly ConfigurationStore _store;
        private readonly IReadOnlyList<AmdRegistryValue> _values;
        private readonly bool _requiresRestart;

        protected AmdGpuPackConfigurationService(
            ConfigurationStore store,
            IReadOnlyList<AmdRegistryValue> values,
            bool requiresRestart = true)
        {
            _store = store;
            _values = values;
            _requiresRestart = requiresRestart;
        }

        public void Disable()
        {
            AmdGpuRegistryHelper.RevertValues(_values);
            if (_requiresRestart)
            {
                App.ContentDialogCaller("restart");
            }

            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            AmdGpuRegistryHelper.ApplyValues(_values);
            if (_requiresRestart)
            {
                App.ContentDialogCaller("restart");
            }

            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled() => AmdGpuRegistryHelper.AreValuesApplied(_values);
    }

    internal sealed class AmdDriverTelemetryConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdDriverTelemetryConfigurationService(
            [FromKeyedServices("AmdDriverTelemetry")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.DriverTelemetry)
        {
        }
    }

    internal sealed class AmdFeatureLatencyConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdFeatureLatencyConfigurationService(
            [FromKeyedServices("AmdFeatureLatency")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.FeatureLatency)
        {
        }
    }

    internal sealed class AmdPowerPlayConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdPowerPlayConfigurationService(
            [FromKeyedServices("AmdPowerPlay")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.PowerPlay)
        {
        }
    }

    internal sealed class AmdUlpsConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdUlpsConfigurationService(
            [FromKeyedServices("AmdUlps")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.Ulps)
        {
        }
    }

    internal sealed class AmdAspmConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdAspmConfigurationService(
            [FromKeyedServices("AmdAspm")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.Aspm)
        {
        }
    }

    internal sealed class AmdClockGatingConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdClockGatingConfigurationService(
            [FromKeyedServices("AmdClockGating")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.ClockGating)
        {
        }
    }

    internal sealed class AmdPowerGatingConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdPowerGatingConfigurationService(
            [FromKeyedServices("AmdPowerGating")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.PowerGating)
        {
        }
    }

    internal sealed class AmdDisplayPowerConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdDisplayPowerConfigurationService(
            [FromKeyedServices("AmdDisplayPower")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.DisplayPower)
        {
        }
    }

    internal sealed class AmdSpreadSpectrumConfigurationService : AmdGpuPackConfigurationService
    {
        public AmdSpreadSpectrumConfigurationService(
            [FromKeyedServices("AmdSpreadSpectrum")] ConfigurationStore store)
            : base(store, AmdGpuTweaksPacks.SpreadSpectrum)
        {
        }
    }

    /// <summary>
    /// Disables AMD Crash Defender / logging services (Start=4). Re-enable restores Start=2.
    /// </summary>
    internal sealed class AmdCrashDefenderConfigurationService : IConfigurationService
    {
        private static readonly string[] ServiceNames =
        {
            "AMD Crash Defender Service",
            "amdfendr",
            "amdfendrmgr",
            "amdlog",
        };

        private readonly ConfigurationStore _store;

        public AmdCrashDefenderConfigurationService(
            [FromKeyedServices("AmdCrashDefender")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            AmdGpuRegistryHelper.EnsureAmdGpuPresent();
            foreach (string name in ServiceNames)
            {
                try
                {
                    if (!ServiceHelper.TryGetStartupType(name, out _))
                    {
                        continue;
                    }

                    ServiceHelper.SetStartupType(name, ServiceStartMode.Automatic);
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[AMD] Could not restore service {0}.", name);
                }
            }

            App.ContentDialogCaller("restart");
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            AmdGpuRegistryHelper.EnsureAmdGpuPresent();
            foreach (string name in ServiceNames)
            {
                try
                {
                    if (!ServiceHelper.TryGetStartupType(name, out _))
                    {
                        continue;
                    }

                    ServiceHelper.StopService(name);
                    ServiceHelper.SetStartupType(name, ServiceStartMode.Disabled);
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[AMD] Could not disable service {0}.", name);
                }
            }

            App.ContentDialogCaller("restart");
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            if (!GpuDetectionService.HasAmdGpu() || AmdGpuRegistryHelper.FindAmdAdapterKeyPath() is null)
            {
                return false;
            }

            foreach (string name in ServiceNames)
            {
                if (ServiceHelper.TryGetStartupType(name, out ServiceStartMode startupType)
                    && startupType == ServiceStartMode.Disabled)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
