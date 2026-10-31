using SynToolkit.Models;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using System.Threading.Tasks;
using System;

namespace SynToolkit.Commands
{
    public class SaveConfigurationCommand : AsyncCommandBase
    {
        private readonly ConfigurationItemViewModel _configurationItemViewModel;
        private readonly ConfigurationStore _configurationStore;
        private readonly IConfigurationService _configurationService;

        public SaveConfigurationCommand(
            ConfigurationItemViewModel configurationItemViewModel,
            ConfigurationStore configurationStore,
            IConfigurationService configurationService)
        {
            _configurationItemViewModel = configurationItemViewModel;
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }

        /// <summary>
        /// Saves the current state of a ConfigurationService
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(object parameter)
        {
            bool currentSetting = _configurationStore.CurrentSetting;

            App.logger.Info($"Toggled {_configurationItemViewModel.Key} to {currentSetting}");
            _configurationItemViewModel.ClearStatus();
            _configurationItemViewModel.IsBusy = true;

            try
            {
                await Task.Run(currentSetting
                    ? _configurationService.Enable
                    : _configurationService.Disable);

                await _configurationItemViewModel.RefreshCurrentSettingAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, $"Unable to apply {_configurationItemViewModel.Key}.");
                await _configurationItemViewModel.RefreshCurrentSettingAsync();
                _configurationItemViewModel.ShowApplyFailure(exception);
            }
            finally
            {
                _configurationItemViewModel.IsBusy = false;
            }
        }
    }
}
