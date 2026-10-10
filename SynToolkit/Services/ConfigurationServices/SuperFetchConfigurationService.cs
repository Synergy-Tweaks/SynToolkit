using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.ServiceProcess;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// SynergyOS disables SysMain (SuperFetch) for steadier latency.
    /// </summary>
    internal class SuperFetchConfigurationService : IConfigurationService
    {
        private const string SERVICE_NAME = "SysMain";
        private readonly ConfigurationStore _store;

        public SuperFetchConfigurationService(
            [FromKeyedServices("SuperFetch")] ConfigurationStore store)
        {
            _store = store;
        }

        public void Disable()
        {
            ServiceHelper.StopService(SERVICE_NAME);
            ServiceHelper.SetStartupType(SERVICE_NAME, ServiceStartMode.Disabled);
            _store.CurrentSetting = IsEnabled();
        }

        public void Enable()
        {
            ServiceHelper.SetStartupType(SERVICE_NAME, ServiceStartMode.Automatic);
            ServiceHelper.StartService(SERVICE_NAME);
            _store.CurrentSetting = IsEnabled();
        }

        public bool IsEnabled()
        {
            return ServiceHelper.TryGetStartupType(SERVICE_NAME, out ServiceStartMode startupType)
                && startupType != ServiceStartMode.Disabled;
        }
    }
}
