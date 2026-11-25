using Microsoft.Extensions.DependencyInjection;
using SynToolkit.Stores;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class NicTweakerSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _store;

        public NicTweakerSubMenu(
            [FromKeyedServices("NicTweakerSubMenu")] ConfigurationStoreSubMenu store)
        {
            _store = store;
        }
    }
}
