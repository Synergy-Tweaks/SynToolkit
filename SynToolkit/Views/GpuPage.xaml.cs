#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SynToolkit.Models.GpuDrivers;
using SynToolkit.Services.GpuDrivers;
using SynToolkit.Services.NvidiaProfileInspector;
using SynToolkit.Utils;
using SynToolkit.ViewModels;

namespace SynToolkit.Views
{
    public sealed partial class GpuPage : Page
    {
        private const string NipFileFilter = "NVIDIA Profile Inspector profiles (*.nip)|*.nip|All files (*.*)|*.*";
        private const string AmdInstallerFilter = "AMD Adrenalin installer (*.exe)|*.exe|All files (*.*)|*.*";
        private readonly GpuPageViewModel _viewModel;
        private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;
        private bool _isViewModelPropertyChangedSubscribed;
        private int _currentTabIndex;
        private GpuPageUiRestoreState? _pendingRestore;

        public GpuPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<GpuPageViewModel>();
            DataContext = _viewModel;
            _viewModelPropertyChangedHandler = ViewModel_PropertyChanged;
            Loaded += GpuPage_Loaded;
            Unloaded += GpuPage_Unloaded;
            SwitchVendorTab(0);
            UpdateVendorPanelVisibility();
            RefreshDebloatLists();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is GpuPageUiRestoreState state)
            {
                _pendingRestore = state;
            }
        }

        private async void GpuPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isViewModelPropertyChangedSubscribed)
            {
                _viewModel.PropertyChanged += _viewModelPropertyChangedHandler;
                _isViewModelPropertyChangedSubscribed = true;
            }

            BundledProfilesComboBox.PlaceholderText = App.GetValueFromItemList("GpuPage_NoBundledProfiles");

            await _viewModel.LoadGpuDevicesAsync();

            if (_pendingRestore is not null)
            {
                GpuPageUiRestoreState restore = _pendingRestore;
                _pendingRestore = null;
                await _viewModel.RestoreUiStateAsync(restore);
                SwitchVendorTab(restore.TabIndex);
            }

            UpdateVendorPanelVisibility();
            RefreshDebloatLists();
        }

        private void GpuPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isViewModelPropertyChangedSubscribed)
            {
                _viewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
                _isViewModelPropertyChangedSubscribed = false;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GpuPageViewModel.SelectedVendor) ||
                e.PropertyName == nameof(GpuPageViewModel.SelectedGpuDevice))
            {
                UpdateVendorPanelVisibility();
            }
            else if (e.PropertyName == nameof(GpuPageViewModel.PreparedPackage) ||
                     e.PropertyName == nameof(GpuPageViewModel.SelectedDebloatModeText))
            {
                RefreshDebloatLists();
            }
        }

        private void UpdateVendorPanelVisibility()
        {
            bool showWorkspace = _viewModel.SelectedVendor != GpuVendorSelection.None;
            VendorLandingPanel.Visibility = showWorkspace ? Visibility.Collapsed : Visibility.Visible;
            VendorWorkspacePanel.Visibility = showWorkspace ? Visibility.Visible : Visibility.Collapsed;
            NvidiaOverviewExtrasPanel.Visibility = _viewModel.IsNvidiaVendorSelected ? Visibility.Visible : Visibility.Collapsed;
            AmdManualImportCard.Visibility = _viewModel.IsAmdVendorSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SwitchVendorTab(int tabIndex)
        {
            // Shared indices for AMD and NVIDIA: Overview=0, Debloat=1, Removal=2
            _currentTabIndex = tabIndex;
            OverviewPanel.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            DebloatPanel.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            RemovalPanel.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

            Style activeStyle = (Style)Resources["BreadcrumbTabActive"];
            Style inactiveStyle = (Style)Resources["BreadcrumbTabInactive"];
            OverviewTabButton.Style = tabIndex == 0 ? activeStyle : inactiveStyle;
            DebloatTabButton.Style = tabIndex == 1 ? activeStyle : inactiveStyle;
            RemovalTabButton.Style = tabIndex == 2 ? activeStyle : inactiveStyle;
        }

        private async Task EnterVendorWorkspaceAsync(GpuVendorSelection vendor)
        {
            _viewModel.SelectVendor(vendor);
            UpdateVendorPanelVisibility();
            SwitchVendorTab(0);
            await _viewModel.LoadDriversAsync();
        }

        private async void AmdVendorCard_Click(object sender, RoutedEventArgs e) => await EnterVendorWorkspaceAsync(GpuVendorSelection.AMD);
        private async void NvidiaVendorCard_Click(object sender, RoutedEventArgs e) => await EnterVendorWorkspaceAsync(GpuVendorSelection.NVIDIA);

        private void BackToLandingPage_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ReturnToLandingPage();
            UpdateVendorPanelVisibility();
        }

        private void OverviewTab_Click(object sender, RoutedEventArgs e) => SwitchVendorTab(0);
        private void DebloatTab_Click(object sender, RoutedEventArgs e) => SwitchVendorTab(1);
        private void RemovalTab_Click(object sender, RoutedEventArgs e) => SwitchVendorTab(2);

        private async void GpuDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel.SelectedVendor == GpuVendorSelection.None)
            {
                return;
            }

            await _viewModel.LoadDriversAsync();
        }

        private async void RefreshDriversButton_Click(object sender, RoutedEventArgs e) => await _viewModel.LoadDriversAsync();

        private async void PrepareDriverButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.PrepareSelectedDriverAsync();
            RefreshDebloatLists();
            SwitchVendorTab(1);
        }

        private async void InstallPreparedDriverButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.LaunchPreparedInstallAsync();
            if (!_viewModel.HasError)
            {
                ArmUiRecoveryAfterDriverInstall();
            }
        }

        private async void ReloadPreparedPackageButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.ReloadPreparedPackageAsync();
            RefreshDebloatLists();
        }

        private async void ImportAmdInstallerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? filePath = NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, AmdInstallerFilter);
            if (filePath is null)
            {
                return;
            }

            await _viewModel.ImportAmdInstallerAsync(filePath);
            if (_viewModel.HasError)
            {
                return;
            }

            RefreshDebloatLists();
            SwitchVendorTab(1);
        }

        private async void ImportAmdFolderMenuItem_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? folderPath = NativeFileDialogHelper.ShowFolderBrowserDialog(
                windowHandle,
                App.GetValueFromItemList("GpuPage_ImportFolderDialogDescription"));
            if (folderPath is null)
            {
                return;
            }

            await _viewModel.ImportAmdExtractedFolderAsync(folderPath);
            if (_viewModel.HasError)
            {
                return;
            }

            RefreshDebloatLists();
            SwitchVendorTab(1);
        }

        private void ArmUiRecoveryAfterDriverInstall()
        {
            GpuUiRecoveryCoordinator.Arm(
                DispatcherQueue,
                captureState: () =>
                {
                    if (_viewModel.SelectedVendor == GpuVendorSelection.None)
                        return null;

                    return _viewModel.CaptureUiState(_currentTabIndex);
                },
                rebuild: state =>
                {
                    if (App.m_window is MainWindow mainWindow)
                        mainWindow.ForceRebuildGpuPage(state);
                });
        }

        private void DebloatModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.RefreshDebloatComponentsForSelectedMode();
            RefreshDebloatLists();
        }

        private void RefreshDebloatLists()
        {
            List<GpuPackageComponent> components = _viewModel.DebloatComponents.ToList();
            PackageComponentsItemsControl.ItemsSource = components.Where(component => component.Kind == GpuPackageComponentKind.Package).ToList();
            ScheduledTaskComponentsItemsControl.ItemsSource = components.Where(component => component.Kind == GpuPackageComponentKind.ScheduledTask).ToList();
            DisplayComponentsItemsControl.ItemsSource = components.Where(component => component.Kind == GpuPackageComponentKind.DisplayDriver).ToList();
            FolderComponentsItemsControl.ItemsSource = components.Where(component => component.Kind == GpuPackageComponentKind.Folder).ToList();
        }

        private async void RunRemovalButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.RunRemovalAsync();
            if (!_viewModel.HasCompletedRemoval)
            {
                return;
            }

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = App.GetValueFromItemList("GpuPage_RebootDialogTitle"),
                Content = App.GetValueFromItemList("GpuPage_RebootDialogContent"),
                PrimaryButtonText = App.GetValueFromItemList("GpuPage_RebootNow"),
                CloseButtonText = App.GetValueFromItemList("GpuPage_RebootDialogLater"),
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await _viewModel.RebootAfterRemovalAsync();
            }
        }

        private async void RebootNowButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.RebootAfterRemovalAsync();
        }

        private async void BrowseNipFileButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? filePath = NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, NipFileFilter);
            if (filePath is not null)
            {
                await _viewModel.LoadNipFileAsync(filePath);
            }
        }

        private async void ApplyNvidiaProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = App.GetValueFromItemList("GpuPage_ApplyProfilesDialogTitle"),
                Content = App.GetValueFromItemList("GpuPage_ApplyProfilesDialogContent"),
                PrimaryButtonText = App.GetValueFromItemList("GpuPage_ApplyProfilesDialogPrimary"),
                CloseButtonText = App.GetValueFromItemList("GpuPage_ApplyProfilesDialogClose"),
                DefaultButton = ContentDialogButton.Close
            };

            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await _viewModel.ApplyNvidiaProfilesAsync();
        }

        private async void ExportLoadedProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string suggestedFileName = string.IsNullOrWhiteSpace(_viewModel.NipFilePath) ? "NVIDIA Profiles.nip" : System.IO.Path.GetFileName(_viewModel.NipFilePath);
            string? filePath = NativeFileDialogHelper.ShowSaveFileDialog(windowHandle, NipFileFilter, suggestedFileName);
            if (filePath is not null)
            {
                await _viewModel.ExportLoadedProfilesAsync(filePath);
            }
        }

        private async void LoadBundledProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (BundledProfilesComboBox.SelectedItem is BundledNvidiaProfileFile selected)
            {
                await _viewModel.LoadBundledProfileAsync(selected);
            }
        }

        private void AddNewSettingButton_Click(object sender, RoutedEventArgs e) => _viewModel.AddNewSettingRow();

        private void RemoveNewSettingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: NvidiaProfileSetting setting })
            {
                _viewModel.RemoveNewSettingRow(setting);
            }
        }

        private async void ExportNewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string suggestedFileName = string.IsNullOrWhiteSpace(_viewModel.NewProfileName) ? "Custom NVIDIA Profile.nip" : $"{_viewModel.NewProfileName}.nip";
            string? filePath = NativeFileDialogHelper.ShowSaveFileDialog(windowHandle, NipFileFilter, suggestedFileName);
            if (filePath is not null)
            {
                await _viewModel.ExportNewProfileAsync(filePath);
            }
        }
    }
}
