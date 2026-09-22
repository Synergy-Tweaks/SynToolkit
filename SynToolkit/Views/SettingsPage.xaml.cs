using SynToolkit.Models;
using SynToolkit.Services;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using CommunityToolkit.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.LayoutRenderers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Appointments;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SynToolkit.Views
{
    public sealed partial class SettingsPage : Page
    {
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _isPageLoaded;

        public bool KeepBackground_State { get; private set; }
        public bool DiscordRpc_State { get; private set; }

        public string Version
        {
            get
            {
                return App.Version ?? "Unknown";
            }
        }

        public SettingsPage()
        {
            this.InitializeComponent();
            
            try
            {
                KeepBackground_State = RegistryHelper.IsMatch("HKLM\\SOFTWARE\\SynToolkit", "KeepInBackground", 1);
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to load KeepBackground state: {ex.Message}");
                KeepBackground_State = false;
            }

            try
            {
                // Discord RPC is enabled by default (1), so we check if it's NOT disabled (0)
                DiscordRpc_State = !RegistryHelper.IsMatch("HKLM\\SOFTWARE\\SynToolkit", "DiscordRpcDisabled", 1);
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to load Discord RPC state: {ex.Message}");
                DiscordRpc_State = true; // Default to enabled
            }

            // Attach after assigning the persisted state. A programmatic IsOn
            // change must never be interpreted as a user preference change.
            BackgroundToggle.IsOn = KeepBackground_State;
            BackgroundToggle.Toggled += KeepBackground_Toggled;

            DiscordRpcToggle.IsOn = DiscordRpc_State;
            DiscordRpcToggle.Toggled += DiscordRpc_Toggled;

            try
            {
                SteamGridDbApiKeyBox.Text = Services.Games.GameArtworkService.GetConfiguredApiKey() ?? string.Empty;
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to load SteamGridDB API key: {ex.Message}");
                SteamGridDbApiKeyBox.Text = string.Empty;
            }
            
            try
            {
                this.DataContext = new SettingsPageViewModel();
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to create SettingsPageViewModel: {ex.Message}");
            }
            
            LoadText();
            LoadSystemInformation();

            Loaded += SettingsPage_Loaded;
            Unloaded += SettingsPage_Unloaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            _isPageLoaded = true;
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = false;
            _lifetimeCancellation.Cancel();
        }

        public void LoadText()
        {
            try
            {
                // Default text loading
                TitleTxt.Text = App.GetValueFromItemList("Settings");
                BehaviorHeader.Text = App.GetValueFromItemList("Behavior");
                BackgroundDescription.Header = App.GetValueFromItemList("Settings_BackgroundDesc");
                DiscordRpcCard.Header = App.GetValueFromItemList("Settings_DiscordRpc");
                DiscordRpcCard.Description = App.GetValueFromItemList("Settings_DiscordRpcDesc");
                SteamGridDbCard.Header = App.GetValueFromItemList("Settings_SteamGridDb");
                SteamGridDbCard.Description = App.GetValueFromItemList("Settings_SteamGridDbDesc");
                SteamGridDbApiKeyBox.PlaceholderText = App.GetValueFromItemList("Settings_SteamGridDbPlaceholder");
                AboutHeader.Text = App.GetValueFromItemList("About");
                toCloneRepoCard.Header = App.GetValueFromItemList("CloneRepoCard");
                bugRequestCard.Header = App.GetValueFromItemList("BugReportCard");
                WarningHeader.Header = App.GetValueFromItemList("WarningHeader");
                LanguageHeader.Header = App.GetValueFromItemList("Language");
                Update.Header = App.GetValueFromItemList("CheckUpdates");
                CheckUpdateButton.Content = App.GetValueFromItemList("CheckUpdatesBtn");
                NoUpdatesBar.Text = App.GetValueFromItemList("LatestVer");
                SystemInfo.Header = App.GetValueFromItemList("SystemInfo");
                SystemInfo.Description = App.GetValueFromItemList("SystemInfo_ReadOnly");

                // Experiments
                ExperimentalHeader.Text = App.GetValueFromItemList("ExperimentsHeader");
                ExperimentsExpander.Header = App.GetValueFromItemList("ExperimentsCardHeader");
                ExperimentsExpander.Description = App.GetValueFromItemList("ExperimentsCardDescription");
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to load settings text: {ex.Message}");
            }
        }

        private void LoadSystemInformation()
        {
            try
            {
                ISystemInformationService detector = App._host.Services.GetRequiredService<ISystemInformationService>();
                SystemInformationSnapshot snapshot = detector.Detect();

                WindowsIdentityText.Text = $"{App.GetValueFromItemList("SystemInfo_Windows")}: "
                    + $"{snapshot.WindowsProductName} • {snapshot.WindowsDisplayVersion} • "
                    + $"{snapshot.WindowsBuild} • {snapshot.Architecture}";

                if (snapshot.CustomWindowsBuild is not null)
                {
                    CustomWindowsIdentityText.Text = $"{App.GetValueFromItemList("SystemInfo_CustomBuild")}: "
                        + snapshot.CustomWindowsBuild.DisplayName;
                    CustomWindowsIdentityText.Visibility = Visibility.Visible;
                }
                else
                {
                    CustomWindowsIdentityText.Visibility = Visibility.Collapsed;
                }

                string playbookText = snapshot.Playbook.Status switch
                {
                    PlaybookDetectionStatus.Detected => string.IsNullOrWhiteSpace(snapshot.Playbook.Version)
                        ? snapshot.Playbook.Name ?? App.GetValueFromItemList("SystemInfo_NotDetected")
                        : $"{snapshot.Playbook.Name} {snapshot.Playbook.Version}",
                    PlaybookDetectionStatus.Conflicting => App.GetValueFromItemList("SystemInfo_Conflicting"),
                    _ => App.GetValueFromItemList("SystemInfo_NotDetected")
                };

                PlaybookIdentityText.Text = $"{App.GetValueFromItemList("SystemInfo_Playbook")}: {playbookText}";
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to display detected Windows and playbook information.");
                WindowsIdentityText.Text = $"{App.GetValueFromItemList("SystemInfo_Windows")}: "
                    + App.GetValueFromItemList("SystemInfo_NotDetected");
                CustomWindowsIdentityText.Visibility = Visibility.Collapsed;
                PlaybookIdentityText.Text = $"{App.GetValueFromItemList("SystemInfo_Playbook")}: "
                    + App.GetValueFromItemList("SystemInfo_NotDetected");
            }
        }

        private void KeepBackground_Toggled(object sender, RoutedEventArgs e)
        {
            SettingsBehaviorHelper.KeepBackground_Toggled(sender, e);
        }

        private void DiscordRpc_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle) return;

            try
            {
                // Store the inverse: DiscordRpcDisabled = 1 means RPC is OFF
                RegistryHelper.SetValue(
                    @"HKLM\SOFTWARE\SynToolkit",
                    "DiscordRpcDisabled",
                    toggle.IsOn ? 0 : 1,
                    Microsoft.Win32.RegistryValueKind.DWord);

                App.ContentDialogCaller("restartApp");
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to save Discord RPC state: {ex.Message}");
            }
        }

        private void SteamGridDbApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.Games.GameArtworkService.SetConfiguredApiKey(SteamGridDbApiKeyBox.Text);
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to save SteamGridDB API key: {ex.Message}");
            }
        }

        private void toCloneRepoCard_Click(object sender, RoutedEventArgs e)
        {
            DataPackage package = new DataPackage();
            package.SetText(gitCloneTextBlock.Text);
            Clipboard.SetContent(package);
        }

        private async void bugRequestCard_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://github.com/kwanteks/synergyos/issues/new"));
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            NoUpdatesBar.Visibility = Visibility.Collapsed;
            ProgressRing.Visibility = Visibility.Visible;
            CheckUpdateButton.IsEnabled = false;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            try
            {
                SynToolkitUpdateStatus status = await SynToolkitUpdateHelper.CheckUpdatesAsync(
                    forceRefresh: true,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (_isPageLoaded && status.IsUpdateAvailable)
                {
                    App.ContentDialogCaller("newUpdate");
                }
                else if (_isPageLoaded)
                {
                    NoUpdatesBar.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away makes this result stale.
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to check for updates: {ex.Message}");
            }
            finally
            {
                if (_isPageLoaded && !cancellationToken.IsCancellationRequested)
                {
                    ProgressRing.Visibility = Visibility.Collapsed;
                    CheckUpdateButton.IsEnabled = true;
                }
            }
        }
        #region experiments
        private void IsExperimentEnabled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch s || s.Tag == null) return;
            
            try
            {
                s.IsOn = RegistryHelper.IsMatch(@$"HKLM\SOFTWARE\SynToolkit\Experiments\{s.Tag}", "enabled", 1);
                s.Toggled -= ToggleState;
                s.Toggled += ToggleState;
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to check experiment state: {ex.Message}");
            }
        }

        private void ToggleState(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch s || s.Tag == null) return;
            
            try
            {
                RegistryHelper.SetValue(@$"HKLM\SOFTWARE\SynToolkit\Experiments\{s.Tag}", "enabled", s.IsOn, Microsoft.Win32.RegistryValueKind.DWord);
                App.ContentDialogCaller("restartApp");
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to toggle experiment state: {ex.Message}");
            }
        }
        #endregion experiments

    }
}
