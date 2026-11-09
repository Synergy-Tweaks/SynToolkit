using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class LatencyTweaksSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _store;

        public LatencyTweaksSubMenu(
            [FromKeyedServices("LatencyTweaksSubMenu")] ConfigurationStoreSubMenu store)
        {
            _store = store;
        }
    }
}
