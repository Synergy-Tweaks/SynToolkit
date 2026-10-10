using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class TweaksPrivacySubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStoreSubMenu _store;

        public TweaksPrivacySubMenu(
            [FromKeyedServices("TweaksPrivacySubMenu")] ConfigurationStoreSubMenu store)
        {
            _store = store;
        }
    }
}
