#nullable enable

using System;
using System.Reflection;
using SynToolkit.Services.SynergyOsUpdate;

namespace SynToolkit.Services.SynToolkitUpdate
{
    /// <summary>
    /// Installed SynToolkit version = the running assembly version.
    /// </summary>
    public static class SynToolkitInstalledVersion
    {
        public static string? GetInstalledSynToolkitVersion()
        {
            try
            {
                Version? version = Assembly.GetExecutingAssembly().GetName().Version;
                return version is null ? null : SynergyOsSemVer.NormalizeDisplay(version.ToString());
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "SynToolkit installed version could not be read.");
                return null;
            }
        }
    }
}
