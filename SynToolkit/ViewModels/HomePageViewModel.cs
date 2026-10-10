#nullable enable

using ABI.System.Collections;
using SynToolkit.Models;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Utils;
using SynToolkit.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Foundation.Collections;
using Windows.Services.Maps;

namespace SynToolkit.ViewModels
{
    public partial class HomePageViewModel : ObservableObject
    {
        private IEnumerable<ConfigurationItemViewModel> ConfigurationItemViewModels { get; }
        private IEnumerable<MultiOptionConfigurationItemViewModel> MultiOptionConfigurationItemViewModels { get; }

        [ObservableProperty]
        public partial ObservableCollection<Profiles> ProfilesList { get; set; } = new();

        [ObservableProperty]
        public partial string Name { get; set; } = string.Empty;

        [ObservableProperty]
        public partial Profiles? ProfileSelected { get; set; }

        public HomePageViewModel(
            IEnumerable<Profiles> profiles,
            IEnumerable<ConfigurationItemViewModel> configurationItemViewModels,
            IEnumerable<MultiOptionConfigurationItemViewModel> multiOptionConfigurationItemViewModels)
        {
            ConfigurationItemViewModels = configurationItemViewModels;
            MultiOptionConfigurationItemViewModels = multiOptionConfigurationItemViewModels;
            foreach (Profiles profile in profiles)
            {
                ProfilesList.Add(profile);
            }

            ProfileSelected = ProfilesList.FirstOrDefault();
        }

        public static HomePageViewModel LoadViewModel(
            IEnumerable<Profiles> profiles,
            IEnumerable<ConfigurationItemViewModel> configurationItemViewModels,
            IEnumerable<MultiOptionConfigurationItemViewModel> multiOptionConfigurationItemViewModels)
        {
            HomePageViewModel viewModel = new(profiles, configurationItemViewModels, multiOptionConfigurationItemViewModels);

            return viewModel;
        }

        [RelayCommand]
        private void AddProfile()
        {
            string profileName = Name.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return;
            }

            try
            {
                Profiles profile = ProfileSerializing.CreateProfile(profileName);
                ProfilesList.Add(profile);
                ProfileSelected = profile;
                Name = string.Empty;
            }
            catch (Exception ex)
            {
                App.logger.Error($"Unable to create profile '{profileName}': {ex.Message}");
            }
        }

        [RelayCommand]
        private void RemoveProfile() 
        {
            Profiles? selectedProfile = ProfileSelected;
            if (selectedProfile is null)
            {
                return;
            }

            DirectoryInfo profilesDirectory = new DirectoryInfo($"{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)}\\Synergy\\Profiles\\");
            FileInfo[] profileFile = profilesDirectory.Exists ? profilesDirectory.GetFiles() : Array.Empty<FileInfo>();

            foreach (FileInfo file in profileFile.ToList())
            {
                if (selectedProfile.Key + ".json" == file.Name) File.Delete(file.FullName);
            }
            ProfilesList.Remove(selectedProfile);
            ProfileSelected = ProfilesList.FirstOrDefault();
        }

        [RelayCommand]
        private void SetProfile()
        {
            Profiles? selectedProfile = ProfileSelected;
            if (selectedProfile is null)
            {
                App.logger.Warn("Tried to set a profile whilst nothing was selected");
                return;
            }

            // need more research to figure out a better way to do this
            List<ConfigurationItemViewModel> configurationItemVMs = ConfigurationItemViewModels.ToList();
            List<MultiOptionConfigurationItemViewModel> multiConfigurationItemVMs = MultiOptionConfigurationItemViewModels.ToList();
            foreach (ConfigurationItemViewModel viewModel in configurationItemVMs)
            {
                try
                {
                    if (selectedProfile.ConfigurationServices.Contains(viewModel.Key))
                    {
                        //ConfigurationItemViewModel config = App._host.Services.GetKeyedService<ConfigurationItemViewModel>(viewModel.Key);
                        viewModel.CurrentSetting = true;
                    }
                    else if (viewModel.CurrentSetting == true)
                    {
                        viewModel.CurrentSetting = false;
                    }
                }
                catch
                {
                    App.logger.Warn("Tried to set a profile whilst nothing was selected");
                    break;
                }
            }
            foreach (KeyValuePair<string, string> keyPair in selectedProfile.MultiOptionConfigServices)
            {
                foreach (MultiOptionConfigurationItemViewModel vm in multiConfigurationItemVMs)
                {
                    if (vm.Key == keyPair.Key && vm.CurrentSetting != keyPair.Value)
                    {
                        vm.CurrentSetting = keyPair.Value;
                    }
                }
            }

            // Legacy profiles stored MMCSS as a boolean toggle in ConfigurationServices.
            // Map ON → Optimized, OFF → Default when MultiConfig has no MMCSS entry.
            bool mmcssInMultiConfig = selectedProfile.MultiOptionConfigServices
                .Any(pair => string.Equals(pair.Key, "MmcssOptimization", StringComparison.Ordinal));
            if (!mmcssInMultiConfig)
            {
                MultiOptionConfigurationItemViewModel? mmcssVm = multiConfigurationItemVMs
                    .FirstOrDefault(vm => string.Equals(vm.Key, "MmcssOptimization", StringComparison.Ordinal));
                if (mmcssVm is not null)
                {
                    bool wasEnabled = selectedProfile.ConfigurationServices
                        .Contains("MmcssOptimization", StringComparer.Ordinal);
                    string migrated = wasEnabled ? "Optimized" : "Default";
                    if (!string.Equals(mmcssVm.CurrentSetting, migrated, StringComparison.Ordinal))
                    {
                        mmcssVm.CurrentSetting = migrated;
                    }
                }
            }

            App.ContentDialogCaller("restart");
        }
    }
}
