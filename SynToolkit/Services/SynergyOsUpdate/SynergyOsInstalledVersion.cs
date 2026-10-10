#nullable enable

using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Models;
using SynToolkit.Utils;
using System;

namespace SynToolkit.Services.SynergyOsUpdate
{
    /// <summary>
    /// Single entry point for reading the installed SynergyOS playbook version.
    /// Source: AME/OEM playbook metadata via <see cref="ISystemInformationService"/>,
    /// gated by <see cref="CompatibilityHelper.IsSynergyOsCompatible"/> OEM markers
    /// (Manufacturer=Kwanteks, Model=SOS 11|SYNERGYOS, SupportURL=dsc.gg/kwanteks).
    /// </summary>
    public static class SynergyOsInstalledVersion
    {
        public static string? GetInstalledSynergyOSVersion()
        {
            try
            {
                if (!CompatibilityHelper.IsSynergyOsCompatible())
                {
                    App.logger.Debug("SynergyOS update check skipped: SynergyOS OEM markers not detected.");
                    return null;
                }

                if (App._host?.Services is null)
                {
                    App.logger.Debug("SynergyOS update check skipped: host services unavailable.");
                    return null;
                }

                ISystemInformationService detector =
                    App._host.Services.GetRequiredService<ISystemInformationService>();
                PlaybookInformation playbook = detector.Detect().Playbook;
                return ResolveFromPlaybook(playbook);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "SynergyOS installed version could not be read.");
                return null;
            }
        }

        /// <summary>
        /// Testable resolver used by unit tests and <see cref="GetInstalledSynergyOSVersion"/>.
        /// </summary>
        public static string? ResolveFromPlaybook(PlaybookInformation playbook)
        {
            if (playbook.Status != PlaybookDetectionStatus.Detected)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(playbook.Version))
            {
                return null;
            }

            return SynergyOsSemVer.NormalizeDisplay(playbook.Version);
        }
    }
}
