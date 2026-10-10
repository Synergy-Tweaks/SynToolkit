using System;
using System.Threading.Tasks;
using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using SynToolkit.ViewModels;

namespace SynToolkit.Commands
{
    public class MultiOptionSaveConfigurationCommand : AsyncCommandBase
    {
        private readonly MultiOptionConfigurationItemViewModel _configurationItemViewModel;
        private readonly MultiOptionConfigurationStore _configurationStore;
        private readonly IMultiOptionConfigurationServices _configurationService;

        public MultiOptionSaveConfigurationCommand(
            MultiOptionConfigurationItemViewModel configurationItemViewModel,
            MultiOptionConfigurationStore configurationStore,
            IMultiOptionConfigurationServices configurationService)
        {
            _configurationItemViewModel = configurationItemViewModel;
            _configurationStore = configurationStore;
            _configurationService = configurationService;
        }

        /// <summary>
        /// Saves the state of a MultiOptionConfigurationService
        /// </summary>
        protected override async Task ExecuteAsync(object parameter)
        {
            int currentSetting = _configurationItemViewModel.Options.IndexOf(_configurationStore.CurrentSetting);

            App.logger.Info($"Changed {_configurationItemViewModel.Key} to option index {currentSetting}");
            _configurationItemViewModel.IsBusy = true;

            try
            {
                if (_configurationService is IPromptingMultiOptionConfigurationService prompting
                    && prompting.IsCustomPromptOption(currentSetting))
                {
                    int? customValue = await prompting.PromptCustomValueAsync();
                    if (customValue is null)
                    {
                        _configurationItemViewModel.ErrorMessage = string.Empty;
                        _configurationItemViewModel.RefreshCurrentSetting();
                        _configurationItemViewModel.ApplyStatusWarning(prompting.GetStatusWarning());
                        return;
                    }

                    await Task.Run(() => prompting.ApplyRawValue(customValue.Value));
                }
                else
                {
                    await Task.Run(() => _configurationService.ChangeStatus(currentSetting));
                }

                _configurationItemViewModel.ErrorMessage = string.Empty;
                _configurationItemViewModel.RefreshCurrentSetting();
                if (_configurationService is IWarnedMultiOptionConfigurationService warnedAfter)
                {
                    _configurationItemViewModel.ApplyStatusWarning(warnedAfter.GetStatusWarning());
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, $"Unable to apply {_configurationItemViewModel.Key} option {currentSetting}.");
                _configurationItemViewModel.ErrorMessage = exception.Message;
                _configurationItemViewModel.RefreshCurrentSetting();
                if (_configurationService is IWarnedMultiOptionConfigurationService warnedError)
                {
                    _configurationItemViewModel.ApplyStatusWarning(warnedError.GetStatusWarning());
                }
            }
            finally
            {
                _configurationItemViewModel.IsBusy = false;
            }
        }
    }
}
