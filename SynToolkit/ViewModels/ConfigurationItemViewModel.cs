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

        internal ConfigurationStore Store => _configurationStore;

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
                    : !IsStateKnown
                        ? App.GetValueFromItemList("ConfigurationStatus_Detecting")
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

                _stateVersion++;
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
        private bool _isStateKnown;
        private string _statusMessage = string.Empty;
        private int _stateVersion;

        /// <summary>Bumped whenever the displayed state changes, so a stale probe result can be dropped.</summary>
        public int StateVersion => _stateVersion;

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

        /// <summary>False until the first probe finishes; controls stay disabled while detecting.</summary>
        public bool IsStateKnown
        {
            get => _isStateKnown;
            private set
            {
                if (SetProperty(ref _isStateKnown, value))
                {
                    OnPropertyChanged(nameof(CanInteract));
                    OnPropertyChanged(nameof(DisplayDescription));
                }
            }
        }

        public bool CanInteract => IsStateAvailable && IsStateKnown && !IsBusy;


        public ICommand SaveConfigurationCommand { get; }

        public ConfigurationItemViewModel(
            Configuration configuration,
            ConfigurationStore configurationStore,
            IConfigurationService configurationService)
        {
            _configurationStore = configurationStore;
            _configurationService = configurationService;
            Configuration = configuration;

            // Probing is deferred: the service probes spawn processes and can take
            // seconds. The window shows immediately and MainWindow refreshes states
            // in the background; pages refresh again when they load.
            SaveConfigurationCommand = new SaveConfigurationCommand(this, configurationStore, configurationService);
        }

        public bool FetchCurrentSetting()
        {
            IsBusy = true;

            try
            {
                bool currentSetting = ProbeCurrentSetting();
                ApplyProbedSetting(currentSetting);

                return currentSetting;
            }
            catch (Exception exception)
            {
                ApplyProbeFailure(exception);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Probes the live state without touching UI-bound properties. Thread-safe.
        /// </summary>
        public bool ProbeCurrentSetting() => _configurationService.IsEnabled();

        /// <summary>Applies a probed state. Must run on the UI thread.</summary>
        public void ApplyProbedSetting(bool detectedSetting)
        {
            IsStateAvailable = true;
            IsStateKnown = true;
            ClearStatus();
            SetProperty(ref _currentSetting, detectedSetting, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = detectedSetting;
            ApplyDetectionStatus();
            _stateVersion++;
        }

        /// <summary>
        /// Applies a probe result only if the state has not changed since the probe
        /// started; a click that lands mid-probe must win. Must run on the UI thread.
        /// </summary>
        public bool TryApplyProbedSetting(bool detectedSetting, int expectedVersion)
        {
            if (_stateVersion != expectedVersion)
            {
                return false;
            }

            ApplyProbedSetting(detectedSetting);
            return true;
        }

        /// <summary>Applies a probe failure. Must run on the UI thread.</summary>
        public void ApplyProbeFailure(Exception exception)
        {
            IsStateAvailable = false;
            IsStateKnown = true;
            SetStatus(
                App.GetValueFromItemList("ConfigurationStatus_Unavailable"),
                exception);
            App.logger.Warn(exception, $"Unable to detect current state for {Key}.");
            SetProperty(ref _currentSetting, false, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = false;
            _stateVersion++;
        }

        /// <summary>Applies a probe failure unless the state changed since the probe started.</summary>
        public bool TryApplyProbeFailure(Exception exception, int expectedVersion)
        {
            if (_stateVersion != expectedVersion)
            {
                return false;
            }

            ApplyProbeFailure(exception);
            return true;
        }

        public void RefreshCurrentSetting()
        {
            bool detectedSetting = FetchCurrentSetting();
            SetProperty(ref _currentSetting, detectedSetting, nameof(CurrentSetting));
            _configurationStore.CurrentSetting = detectedSetting;
        }

        /// <summary>
        /// Probes off the UI thread after a change, then applies the result on the UI
        /// thread. Probing some services spawns processes, which would freeze the UI.
        /// </summary>
        public async Task RefreshCurrentSettingAsync()
        {
            int version = _stateVersion;
            bool detectedSetting;
            try
            {
                detectedSetting = await Task.Run(ProbeCurrentSetting);
            }
            catch (Exception exception)
            {
                TryApplyProbeFailure(exception, version);
                return;
            }

            TryApplyProbedSetting(detectedSetting, version);
        }

        private void ApplyDetectionStatus()
        {
            if (_configurationService is HagsConfigurationService hags
                && hags.GetDetectionStatus() is string hagsStatus
                && !string.IsNullOrWhiteSpace(hagsStatus))
            {
                _statusMessage = hagsStatus;
                OnPropertyChanged(nameof(DisplayDescription));
            }
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
