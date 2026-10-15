using SynToolkit.Models;
using SynToolkit.Services;
using SynToolkit.Services.SynergyOsUpdate;
using SynToolkit.Services.SynToolkitUpdate;
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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace SynToolkit.Views
{
    public sealed partial class SettingsPage : Page
    {
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _isPageLoaded;

        public bool KeepBackground_State { get; private set; }
        public bool DiscordRpc_State { get; private set; }
        private ContextMenuState _contextMenuState;

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

            // Initialize context menu toggle state
            RefreshContextMenuState();
            ContextMenuToggle.Toggled += ContextMenuToggle_Toggled;

            SynergyOsUpdateNotifyToggle.IsOn = SynergyOsUpdateChecker.Default.Settings.IsEnabled;
            SynergyOsUpdateNotifyToggle.Toggled += SynergyOsUpdateNotifyToggle_Toggled;
            RefreshSynergyOsUpdateStatusText();

            SynToolkitUpdateNotifyToggle.IsOn = SynToolkitUpdateChecker.Default.Settings.IsEnabled;
            SynToolkitUpdateNotifyToggle.Toggled += SynToolkitUpdateNotifyToggle_Toggled;
            RefreshSynToolkitUpdateStatusText();

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
            LoadPreferredGitHubRepository();
            ApplyPreferredGitHubRepositoryUi();
            LoadSystemInformation();

            SynergyOsGitHubLink.NavigateUri = new Uri(CommunityLinks.SynergyOsRepoUrl);
            DiscordCommunityLink.NavigateUri = new Uri(CommunityLinks.DiscordInviteUrl);

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

            // Refresh context menu state when page regains focus
            RefreshContextMenuState();
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
                ContextMenuCard.Header = App.GetValueFromItemList("Settings_ContextMenu");
                ContextMenuCard.Description = App.GetValueFromItemList("Settings_ContextMenuDesc");
                SteamGridDbCard.Header = App.GetValueFromItemList("Settings_SteamGridDb");
                SteamGridDbCard.Description = App.GetValueFromItemList("Settings_SteamGridDbDesc");
                SteamGridDbApiKeyBox.PlaceholderText = App.GetValueFromItemList("Settings_SteamGridDbPlaceholder");
                DefaultGitHubRepoCard.Header = App.GetValueFromItemList("Settings_DefaultGitHubRepo");
                DefaultGitHubRepoCard.Description = App.GetValueFromItemList("Settings_DefaultGitHubRepoDesc");
                DefaultGitHubRepoSynToolkitItem.Content = App.GetValueFromItemList("Settings_DefaultGitHubRepo_SynToolkit");
                DefaultGitHubRepoSynergyOsItem.Content = App.GetValueFromItemList("Settings_DefaultGitHubRepo_SynergyOs");
                AboutHeader.Text = App.GetValueFromItemList("About");
                toCloneRepoCard.Header = App.GetValueFromItemList("CloneRepoCard");
                bugRequestCard.Header = App.GetValueFromItemList("BugReportCard");
                WarningHeader.Header = App.GetValueFromItemList("WarningHeader");
                LanguageHeader.Header = App.GetValueFromItemList("Language");
                SynergyOsUpdateNotifyCard.Header = App.GetValueFromItemList("SynergyOsUpdate_NotifyToggle");
                SynergyOsUpdateNotifyCard.Description = App.GetValueFromItemList("SynergyOsUpdate_NotifyToggleDesc");
                SynergyOsUpdateCheckCard.Header = App.GetValueFromItemList("SynergyOsUpdate_CheckNow");
                SynergyOsUpdateCheckCard.Description = App.GetValueFromItemList("SynergyOsUpdate_CheckNowDesc");
                SynergyOsCheckUpdateButton.Content = App.GetValueFromItemList("SynergyOsUpdate_CheckNowBtn");
                SynergyOsViewReleaseButton.Content = App.GetValueFromItemList("SynergyOsUpdate_ViewRelease");
                RefreshSynergyOsUpdateStatusText();
                SynToolkitUpdateNotifyCard.Header = App.GetValueFromItemList("SynToolkitUpdate_NotifyToggle");
                SynToolkitUpdateNotifyCard.Description = App.GetValueFromItemList("SynToolkitUpdate_NotifyToggleDesc");
                SynToolkitUpdateCheckCard.Header = App.GetValueFromItemList("SynToolkitUpdate_CheckNow");
                SynToolkitUpdateCheckCard.Description = App.GetValueFromItemList("SynToolkitUpdate_CheckNowDesc");
                SynToolkitCheckUpdateButton.Content = App.GetValueFromItemList("SynToolkitUpdate_CheckNowBtn");
                SynToolkitViewReleaseButton.Content = App.GetValueFromItemList("SynToolkitUpdate_ViewRelease");
                RefreshSynToolkitUpdateStatusText();
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

        private void RefreshContextMenuState()
        {
            ContextMenuDetectionResult detection = ContextMenuService.DetectCurrentState();
            _contextMenuState = detection.State;

            // Detach handler to prevent triggering during programmatic update
            ContextMenuToggle.Toggled -= ContextMenuToggle_Toggled;

            switch (detection.State)
            {
                case ContextMenuState.On:
                    ContextMenuToggle.IsOn = true;
                    ContextMenuToggle.IsEnabled = true;
                    ContextMenuStatusText.Visibility = Visibility.Collapsed;
                    break;

                case ContextMenuState.Off:
                    ContextMenuToggle.IsOn = false;
                    ContextMenuToggle.IsEnabled = true;
                    ContextMenuStatusText.Visibility = Visibility.Collapsed;
                    break;

                case ContextMenuState.Modified:
                    // Show as "on" since something exists, but warn the user
                    ContextMenuToggle.IsOn = true;
                    ContextMenuToggle.IsEnabled = true;
                    ContextMenuStatusText.Text = App.GetValueFromItemList("Settings_ContextMenuOutOfDate");
                    ContextMenuStatusText.Visibility = Visibility.Visible;
                    break;

                case ContextMenuState.Error:
                    ContextMenuToggle.IsOn = false;
                    ContextMenuToggle.IsEnabled = false;
                    ContextMenuStatusText.Text = App.GetValueFromItemList("Settings_ContextMenuUnknown");
                    ContextMenuStatusText.Visibility = Visibility.Visible;
                    break;
            }

            ContextMenuToggle.Toggled += ContextMenuToggle_Toggled;
        }

        private async void ContextMenuToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle)
            {
                return;
            }

            bool targetOn = toggle.IsOn;

            // Disable toggle while processing
            toggle.IsEnabled = false;
            ContextMenuStatusText.Visibility = Visibility.Collapsed;

            try
            {
                ContextMenuOperationResult result = await Task.Run(() =>
                    targetOn ? ContextMenuService.Enable() : ContextMenuService.Disable());

                if (!_isPageLoaded)
                {
                    return;
                }

                if (result.Success)
                {
                    _contextMenuState = result.DetectedState.State;
                    ContextMenuStatusText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Operation failed - revert toggle to detected state and show warning
                    App.logger.Warn($"[ContextMenu] Toggle operation failed: {result.FailedStep}");

                    _contextMenuState = result.DetectedState.State;

                    // Detach to prevent re-triggering
                    toggle.Toggled -= ContextMenuToggle_Toggled;
                    toggle.IsOn = result.DetectedState.State is ContextMenuState.On or ContextMenuState.Modified;
                    toggle.Toggled += ContextMenuToggle_Toggled;

                    ContextMenuStatusText.Text = result.FailedStep ?? App.GetValueFromItemList("Settings_ContextMenuUnknown");
                    ContextMenuStatusText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[ContextMenu] Unexpected error during toggle.");

                if (_isPageLoaded)
                {
                    // Refresh to get actual state
                    RefreshContextMenuState();
                }
            }
            finally
            {
                if (_isPageLoaded)
                {
                    toggle.IsEnabled = _contextMenuState != ContextMenuState.Error;
                }
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

        private void LoadPreferredGitHubRepository()
        {
            PreferredGitHubRepository preferred = PreferredGitHubRepositorySettings.Get();
            DefaultGitHubRepoCombo.SelectionChanged -= DefaultGitHubRepoCombo_SelectionChanged;
            DefaultGitHubRepoCombo.SelectedItem = preferred == PreferredGitHubRepository.SynergyOS
                ? DefaultGitHubRepoSynergyOsItem
                : DefaultGitHubRepoSynToolkitItem;
            DefaultGitHubRepoCombo.SelectionChanged += DefaultGitHubRepoCombo_SelectionChanged;
        }

        private void ApplyPreferredGitHubRepositoryUi()
        {
            PreferredGitHubRepository preferred = PreferredGitHubRepositorySettings.Get();
            gitCloneTextBlock.Text = CommunityLinks.GetCloneCommand(preferred);
        }

        private void DefaultGitHubRepoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DefaultGitHubRepoCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
            {
                return;
            }

            PreferredGitHubRepository preferred = PreferredGitHubRepositorySettings.Parse(tag);
            try
            {
                PreferredGitHubRepositorySettings.Set(preferred);
                ApplyPreferredGitHubRepositoryUi();
            }
            catch (Exception ex)
            {
                App.logger.Error($"Failed to save preferred GitHub repository: {ex.Message}");
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
            await CommunityLinks.LaunchPreferredNewIssueAsync();
        }

        private void SynergyOsUpdateNotifyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle)
            {
                return;
            }

            try
            {
                SynergyOsUpdateChecker.Default.Settings.IsEnabled = toggle.IsOn;
            }
            catch (Exception exception)
            {
                App.logger.Error($"Failed to save SynergyOS update preference: {exception.Message}");
            }
        }

        private async void SynergyOsCheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            SynergyOsUpdateStatusText.Text = string.Empty;
            SynergyOsViewReleaseButton.Visibility = Visibility.Collapsed;
            SynergyOsUpdateProgressRing.Visibility = Visibility.Visible;
            SynergyOsCheckUpdateButton.IsEnabled = false;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            try
            {
                SynergyOsUpdateCheckResult result;
                if (App.m_window is MainWindow mainWindow)
                {
                    result = await mainWindow.CheckSynergyOsUpdatesFromSettingsAsync(cancellationToken);
                }
                else
                {
                    result = await SynergyOsUpdateChecker.Default.CheckAsync(
                        forceRefresh: true,
                        ignoreSessionGate: true,
                        cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!_isPageLoaded)
                {
                    return;
                }

                ApplySynergyOsStatusFromCheckResult(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Manual SynergyOS update check failed.");
                if (_isPageLoaded)
                {
                    SynergyOsUpdateStatusText.Text = App.GetValueFromItemList("SynergyOsUpdate_CouldNotCheck");
                    SynergyOsViewReleaseButton.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                if (_isPageLoaded && !cancellationToken.IsCancellationRequested)
                {
                    SynergyOsUpdateProgressRing.Visibility = Visibility.Collapsed;
                    SynergyOsCheckUpdateButton.IsEnabled = true;
                }
            }
        }

        private async void SynergyOsViewReleaseButton_Click(object sender, RoutedEventArgs e)
        {
            await CommunityLinks.LaunchUriAsync(SynergyOsReleaseUrl.ResolveSafeOpenUrl());
        }

        private void RefreshSynergyOsUpdateStatusText()
        {
            SynergyOsUpdateSettingsDocument snapshot = SynergyOsUpdateChecker.Default.Settings.Snapshot();
            string installed = SynergyOsUpdateChecker.InstalledVersionProvider();

            if (snapshot.CachedRelease is null && snapshot.LastSuccessfulCheckUtc is null)
            {
                SynergyOsUpdateStatusText.Text = App.GetValueFromItemList("SynergyOsUpdate_NeverChecked");
                SynergyOsViewReleaseButton.Visibility = Visibility.Collapsed;
                return;
            }

            SynergyOsUpdateAvailability status = SynergyOsUpdateDecision.GetUpdateStatus(
                installed,
                snapshot.CachedRelease);
            bool skipped = SynergyOsUpdateDecision.IsVersionSkipped(snapshot.CachedRelease, snapshot);
            SynergyOsUpdateStatusText.Text = SynergyOsUpdateDecision.FormatSettingsStatusText(
                status,
                installed,
                snapshot.CachedRelease,
                skipped,
                key => App.GetValueFromItemList(key));
            SynergyOsViewReleaseButton.Visibility =
                status == SynergyOsUpdateAvailability.UpdateAvailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ApplySynergyOsStatusFromCheckResult(SynergyOsUpdateCheckResult result)
        {
            SynergyOsUpdateSettingsDocument snapshot = SynergyOsUpdateChecker.Default.Settings.Snapshot();

            if (result.Outcome is SynergyOsUpdateCheckOutcome.Failed
                or SynergyOsUpdateCheckOutcome.SkippedRateLimited)
            {
                SynergyOsUpdateStatusText.Text = App.GetValueFromItemList("SynergyOsUpdate_CouldNotCheck");
                SynergyOsViewReleaseButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (result.Outcome == SynergyOsUpdateCheckOutcome.SkippedNotSynergyOs)
            {
                SynergyOsUpdateStatusText.Text = App.GetValueFromItemList("SynergyOsUpdate_NotDetected");
                SynergyOsViewReleaseButton.Visibility = Visibility.Collapsed;
                return;
            }

            SynergyOsCachedRelease latest = result.Latest ?? snapshot.CachedRelease;
            SynergyOsUpdateAvailability status = result.Outcome switch
            {
                SynergyOsUpdateCheckOutcome.NoReleases => SynergyOsUpdateAvailability.Unknown,
                _ => SynergyOsUpdateDecision.GetUpdateStatus(result.InstalledVersion, latest),
            };

            bool skipped = SynergyOsUpdateDecision.IsVersionSkipped(latest, snapshot);
            SynergyOsUpdateStatusText.Text = SynergyOsUpdateDecision.FormatSettingsStatusText(
                status,
                result.InstalledVersion,
                latest,
                skipped,
                key => App.GetValueFromItemList(key));
            SynergyOsViewReleaseButton.Visibility =
                status == SynergyOsUpdateAvailability.UpdateAvailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void SynToolkitUpdateNotifyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleSwitch toggle)
            {
                return;
            }

            try
            {
                SynToolkitUpdateChecker.Default.Settings.IsEnabled = toggle.IsOn;
            }
            catch (Exception exception)
            {
                App.logger.Error($"Failed to save SynToolkit update preference: {exception.Message}");
            }
        }

        private async void SynToolkitCheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            SynToolkitUpdateStatusText.Text = string.Empty;
            SynToolkitViewReleaseButton.Visibility = Visibility.Collapsed;
            SynToolkitUpdateProgressRing.Visibility = Visibility.Visible;
            SynToolkitCheckUpdateButton.IsEnabled = false;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            try
            {
                SynToolkitUpdateCheckResult result;
                if (App.m_window is MainWindow mainWindow)
                {
                    result = await mainWindow.CheckSynToolkitUpdatesFromSettingsAsync(cancellationToken);
                }
                else
                {
                    result = await SynToolkitUpdateChecker.Default.CheckAsync(
                        forceRefresh: true,
                        ignoreSessionGate: true,
                        cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!_isPageLoaded)
                {
                    return;
                }

                ApplySynToolkitStatusFromCheckResult(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Manual SynToolkit update check failed.");
                if (_isPageLoaded)
                {
                    SynToolkitUpdateStatusText.Text = App.GetValueFromItemList("SynToolkitUpdate_CouldNotCheck");
                    SynToolkitViewReleaseButton.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                if (_isPageLoaded && !cancellationToken.IsCancellationRequested)
                {
                    SynToolkitUpdateProgressRing.Visibility = Visibility.Collapsed;
                    SynToolkitCheckUpdateButton.IsEnabled = true;
                }
            }
        }

        private async void SynToolkitViewReleaseButton_Click(object sender, RoutedEventArgs e)
        {
            await CommunityLinks.LaunchUriAsync(SynToolkitReleaseUrl.ResolveSafeOpenUrl());
        }

        private void RefreshSynToolkitUpdateStatusText()
        {
            SynToolkitUpdateSettingsDocument snapshot = SynToolkitUpdateChecker.Default.Settings.Snapshot();
            string installed = SynToolkitUpdateChecker.InstalledVersionProvider();

            if (snapshot.CachedRelease is null && snapshot.LastSuccessfulCheckUtc is null)
            {
                SynToolkitUpdateStatusText.Text = App.GetValueFromItemList("SynToolkitUpdate_NeverChecked");
                SynToolkitViewReleaseButton.Visibility = Visibility.Collapsed;
                return;
            }

            SynToolkitUpdateAvailability status = SynToolkitUpdateDecision.GetUpdateStatus(
                installed,
                snapshot.CachedRelease);
            bool skipped = SynToolkitUpdateDecision.IsVersionSkipped(snapshot.CachedRelease, snapshot);
            string statusText = SynToolkitUpdateDecision.FormatSettingsStatusText(
                status,
                installed,
                snapshot.CachedRelease,
                skipped,
                key => App.GetValueFromItemList(key));
            SynToolkitUpdateStatusText.Text = AppendLastChecked(statusText, snapshot.LastSuccessfulCheckUtc);
            SynToolkitViewReleaseButton.Visibility =
                status == SynToolkitUpdateAvailability.UpdateAvailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ApplySynToolkitStatusFromCheckResult(SynToolkitUpdateCheckResult result)
        {
            SynToolkitUpdateSettingsDocument snapshot = SynToolkitUpdateChecker.Default.Settings.Snapshot();

            if (result.Outcome is SynToolkitUpdateCheckOutcome.Failed
                or SynToolkitUpdateCheckOutcome.SkippedRateLimited)
            {
                SynToolkitUpdateStatusText.Text = AppendLastChecked(
                    App.GetValueFromItemList("SynToolkitUpdate_CouldNotCheck"),
                    snapshot.LastSuccessfulCheckUtc);
                SynToolkitViewReleaseButton.Visibility = Visibility.Collapsed;
                return;
            }

            SynToolkitCachedRelease latest = result.Latest ?? snapshot.CachedRelease;
            SynToolkitUpdateAvailability status = result.Outcome switch
            {
                SynToolkitUpdateCheckOutcome.NoReleases => SynToolkitUpdateAvailability.Unknown,
                _ => SynToolkitUpdateDecision.GetUpdateStatus(result.InstalledVersion, latest),
            };

            bool skipped = SynToolkitUpdateDecision.IsVersionSkipped(latest, snapshot);
            string statusText = SynToolkitUpdateDecision.FormatSettingsStatusText(
                status,
                result.InstalledVersion,
                latest,
                skipped,
                key => App.GetValueFromItemList(key));
            SynToolkitUpdateStatusText.Text = AppendLastChecked(statusText, snapshot.LastSuccessfulCheckUtc);
            SynToolkitViewReleaseButton.Visibility =
                status == SynToolkitUpdateAvailability.UpdateAvailable
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private static string AppendLastChecked(string statusText, DateTimeOffset? lastCheckedUtc)
        {
            string prefix = App.GetValueFromItemList("SynToolkitUpdate_LastCheckedPrefix");
            if (lastCheckedUtc is DateTimeOffset when)
            {
                string stamp = when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                return statusText + "  " + prefix + " " + stamp;
            }

            return statusText + "  " + prefix + " " + App.GetValueFromItemList("SynToolkitUpdate_NeverChecked");
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
