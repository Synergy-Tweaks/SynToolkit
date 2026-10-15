using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SynToolkit.Services;
using SynToolkit.ViewModels;
using Windows.UI;
using Windows.System;

namespace SynToolkit.Views
{
    public sealed partial class AppFetchPage : Page
    {
        private readonly AppFetchPageViewModel _viewModel;
        private CancellationTokenSource _lifetimeCancellation = new();
        private readonly StackLayout _installerListLayout = new() { Spacing = 12 };
        private UniformGridLayout _installerGridLayout;
        private bool _hasLoadedInstallerStates;
        private bool _isListeningForUpdateCount;

        public AppFetchPage()
        {
            InitializeComponent();
            _installerGridLayout = FeaturedInstallersRepeater.Layout as UniformGridLayout;
            _viewModel = App._host.Services.GetRequiredService<AppFetchPageViewModel>();
            DataContext = _viewModel;
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            Unloaded += Page_Unloaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isListeningForUpdateCount)
            {
                _viewModel.AvailableInstallerUpdateCountChanged += OnAvailableInstallerUpdateCountChanged;
                _isListeningForUpdateCount = true;
            }

            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            if (_hasLoadedInstallerStates)
            {
                return;
            }

            _hasLoadedInstallerStates = true;
            try
            {
                await _viewModel.RefreshInstallerStatesAsync(_lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                _hasLoadedInstallerStates = false;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _lifetimeCancellation.Cancel();
            _viewModel.CancelPendingSearch();
            if (_isListeningForUpdateCount)
            {
                _viewModel.AvailableInstallerUpdateCountChanged -= OnAvailableInstallerUpdateCountChanged;
                _isListeningForUpdateCount = false;
            }
        }

        private void OnAvailableInstallerUpdateCountChanged(int updateCount) =>
            DispatcherQueue.TryEnqueue(() =>
                (App.m_window as MainWindow)?.UpdateInstallerUpdateBadge(updateCount));

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is InstallerNavigationRequest request &&
                !string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                _viewModel.SelectedCategory = "All";
                _viewModel.CatalogSearchText = request.SearchTerm;
                MainScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
            }
        }

        private async void InstallerPrimaryAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not FeaturedInstallerViewModel installer)
            {
                return;
            }

            try
            {
                if (installer.IsManualOnly)
                {
                    await Launcher.LaunchUriAsync(installer.DownloadUri);
                    return;
                }

                await _viewModel.InstallSingleAsync(installer);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] The quick action failed for {AppName}.", installer.Name);
            }
        }

        private void ManualSetupCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Button button || button.Content is not Border card)
            {
                return;
            }

            card.Tag ??= card.Background;
            card.Background = button.ActualTheme == ElementTheme.Dark
                ? new SolidColorBrush(Color.FromArgb(255, 54, 54, 54))
                : new SolidColorBrush(Color.FromArgb(255, 222, 222, 222));
        }

        private void ManualSetupCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button { Content: Border card } && card.Tag is Brush originalBackground)
            {
                card.Background = originalBackground;
            }
        }

        private void InstallerViewModeButton_Click(object sender, RoutedEventArgs e)
        {
            bool useGridView = ReferenceEquals(sender, GridInstallerViewButton);
            GridInstallerViewButton.IsChecked = useGridView;
            ListInstallerViewButton.IsChecked = !useGridView;
            FeaturedInstallersRepeater.Layout = useGridView
                ? _installerGridLayout ?? new UniformGridLayout
                {
                    ItemsJustification = UniformGridLayoutItemsJustification.Start,
                    ItemsStretch = UniformGridLayoutItemsStretch.Fill,
                    MinColumnSpacing = 12,
                    MinItemHeight = 268,
                    MinItemWidth = 270,
                    MinRowSpacing = 12
                }
                : _installerListLayout;
        }

        private async void InstallerShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not FeaturedInstallerViewModel installer ||
                !installer.CanManageDesktopShortcut)
            {
                return;
            }

            try
            {
                await _viewModel.ToggleDesktopShortcutAsync(installer);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] The shortcut action failed for {AppName}.", installer.Name);
            }
        }

        private async void InstallerUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not FeaturedInstallerViewModel installer ||
                !installer.CanUninstall)
            {
                return;
            }

            ContentDialog confirmationDialog = new()
            {
                Title = $"Uninstall {installer.Name}?",
                Content = "Close the app before continuing. Its publisher's uninstaller controls whether personal settings and data are kept.",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            if (await confirmationDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            WingetInstallResult result = await _viewModel.UninstallSingleAsync(installer);
            if (result.Succeeded)
            {
                return;
            }

            ContentDialog errorDialog = new()
            {
                Title = $"Could not uninstall {installer.Name}",
                Content = string.IsNullOrWhiteSpace(result.Output)
                    ? "The uninstaller did not complete. Close the app and try again."
                    : result.Output,
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }

        private void PortableAppLaunch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not InstallerQueueItemViewModel queueItem ||
                string.IsNullOrWhiteSpace(queueItem.InstallPath))
            {
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new(queueItem.InstallPath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = queueItem.InstallDirectory ?? Path.GetDirectoryName(queueItem.InstallPath)
                };

                // Use runas verb if the app requires elevation
                if (queueItem.RequiresElevation)
                {
                    startInfo.Verb = "runas";
                }

                Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] Failed to launch portable app: {AppName}", queueItem.Name);
            }
        }

        private void PortableAppOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not InstallerQueueItemViewModel queueItem ||
                string.IsNullOrWhiteSpace(queueItem.InstallDirectory))
            {
                return;
            }

            try
            {
                // Open the folder and select the executable
                if (!string.IsNullOrWhiteSpace(queueItem.InstallPath) && File.Exists(queueItem.InstallPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{queueItem.InstallPath}\"");
                }
                else if (Directory.Exists(queueItem.InstallDirectory))
                {
                    Process.Start("explorer.exe", queueItem.InstallDirectory);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Installers] Failed to open install folder: {Folder}", queueItem.InstallDirectory);
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e) => _ = RunSearchAsync(SearchBox.Text);

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
            _ = RunSearchAsync(args.QueryText);

        private async Task RunSearchAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return;
            }

            try
            {
                Task searchTask = _viewModel.SearchAsync(searchTerm, _lifetimeCancellation.Token);
                ResultsSection.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0
                });
                await searchTask;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[AppFetch] Unhandled search failure for term \"{SearchTerm}\".", searchTerm);
            }
        }
    }
}
