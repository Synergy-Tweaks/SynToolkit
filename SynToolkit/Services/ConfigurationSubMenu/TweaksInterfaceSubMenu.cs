using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class TweaksInterfaceSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _store;

        public TweaksInterfaceSubMenu(
            [FromKeyedServices("TweaksInterfaceSubMenu")] ConfigurationStoreSubMenu store)
        {
            _store = store;
        }
    }
}
