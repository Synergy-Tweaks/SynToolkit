using SynToolkit.Enums;
using SynToolkit.Models;
using SynToolkit.Services;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Services.ConfigurationSubMenu;
using SynToolkit.Stores;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;

namespace SynToolkit.ViewModels
{
    public class ConfigurationSubMenuViewModel : IConfigurationItem
    {
        private readonly ConfigurationStoreSubMenu _configurationStoreSubMenu;

        public ObservableCollection<ConfigurationItemViewModel> ConfigurationItems { get; set; }
        public ObservableCollection<MultiOptionConfigurationItemViewModel> MultiOptionConfigurationItems { get; set; }
        public ObservableCollection<LinksViewModel> LinksViewModels { get; set; }
        public ObservableCollection<ConfigurationSubMenuViewModel> ConfigurationSubMenuViewModels { get; set; }
        public ObservableCollection<ConfigurationButtonViewModel> ConfigurationButtonViewModels { get; set; }

        public ConfigurationSubMenu _configurationSubMenu { get; set; }
        public string Name => _configurationSubMenu.Name;
        public string Description => _configurationSubMenu.Description;
        public string DisplayDescription
        {
            get
            {
                if (CanOpen)
                {
                    return Description;
                }

                string unavailable = App.GetValueFromItemList("AmdGpuRequired");
                return string.IsNullOrWhiteSpace(Description)
                    ? unavailable
                    : $"{Description}{Environment.NewLine}{unavailable}";
            }
        }
        public ConfigurationType Type => _configurationSubMenu.Type;
        public string Icon => _configurationSubMenu.Icon;

        public string Key => _configurationSubMenu.Key;

        /// <summary>
        /// AMD GPU submenu stays visible but non-interactive without an AMD adapter.
        /// </summary>
        public bool CanOpen =>
            !string.Equals(Key, "AmdGpuTweaksSubMenu", StringComparison.Ordinal)
            || GpuDetectionService.HasAmdGpu();

        public ConfigurationSubMenuViewModel() { }

        public ConfigurationSubMenuViewModel(
            ConfigurationSubMenu configurationSubMenu,
            ConfigurationStoreSubMenu configurationStoreSubMenu,
            ObservableCollection<ConfigurationItemViewModel> configurationItems,
            ObservableCollection<MultiOptionConfigurationItemViewModel> multiOptionConfigurationItems,
            ObservableCollection<LinksViewModel> linksViewModels,
            ObservableCollection<ConfigurationSubMenuViewModel> configurationSubMenuViewModels,
            ObservableCollection<ConfigurationButtonViewModel> configurationButtonViewModels)
        {
            _configurationSubMenu = configurationSubMenu;
            _configurationStoreSubMenu = configurationStoreSubMenu;
            ConfigurationItems = configurationItems;
            MultiOptionConfigurationItems = multiOptionConfigurationItems;
            LinksViewModels = linksViewModels;
            ConfigurationSubMenuViewModels = configurationSubMenuViewModels;
            ConfigurationButtonViewModels = configurationButtonViewModels;
        }
    }
}
