using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class TweaksPerformanceSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _store;

        public TweaksPerformanceSubMenu(
            [FromKeyedServices("TweaksPerformanceSubMenu")] ConfigurationStoreSubMenu store)
        {
            _store = store;
        }
    }
}
