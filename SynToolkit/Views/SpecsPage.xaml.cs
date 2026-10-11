#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class SpecsPage : Page
    {
        private readonly SpecsPageViewModel _viewModel;
        private readonly DispatcherTimer _cpuMonitoringTimer;
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _isCpuDetailsExpanded;
        private CancellationTokenSource? _cpuMonitoringCancellation;

        public SpecsPage()
        {
            InitializeComponent();

            try
            {
                _viewModel = App._host.Services.GetRequiredService<SpecsPageViewModel>();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Specs] Failed to create SpecsPageViewModel - native DLL or service initialization issue.");

                // Create a minimal ViewModel that shows the error
                _viewModel = CreateFallbackViewModel(exception);
            }

            DataContext = _viewModel;

            _cpuMonitoringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
            _cpuMonitoringTimer.Tick += CpuMonitoringTimer_Tick;
            Loaded += SpecsPage_Loaded;
            Unloaded += SpecsPage_Unloaded;
        }

        /// <summary>
        /// Creates a fallback ViewModel that displays an error message when initialization fails.
        /// </summary>
        private static SpecsPageViewModel CreateFallbackViewModel(Exception exception)
        {
            // Use the service locator to get a minimal instance
            var fallback = new SpecsPageViewModel(
                App._host.Services.GetRequiredService<ISystemInformationService>());

            fallback.HasError = true;
            fallback.ErrorMessage = exception is TypeInitializationException or DllNotFoundException or BadImageFormatException
                ? $"A required component could not be loaded. This may be caused by missing or incompatible system drivers. Details: {exception.GetBaseException().Message}"
                : $"Failed to initialize system specifications: {exception.Message}";
            fallback.IsLoading = false;

            return fallback;
        }

        private void CpuDetailsExpander_Expanded(object sender, EventArgs args)
        {
            _isCpuDetailsExpanded = true;
            StartCpuMonitoring();
        }

        private void CpuDetailsExpander_Collapsed(object sender, EventArgs args)
        {
            _isCpuDetailsExpanded = false;
            StopCpuMonitoring();
        }
        private void MotherboardDetailsExpander_Expanded(object sender, EventArgs args)
        {
            _ = _viewModel.LoadMotherboardDetailsAsync(_lifetimeCancellation.Token);
        }
        private void SpecsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            _ = LoadSpecsAsync(_lifetimeCancellation.Token);
            if (_isCpuDetailsExpanded)
            {
                StartCpuMonitoring();
            }
        }

        private void SpecsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopCpuMonitoring();
            _lifetimeCancellation.Cancel();
        }

        private async Task LoadSpecsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _viewModel.LoadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when navigating away
            }
            catch (Exception exception) when (
                exception is TypeInitializationException or
                DllNotFoundException or
                BadImageFormatException or
                System.Runtime.InteropServices.SEHException)
            {
                // Native DLL loading failures
                App.logger.Error(exception, "[Specs] Native component failure during load.");
                _viewModel.HasError = true;
                _viewModel.ErrorMessage = $"A system component could not be loaded. This may be caused by missing or incompatible drivers. Details: {exception.GetBaseException().Message}";
                _viewModel.IsLoading = false;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Specs] Unexpected error during load.");
                _viewModel.HasError = true;
                _viewModel.ErrorMessage = $"Failed to load system specifications: {exception.Message}";
                _viewModel.IsLoading = false;
            }
        }

        private void StartCpuMonitoring()
        {
            StopCpuMonitoring();
            _cpuMonitoringCancellation = new CancellationTokenSource();
            _ = _viewModel.RefreshCpuLiveMetricsAsync(_cpuMonitoringCancellation.Token);
            _cpuMonitoringTimer.Start();
        }

        private void StopCpuMonitoring()
        {
            _cpuMonitoringTimer.Stop();
            _cpuMonitoringCancellation?.Cancel();
            _cpuMonitoringCancellation?.Dispose();
            _cpuMonitoringCancellation = null;
        }

        private void CpuMonitoringTimer_Tick(object? sender, object e)
        {
            if (_cpuMonitoringCancellation is { } source)
                _ = _viewModel.RefreshCpuLiveMetricsAsync(source.Token);
        }
    }
}
