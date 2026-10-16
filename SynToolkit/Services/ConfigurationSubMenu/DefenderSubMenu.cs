using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Services.ConfigurationSubMenu
{
    public class DefenderSubMenu : IConfigurationSubMenu
    {
        private readonly ConfigurationStore _defenderSubMenu;
        public DefenderSubMenu(
            [FromKeyedServices("DefenderSubMenu")] ConfigurationStore defenderSubMenu)
        {
            _defenderSubMenu = defenderSubMenu;
        }
    }
}
