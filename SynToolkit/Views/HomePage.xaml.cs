using SynToolkit.Models;
using SynToolkit.Services;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SynToolkit.Views
{
    public sealed partial class HomePage : Page
    {
        private HomePageViewModel _viewModel;
        private List<IConfigurationItem> _configurationItems;
        private GitHubRelease _latestRelease;
        private bool _hasCheckedNeedsAttention;
        private bool _hasStartedReleaseNotesLoad;
        private CancellationTokenSource _lifetimeCancellation = new();

        public HomePage()
        {
            OperatingSystem os = Environment.OSVersion;

            //RecentTogglesHelper.LoadRecentToggles();
            this.InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<HomePageViewModel>();
            this.DataContext = _viewModel;
            LoadText();
            LoadFavorites();
            this.SizeChanged += MainWindow_SizeChanged;
            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;

            ProfilesListView.ItemsSource = _viewModel.ProfilesList;
            ProfilesListView.SelectedItem = _viewModel.ProfileSelected;
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            if (!_hasStartedReleaseNotesLoad)
            {
                _hasStartedReleaseNotesLoad = true;
                _ = LoadReleaseNotesAsync(_lifetimeCancellation.Token);
            }

            if (_hasCheckedNeedsAttention)
            {
                return;
            }

            _hasCheckedNeedsAttention = true;
            try
            {
                NeedsAttentionService service = App._host.Services.GetRequiredService<NeedsAttentionService>();
                NeedsAttentionSnapshot snapshot = await service.GetStartupSnapshotAsync(_lifetimeCancellation.Token);
                if (snapshot.Items.Count == 0)
                {
                    return;
                }

                NeedsAttentionSummary.Text = snapshot.Items.Count == 1
                    ? snapshot.Items[0].Title
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        App.GetValueFromItemList("NeedsAttention_HomeSummaryMany"),
                        snapshot.Items.Count);
                NeedsAttentionCallout.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[NeedsAttention] Home callout could not be refreshed.");
            }
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e) => _lifetimeCancellation.Cancel();

        private void NeedsAttentionButton_Click(object sender, RoutedEventArgs e) =>
            (App.m_window as MainWindow)?.NavigateToPage(
                typeof(NeedsAttentionPage),
                "SynToolkit.Views.NeedsAttentionPage");

        private async Task LoadReleaseNotesAsync(CancellationToken cancellationToken)
        {
            try
            {
                GitHubReleaseService releaseService = new();
                _latestRelease = await releaseService.GetLatestReleaseAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (_latestRelease is not null)
                {
                    ReleaseTitle.Text = _latestRelease.Name;
                    ReleaseDate.Text = _latestRelease.FormattedDate;
                    ReleaseBody.Text = _latestRelease.ShortBody;

                    ReleaseNotesLoading.Visibility = Visibility.Collapsed;
                    ReleaseNotesContent.Visibility = Visibility.Visible;
                }
                else
                {
                    ReleaseNotesLoading.Visibility = Visibility.Collapsed;
                    ReleaseNotesError.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                App.logger.Warn(ex, "Failed to load release notes on home page.");
                ReleaseNotesLoading.Visibility = Visibility.Collapsed;
                ReleaseNotesError.Visibility = Visibility.Visible;
            }
        }

        private async void ViewAllReleasesButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(GitHubReleaseService.ReleasesUrl));
        }

        private async void ReadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_latestRelease is not null && !string.IsNullOrWhiteSpace(_latestRelease.HtmlUrl))
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(_latestRelease.HtmlUrl));
            }
            else
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(GitHubReleaseService.ReleasesUrl));
            }
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.ActualWidth <= 640)
            {
                Grid.SetRow(ProfilesPanel, 3);
                Grid.SetColumn(ProfilesPanel, 0);
                Grid.SetColumnSpan(FavoritesSection, 2);
                ProfilesPanel.Margin = new Thickness{ Left = 36, Right = 5, Top = 0, Bottom = 0 }; 
            }
            if (this.ActualWidth >= 640)
            {
                Grid.SetRow(ProfilesPanel, 2);
                Grid.SetColumn(ProfilesPanel, 1);
                Grid.SetColumnSpan(FavoritesSection, 1);
                ProfilesPanel.Margin = new Thickness { Left = 16, Right = 5, Top = 0, Bottom = 0 };
            }
        }
        private void LoadFavorites()
        {
            FavoriteIdMigrationRegistry.MigrateFavoritesRegistry();

            _configurationItems = new List<IConfigurationItem>();
            // Get all values in the Favorites reg key
            string keyPath = @"SOFTWARE\SynToolkit\Favorites";
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (key != null)
                {
                    foreach (string valueName in key.GetValueNames())
                    {
                        try
                        {
                            foreach (string mappedId in FavoriteIdMigration.Expand(valueName))
                            {
                                var favorite = App.RootList.FirstOrDefault(item => item.Key == mappedId);
                                if (favorite is not null
                                    && !_configurationItems.Any(item => item.Key == favorite.Key))
                                {
                                    _configurationItems.Add(favorite);
                                }
                            }
                        }
                        catch
                        {
                            App.logger.Error(@$"Value {valueName} was not found in RootList when trying to initialize favorites");
                        }
                    }
                }
                else
                {
                    App.logger.Warn(@$"Key ""HKLM\SOFTWARE\SynToolkit\Favorites"" was not found");
                }
            }
            if (_configurationItems.Count == 0)
            {
                NoFavoritesText.Visibility = Visibility.Visible;
                FavoritesPanel.MinHeight = 300;
            }
            else
            {
                NoFavoritesText.Visibility = Visibility.Collapsed;
                FavoritesPanel.MinHeight = 50;
            }
            FavoritesControl.ItemsSource = _configurationItems;
        }
        private void LoadText()
        {
            // Home Header
            HomeHeaderText.Text = App.GetValueFromItemList("Home_HeaderText");
            try
            {
                WelcomeText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    App.WelcomeGreetingFormat,
                    App.DisplayUserName);
            }
            catch (FormatException)
            {
                WelcomeText.Text = App.IsReturningUser
                    ? $"Welcome back, {App.DisplayUserName}"
                    : $"Welcome, {App.DisplayUserName}";
            }

            // Release Notes
            ReleaseNotesHeader.Text = App.GetValueFromItemList("Home_WhatsNew");
            ViewAllReleasesButton.Content = App.GetValueFromItemList("Home_ViewAllReleases");
            ReadMoreButton.Content = App.GetValueFromItemList("Home_ReadMore");
            ReleaseNotesError.Text = App.GetValueFromItemList("Home_ReleaseNotesError");

            // Other
            ProfilesHeader.Text = App.GetValueFromItemList("Home_ProfilesText");
            FavoritesHeader.Text = App.GetValueFromItemList("Home_Favorites");
            NewProfileButton.Content = App.GetValueFromItemList("NewProfilesButton");
            NoFavoritesText.Text = App.GetValueFromItemList("NoFavorites");
        }

        /// <summary>
        /// Deletes the profile
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DeleteProfile(object sender, RoutedEventArgs e)
        {
            if (ProfilesListView.SelectedItem != null)
            {
                var selectedItem = ProfilesListView.SelectedItem as Profiles;

                if (selectedItem.Key != "default.json")
                {
                    ContentDialog dialog = new ContentDialog();

                    dialog.XamlRoot = this.XamlRoot;
                    dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
                    dialog.Title = App.GetValueFromItemList("DeleteProfileConfirmation");
                    dialog.PrimaryButtonText = App.GetValueFromItemList("Yes");
                    dialog.CloseButtonText = App.GetValueFromItemList("Cancel");
                    dialog.DefaultButton = ContentDialogButton.Primary;
                    dialog.PrimaryButtonCommand = _viewModel.RemoveProfileCommand;

                    var result = await dialog.ShowAsync();
                }
                else
                {
                    ContentDialog dialog = new ContentDialog();

                    dialog.XamlRoot = this.XamlRoot;
                    dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
                    dialog.Title = App.GetValueFromItemList("TryDeleteDefaultProfile");
                    dialog.CloseButtonText = App.GetValueFromItemList("Ok");
                    dialog.DefaultButton = ContentDialogButton.Primary;

                    var result = await dialog.ShowAsync();
                }
            }
        }


        private async void SetProfile_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog();

            dialog.XamlRoot = this.XamlRoot;
            dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            dialog.Title = App.GetValueFromItemList("Home_SetProfileConfig");
            dialog.PrimaryButtonText = App.GetValueFromItemList("Yes"); ;
            dialog.CloseButtonText = App.GetValueFromItemList("No"); ;
            dialog.DefaultButton = ContentDialogButton.Primary;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                RestartPCPrompt();
                _viewModel.SetProfileCommand.Execute(this);
            }
        }

        /// <summary>
        /// Prompts the user to restart their PC
        /// </summary>
        private async void RestartPCPrompt()
        {
            ContentDialog dialog = new ContentDialog();

            dialog.XamlRoot = this.XamlRoot;
            dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            dialog.Title = App.GetValueFromItemList("RestartPCPromptHeader");
            dialog.PrimaryButtonText = App.GetValueFromItemList("Restart"); ;
            dialog.CloseButtonText = App.GetValueFromItemList("Later"); ;
            dialog.DefaultButton = ContentDialogButton.Primary;
            dialog.PrimaryButtonCommand = new RelayCommand(ComputerStateHelper.RestartComputer);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ComputerStateHelper.RestartComputer();
            }
        }

        private async void NewProfile()
        {
            ContentDialog dialog = new ContentDialog();

            dialog.XamlRoot = this.XamlRoot;
            dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
            dialog.Title = App.GetValueFromItemList("NewProfilesButton");
            dialog.PrimaryButtonText = App.GetValueFromItemList("Create");
            dialog.CloseButtonText = App.GetValueFromItemList("Cancel");
            dialog.Content = new NewProfilePage(_viewModel);
            dialog.DefaultButton = ContentDialogButton.Primary;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                _viewModel.AddProfileCommand.Execute(null);
            }
            Name = "";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            NewProfile();
        }

        private void SetProfile_Loaded(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuFlyoutItem;
            button.Text = App.GetValueFromItemList("Home_SetProfileBtn");
        }

        private void DeleteProfile_Loaded(object sender, RoutedEventArgs e)
        {
            var button = sender as MenuFlyoutItem;
            button.Text = App.GetValueFromItemList("Home_DeleteProfileBtn");
        }
        private void ToggleSwitch_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggleSwitch)
            {
                toggleSwitch.Toggled -= ToggleSwitchBehavior.OnToggled;
                toggleSwitch.Toggled += ToggleSwitchBehavior.OnToggled;
            }
        }

        private async void LinkCard_Click(object sender, RoutedEventArgs e)
        {
            SettingsCard linkCard = sender as SettingsCard;
            LinksViewModel linkVM = linkCard.DataContext as LinksViewModel;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(linkVM.Link));
        }

        private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem menuFlyoutItem = sender as MenuFlyoutItem;
            try
            {
                RegistryHelper.DeleteValue(@"HKLM\SOFTWARE\\SynToolkit\\Favorites", menuFlyoutItem.Tag.ToString());
                LoadFavorites();
            }
            catch
            {
                App.logger.Error($@"{menuFlyoutItem.Tag.ToString()} value was not found");
            }
        }
    }
}
