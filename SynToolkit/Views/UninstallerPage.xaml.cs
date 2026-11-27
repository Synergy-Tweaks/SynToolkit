#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services.Uninstaller;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class UninstallerPage : Page
    {
        private readonly UninstallerPageViewModel _viewModel;
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _hasScanned;
        private bool _suppressNextItemClick;

        public UninstallerPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<UninstallerPageViewModel>();
            DataContext = _viewModel;
            Unloaded += Page_Unloaded;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hasScanned)
            {
                return;
            }

            _hasScanned = true;
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            try
            {
                await _viewModel.ScanAsync(_lifetimeCancellation.Token);
                _ = LoadIconsAsync();
            }
            catch (OperationCanceledException)
            {
                _hasScanned = false;
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e) => _lifetimeCancellation.Cancel();

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            try
            {
                await _viewModel.ScanAsync(_lifetimeCancellation.Token);
                _ = LoadIconsAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// Fills row icons after the list is visible so scanning stays fast.
        /// WinRT thumbnails are awaited on the UI thread (BitmapImage requires it).
        /// </summary>
        private async Task LoadIconsAsync()
        {
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            if (XamlRoot is not null)
            {
                InstalledAppIconHelper.DisplayScale = XamlRoot.RasterizationScale;
            }

            Dictionary<string, List<Services.Uninstaller.ShortcutIconEntry>> shortcutMap;
            try
            {
                shortcutMap = await Task.Run(
                    Services.Uninstaller.InstalledAppIconHelper.BuildShortcutMap,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Icons used to load strictly one after another, so the last rows waited
            // for every thumbnail before them. Extraction runs on the thread pool, so
            // let several run at once; only the BitmapImage step comes back to the UI
            // thread. Visible apps (the filtered list) go first.
            InstalledAppItemViewModel[] apps = _viewModel.FilteredApps
                .Concat(_viewModel.AllApps)
                .Where(app => !app.HasCustomIcon)
                .Distinct()
                .ToArray();

            using SemaphoreSlim gate = new(6);
            IEnumerable<Task> loads = apps.Select(async app =>
            {
                try
                {
                    await gate.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    ImageSource? icon = await InstalledAppIconHelper.GetIconAsync(
                        app.DisplayName,
                        app.DisplayIcon,
                        app.InstallLocation,
                        shortcutMap,
                        cancellationToken);
                    if (icon is not null)
                    {
                        app.SetIcon(icon);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(loads);
        }

        private void AppSelectionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _suppressNextItemClick = true;
        }

        private void AppCard_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_suppressNextItemClick)
            {
                _suppressNextItemClick = false;
                return;
            }

            if (sender is FrameworkElement element &&
                element.DataContext is InstalledAppItemViewModel app)
            {
                _viewModel.ToggleSelection(app);
            }
        }

        private async void UninstallSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanUninstallSelected)
            {
                return;
            }

            IReadOnlyList<InstalledAppItemViewModel> selectedApps = _viewModel.GetSelectedApps();
            if (selectedApps.Count == 0)
            {
                return;
            }

            bool confirmed = await ConfirmUninstallSelectionAsync(selectedApps);
            if (!confirmed)
            {
                return;
            }

            try
            {
                UninstallBatchResult result = await _viewModel.RunUninstallQueueAsync(selectedApps, _lifetimeCancellation.Token);
                await ShowQueueSummaryAsync(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Uninstaller] Queue uninstall failed.");
            }
        }

        private async Task<bool> ConfirmUninstallSelectionAsync(IReadOnlyList<InstalledAppItemViewModel> selectedApps)
        {
            StackPanel contentPanel = new() { Spacing = 8 };
            contentPanel.Children.Add(new TextBlock
            {
                Text = "These apps will be uninstalled in order:",
                TextWrapping = TextWrapping.Wrap
            });

            StackPanel appListPanel = new() { Spacing = 4 };
            foreach (InstalledAppItemViewModel app in selectedApps)
            {
                appListPanel.Children.Add(new TextBlock
                {
                    Text = app.IsRisky ? "[Risky] " + app.DisplayName : "- " + app.DisplayName,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            contentPanel.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                Content = appListPanel
            });

            ContentDialog dialog = new()
            {
                Title = $"Uninstall {selectedApps.Count} selected app(s)?",
                Content = contentPanel,
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            if (!selectedApps.Any(app => app.IsRisky))
            {
                return true;
            }

            ContentDialog riskyDialog = new()
            {
                Title = "Risky uninstall selection",
                Content =
                    "Your selection includes shared components (drivers, runtimes, or WebView dependencies). " +
                    "Removing them can break other apps or hardware features.\n\nContinue anyway?",
                PrimaryButtonText = "Uninstall anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            return await riskyDialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task ShowQueueSummaryAsync(UninstallBatchResult result)
        {
            string summary = $"{result.Removed} removed, {result.Failed} failed, {result.Skipped} skipped";
            ContentDialog summaryDialog = new()
            {
                Title = "Uninstall finished",
                Content = summary,
                PrimaryButtonText = "Close",
                SecondaryButtonText = result.Failures.Count > 0 ? "Show details" : string.Empty,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            ContentDialogResult dialogResult = await summaryDialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Secondary || result.Failures.Count == 0)
            {
                return;
            }

            StackPanel detailsPanel = new() { Spacing = 6 };
            foreach (UninstallFailureDetail failure in result.Failures)
            {
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = $"- {failure.AppName}: {failure.Reason}",
                    TextWrapping = TextWrapping.Wrap
                });
            }

            ContentDialog detailDialog = new()
            {
                Title = "Uninstall failures",
                Content = new ScrollViewer
                {
                    MaxHeight = 300,
                    Content = detailsPanel
                },
                CloseButtonText = "Close",
                XamlRoot = XamlRoot
            };
            await detailDialog.ShowAsync();
        }

        private void CancelQueueButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.RequestCancelQueue();
        }
    }
}
