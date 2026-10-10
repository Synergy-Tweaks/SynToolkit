using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class TweaksNetworkPowerSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _store;

        public TweaksNetworkPowerSubMenu(
            [FromKeyedServices("TweaksNetworkPowerSubMenu")] ConfigurationStoreSubMenu store)
        {
            _store = store;
        }
    }
}
