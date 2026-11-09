#nullable enable

using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Services.ConfigurationServices;
using System;
using System.Threading.Tasks;

namespace SynToolkit.Commands.ConfigurationButtonsCommand
{
    /// <summary>
    /// Reverts the whole "Latency / Gaming performance" group to Windows defaults, matching
    /// "Revert Latency Tweaks.bat" in the SynergyOS playbook.
    /// </summary>
    public class RevertLatencyTweaksCommand : AsyncCommandBase
    {
        protected override Task ExecuteAsync(object? parameter)
        {
            IServiceProvider services = App._host.Services;

            // Fullscreen optimizations is the existing toggle: Enable() is the Windows default.
            services.GetRequiredKeyedService<IConfigurationService>("FullScreenOptimizations").Enable();
            services.GetRequiredKeyedService<IConfigurationService>("MmcssGamesTask").Disable();
            services.GetRequiredKeyedService<IConfigurationService>("NetworkThrottling").Disable();
            services.GetRequiredKeyedService<IConfigurationService>("PowerThrottling").Disable();
            services.GetRequiredKeyedService<IConfigurationService>("LatencyPowerPlan").Disable();

            App.ReportConfigurationActionSuccess(
                "Latency tweaks reverted to Windows defaults. Sign out or restart so every change applies.");

            return Task.CompletedTask;
        }
    }
}
