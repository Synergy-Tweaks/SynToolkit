using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Models;
using SynToolkit.Stores;
using System.Windows.Input;
using SynToolkit.Commands;
using SynToolkit.Enums;
using Windows.UI;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace SynToolkit.ViewModels
{
    public class MultiOptionConfigurationItemViewModel : ObservableObject, IConfigurationItem
    {
        private readonly MultiOptionConfigurationStore _configurationStore;
        private readonly IMultiOptionConfigurationServices _configurationService;

        public MultiOptionConfiguration Configuration { get; set; }
        public string Name => Configuration.Name;
        public ConfigurationType Type => Configuration.Type;
        public string Icon => Configuration.Icon;

        public List<string> Options => _configurationStore.Options; 
        public string Key => Configuration.Key;

        public string Description =>
            App.GetValueFromItemList(Key, desc: true);

        public string DisplayDescription
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_errorMessage))
                {
                    return Description;
                }

                return string.IsNullOrWhiteSpace(Description)
                    ? _errorMessage
                    : $"{Description}{Environment.NewLine}{_errorMessage}";
            }
        }

        public Color Color { get; set; }


        private string _currentSetting;

        public string CurrentSetting
        {
            get => _currentSetting;
            set
            {
                if (!SetProperty(ref _currentSetting, value))
                {
                    return;
                }

                _configurationStore.CurrentSetting = CurrentSetting;
                this.MultiOptionSaveConfigurationCommand.Execute(this);
            }
        }

        private string _errorMessage = string.Empty;

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(DisplayDescription));
                }
            }
        }

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        private bool _isStateAvailable = true;

        public bool IsStateAvailable
        {
            get => _isStateAvailable;
            private set
            {
                if (SetProperty(ref _isStateAvailable, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                }
            }
        }

        public bool CanInteract => IsStateAvailable && !IsBusy;

        public ICommand MultiOptionSaveConfigurationCommand { get; }

        public MultiOptionConfigurationItemViewModel(
            MultiOptionConfiguration configuration,
            MultiOptionConfigurationStore configurationStore,
            IMultiOptionConfigurationServices configurationService)
        {
            Configuration = configuration;

            _configurationStore = configurationStore;
            _configurationService = configurationService;

            // Probing is deferred; see ConfigurationItemViewModel.
            MultiOptionSaveConfigurationCommand = new MultiOptionSaveConfigurationCommand(this, configurationStore, configurationService);
        }

        public string FetchCurrentSetting()
        {
            IsBusy = true;

            try
            {
                string currentSetting = ProbeCurrentSetting();
                ApplyProbedSetting(currentSetting);
                return currentSetting;
            }
            catch (Exception exception)
            {
                ApplyProbeFailure(exception);
                return "State unavailable";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Probes the live state without touching UI-bound properties. Thread-safe.
        /// </summary>
        public string ProbeCurrentSetting() => _configurationService.Status();

        /// <summary>Applies a probed state. Must run on the UI thread.</summary>
        public void ApplyProbedSetting(string detectedSetting)
        {
            IsStateAvailable = true;
            SetProperty(ref _currentSetting, detectedSetting, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = detectedSetting;
            if (_configurationService is IWarnedMultiOptionConfigurationService warned)
            {
                ApplyStatusWarning(warned.GetStatusWarning());
            }
        }

        /// <summary>Applies a probe failure. Must run on the UI thread.</summary>
        public void ApplyProbeFailure(Exception exception)
        {
            const string unavailableState = "State unavailable";
            if (!_configurationStore.Options.Contains(unavailableState))
            {
                _configurationStore.Options.Add(unavailableState);
            }

            IsStateAvailable = false;
            App.logger.Warn(exception, $"Unable to detect current state for {Key}.");
            SetProperty(ref _currentSetting, unavailableState, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = unavailableState;
        }

        public void RefreshCurrentSetting()
        {
            string detectedSetting = FetchCurrentSetting();
            SetProperty(ref _currentSetting, detectedSetting, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = detectedSetting;
            if (_configurationService is IWarnedMultiOptionConfigurationService warned)
            {
                ApplyStatusWarning(warned.GetStatusWarning());
            }
        }

        /// <summary>
        /// Probes off the UI thread after a change, then applies the result on the UI
        /// thread. Probing some services spawns processes, which would freeze the UI.
        /// </summary>
        public async System.Threading.Tasks.Task RefreshCurrentSettingAsync()
        {
            string detectedSetting;
            try
            {
                detectedSetting = await System.Threading.Tasks.Task.Run(ProbeCurrentSetting);
            }
            catch (Exception exception)
            {
                ApplyProbeFailure(exception);
                return;
            }

            ApplyProbedSetting(detectedSetting);
        }

        public void ApplyStatusWarning(string warning)
        {
            ErrorMessage = string.IsNullOrWhiteSpace(warning) ? string.Empty : warning;
        }
    }
}
