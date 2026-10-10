using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Models;
using SynToolkit.Stores;
using System.Windows.Input;
using SynToolkit.Commands;
using SynToolkit.Enums;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
//using System.Drawing;

namespace SynToolkit.ViewModels
{
    public class ConfigurationItemViewModel : ObservableObject, IConfigurationItem
    {
        private readonly ConfigurationStore _configurationStore;
        private readonly IConfigurationService _configurationService;

        public Configuration Configuration { get; set; }
        public string Name => Configuration.Name;
        public string Key => Configuration.Key;
        public string Description => Configuration.Description;
        public string DisplayDescription
        {
            get
            {
                string status = !string.IsNullOrWhiteSpace(_statusMessage)
                    ? _statusMessage
                    : IsBusy
                        ? App.GetValueFromItemList("ConfigurationStatus_Applying")
                        : string.Empty;

                if (string.IsNullOrWhiteSpace(status))
                {
                    return Description;
                }

                return string.IsNullOrWhiteSpace(Description)
                    ? status
                    : $"{Description}{Environment.NewLine}{status}";
            }
        }
        public ConfigurationType Type => Configuration.Type;
        public string Icon => Configuration.Icon;

        private bool _currentSetting;

        public bool CurrentSetting
        {
            get => _currentSetting;
            set
            {
                if (!SetProperty(ref _currentSetting, value))
                {
                    return;
                }

                ClearStatus();
                _configurationStore.CurrentSetting = CurrentSetting;
                this.SaveConfigurationCommand.Execute(this);
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
                    OnPropertyChanged(nameof(DisplayDescription));
                }
            }
        }

        private bool _isStateAvailable = true;
        private string _statusMessage = string.Empty;

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


        public ICommand SaveConfigurationCommand { get; }

        public ConfigurationItemViewModel(
            Configuration configuration,
            ConfigurationStore configurationStore,
            IConfigurationService configurationService)
        {
            _configurationStore = configurationStore;
            _configurationService = configurationService;
            Configuration = configuration;

            _currentSetting = FetchCurrentSetting();
            SaveConfigurationCommand = new SaveConfigurationCommand(this, configurationStore, configurationService);
            
        }

        public bool FetchCurrentSetting()
        {
            IsBusy = true;

            try
            {
                bool currentSetting = _configurationService.IsEnabled();
                _configurationStore.CurrentSetting = currentSetting;
                IsStateAvailable = true;

                if (_configurationService is HagsConfigurationService hags
                    && hags.GetDetectionStatus() is string hagsStatus
                    && !string.IsNullOrWhiteSpace(hagsStatus))
                {
                    _statusMessage = hagsStatus;
                    OnPropertyChanged(nameof(DisplayDescription));
                }
                else
                {
                    ClearStatus();
                }

                return currentSetting;
            }
            catch (Exception exception)
            {
                IsStateAvailable = false;
                SetStatus(
                    App.GetValueFromItemList("ConfigurationStatus_Unavailable"),
                    exception);
                App.logger.Warn(exception, $"Unable to detect current state for {Key}.");
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void RefreshCurrentSetting()
        {
            bool detectedSetting = FetchCurrentSetting();
            SetProperty(ref _currentSetting, detectedSetting, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = detectedSetting;
        }

        public void ClearStatus()
        {
            if (string.IsNullOrEmpty(_statusMessage))
            {
                return;
            }

            _statusMessage = string.Empty;
            OnPropertyChanged(nameof(DisplayDescription));
        }

        public void ShowApplyFailure(Exception exception)
        {
            SetStatus(
                App.GetValueFromItemList("ConfigurationStatus_ApplyFailed"),
                exception);
        }

        private void SetStatus(string prefix, Exception exception)
        {
            string detail = exception?.Message ?? string.Empty;
            detail = string.Join(" ", detail
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (detail.Length > 180)
            {
                detail = detail[..177] + "...";
            }

            _statusMessage = string.IsNullOrWhiteSpace(detail)
                ? prefix
                : $"{prefix}: {detail}";
            OnPropertyChanged(nameof(DisplayDescription));
        }
    }
}
