#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SynToolkit.Services;
using SynToolkit.Services.Uninstaller;
using SynToolkit.ViewModels;
using Windows.Storage.Streams;

namespace SynToolkit.Views
{
    public sealed partial class UninstallerPage : Page
    {
        private readonly UninstallerPageViewModel _viewModel;
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _hasScanned;

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
        /// Extraction runs off the UI thread; BitmapImage creation stays on it.
        /// </summary>
        private async Task LoadIconsAsync()
        {
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            foreach (InstalledAppItemViewModel app in _viewModel.AllApps.ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                byte[]? png;
                try
                {
                    png = await Task.Run(
                        () => InstalledAppIconHelper.TryExtractIconPng(app.DisplayIcon, app.InstallLocation),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (png is null || png.Length == 0 || cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    using InMemoryRandomAccessStream stream = new();
                    using (DataWriter writer = new(stream))
                    {
                        writer.WriteBytes(png);
                        await writer.StoreAsync();
                    }

                    stream.Seek(0);
                    BitmapImage bitmap = new();
                    await bitmap.SetSourceAsync(stream);
                    app.SetIcon(bitmap);
                }
                catch
                {
                    // keep the default icon
                }
            }
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not InstalledAppItemViewModel app ||
                !app.CanUninstall ||
                app.IsUninstalling)
            {
                return;
            }

            ContentDialog confirmationDialog = new()
            {
                Title = $"Uninstall {app.DisplayName}?",
                Content = "Close the app before continuing. Its publisher's uninstaller controls whether personal settings and data are kept.",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await confirmationDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            app.IsUninstalling = true;
            try
            {
                WingetInstallResult result = await _viewModel.UninstallAsync(app, _lifetimeCancellation.Token);
                if (result.Succeeded)
                {
                    _viewModel.RemoveApp(app);
                    return;
                }

                ContentDialog errorDialog = new()
                {
                    Title = $"Could not uninstall {app.DisplayName}",
                    Content = string.IsNullOrWhiteSpace(result.Output)
                        ? "The uninstaller did not complete. Close the app and try again."
                        : result.Output,
                    CloseButtonText = "Close",
                    XamlRoot = XamlRoot
                };
                await errorDialog.ShowAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Uninstaller] Uninstall failed for {AppName}.", app.DisplayName);
            }
            finally
            {
                app.IsUninstalling = false;
            }
        }
    }
}
