#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class SpecsPage : Page
    {
        private readonly SpecsPageViewModel _viewModel;
        private readonly DispatcherTimer _cpuMonitoringTimer;
        private bool _isCpuDetailsExpanded;

        public SpecsPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<SpecsPageViewModel>();
            DataContext = _viewModel;

            _cpuMonitoringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
            _cpuMonitoringTimer.Tick += CpuMonitoringTimer_Tick;
            Loaded += SpecsPage_Loaded;
            Unloaded += SpecsPage_Unloaded;

            _ = _viewModel.LoadAsync();
        }

        private void CpuDetailsExpander_Expanded(object sender, EventArgs args)
        {
            _isCpuDetailsExpanded = true;
            _viewModel.RefreshCpuLiveMetrics();
            _cpuMonitoringTimer.Start();
        }

        private void CpuDetailsExpander_Collapsed(object sender, EventArgs args)
        {
            _isCpuDetailsExpanded = false;
            _cpuMonitoringTimer.Stop();
        }
        private void MotherboardDetailsExpander_Expanded(object sender, EventArgs args)
        {
            _ = _viewModel.LoadMotherboardDetailsAsync();
        }
        private void SpecsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isCpuDetailsExpanded)
            {
                _viewModel.RefreshCpuLiveMetrics();
                _cpuMonitoringTimer.Start();
            }
        }

        private void SpecsPage_Unloaded(object sender, RoutedEventArgs e) => _cpuMonitoringTimer.Stop();

        private void CpuMonitoringTimer_Tick(object? sender, object e) => _viewModel.RefreshCpuLiveMetrics();
    }
}