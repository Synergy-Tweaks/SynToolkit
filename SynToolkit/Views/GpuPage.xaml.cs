#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        private readonly GpuPageViewModel _viewModel;
        private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;
        private int _currentVendorTabIndex;
        private readonly Dictionary<string, NvidiaTweakRowView> _nvidiaTweakRows = new(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, NvidiaTweakValueSnapshot> _nvidiaTweakValues = new Dictionary<string, NvidiaTweakValueSnapshot>();
        private int _nvidiaTweakCheckVersion;
        private bool _nvidiaTweakCheckSucceeded;
        private bool _updatingNvidiaTweakRows;
        private bool _isViewModelPropertyChangedSubscribed;

        private sealed class NvidiaTweakRowView(TextBox valueBox, Button actionButton)
        {
            public TextBox ValueBox { get; } = valueBox;
            public Button ActionButton { get; } = actionButton;
            public string LoadedValueText { get; set; } = string.Empty;
        }

        public GpuPage()
        {
            InitializeComponent();
            _viewModel = App._host.Services.GetRequiredService<GpuPageViewModel>();
            DataContext = _viewModel;
            _viewModelPropertyChangedHandler = ViewModel_PropertyChanged;
            Loaded += GpuPage_Loaded;
            Unloaded += GpuPage_Unloaded;
            BuildNvidiaTweakLists();
            SwitchVendorTab(0);
            UpdateVendorPanelVisibility();
            RefreshDebloatLists();
        }

        private async void GpuPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isViewModelPropertyChangedSubscribed)
            {
                _viewModel.PropertyChanged += _viewModelPropertyChangedHandler;
                _isViewModelPropertyChangedSubscribed = true;
            }

            await _viewModel.LoadGpuDevicesAsync();
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
                _ = RefreshNvidiaTweaksIfNeededAsync();
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

            bool isNvidia = _viewModel.IsNvidiaVendorSelected;
            PerformanceTabButton.Visibility = isNvidia ? Visibility.Visible : Visibility.Collapsed;
            PerformanceTabChevron.Visibility = isNvidia ? Visibility.Visible : Visibility.Collapsed;
            NvidiaOverviewExtrasPanel.Visibility = isNvidia ? Visibility.Visible : Visibility.Collapsed;

            if (!isNvidia && _currentVendorTabIndex == 2)
            {
                SwitchVendorTab(0);
            }
        }

        private void SwitchVendorTab(int tabIndex)
        {
            _currentVendorTabIndex = tabIndex;
            bool isNvidia = _viewModel.IsNvidiaVendorSelected;
            OverviewPanel.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            DebloatPanel.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            PerformancePanel.Visibility = tabIndex == 2 && isNvidia ? Visibility.Visible : Visibility.Collapsed;
            RemovalPanel.Visibility = tabIndex == (isNvidia ? 3 : 2) ? Visibility.Visible : Visibility.Collapsed;

            Style activeStyle = (Style)Resources["BreadcrumbTabActive"];
            Style inactiveStyle = (Style)Resources["BreadcrumbTabInactive"];
            OverviewTabButton.Style = tabIndex == 0 ? activeStyle : inactiveStyle;
            DebloatTabButton.Style = tabIndex == 1 ? activeStyle : inactiveStyle;
            PerformanceTabButton.Style = tabIndex == 2 ? activeStyle : inactiveStyle;
            RemovalTabButton.Style = tabIndex == (isNvidia ? 3 : 2) ? activeStyle : inactiveStyle;
        }

        private async Task EnterVendorWorkspaceAsync(GpuVendorSelection vendor)
        {
            _viewModel.SelectVendor(vendor);
            UpdateVendorPanelVisibility();
            SwitchVendorTab(0);
            await _viewModel.LoadDriversAsync();
            await RefreshNvidiaTweaksIfNeededAsync();
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
        private async void PerformanceTab_Click(object sender, RoutedEventArgs e)
        {
            SwitchVendorTab(2);
            await RefreshNvidiaTweaksIfNeededAsync();
        }
        private void RemovalTab_Click(object sender, RoutedEventArgs e) => SwitchVendorTab(_viewModel.IsNvidiaVendorSelected ? 3 : 2);

        private async void GpuDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel.SelectedVendor == GpuVendorSelection.None)
            {
                return;
            }

            await _viewModel.LoadDriversAsync();
            await RefreshNvidiaTweaksIfNeededAsync();
        }

        private async void RefreshDriversButton_Click(object sender, RoutedEventArgs e) => await _viewModel.LoadDriversAsync();

        private async void PrepareDriverButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.PrepareSelectedDriverAsync();
            RefreshDebloatLists();
            SwitchVendorTab(1);
        }

        private async void InstallPreparedDriverButton_Click(object sender, RoutedEventArgs e) => await _viewModel.LaunchPreparedInstallAsync();

        private async void ReloadPreparedPackageButton_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.ReloadPreparedPackageAsync();
            RefreshDebloatLists();
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
                Title = "Reboot to finish GPU cleanup?",
                Content = "The removal workflow finished. Reboot Windows now to complete GPU driver cleanup.",
                PrimaryButtonText = "Reboot now",
                CloseButtonText = "Later",
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

        private async void RefreshNvidiaTweakStatesButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshNvidiaTweaksIfNeededAsync();
        }

        private async Task RefreshNvidiaTweaksIfNeededAsync()
        {
            if (_viewModel.SelectedGpuDevice is { IsNvidia: true } device)
            {
                await RefreshNvidiaTweakStatesAsync(device);
                return;
            }

            _nvidiaTweakCheckVersion++;
            _nvidiaTweakValues = new Dictionary<string, NvidiaTweakValueSnapshot>();
            _nvidiaTweakCheckSucceeded = false;
            UpdateNvidiaTweakActionAvailability();
        }

        private async void ApplyNvidiaTweakGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string groupName } ||
                !Enum.TryParse(groupName, ignoreCase: true, out NvidiaTweakGroup group) ||
                _viewModel.SelectedGpuDevice is not { IsNvidia: true } device)
            {
                return;
            }

            await ApplyNvidiaTweakAsync(device, () => NvidiaPerformanceTweaksService.ApplyGroupAsync(device, group), groupName);
        }

        private async void RevertNvidiaTweakGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string groupName } ||
                !Enum.TryParse(groupName, ignoreCase: true, out NvidiaTweakGroup group) ||
                _viewModel.SelectedGpuDevice is not { IsNvidia: true } device)
            {
                return;
            }

            await ApplyNvidiaTweakAsync(device, () => NvidiaPerformanceTweaksService.RemoveGroupAsync(device, group), groupName, true);
        }

        private async void ApplyNvidiaTweakItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string id } ||
                _viewModel.SelectedGpuDevice is not { IsNvidia: true } device ||
                !_nvidiaTweakRows.TryGetValue(id, out NvidiaTweakRowView? row))
            {
                return;
            }

            NvidiaTweakDefinition? definition = NvidiaPerformanceTweaksService.Definitions.FirstOrDefault(item => item.Id == id);
            if (definition is null)
            {
                return;
            }

            bool isApplied = _nvidiaTweakValues.TryGetValue(id, out NvidiaTweakValueSnapshot? value) && value.State == NvidiaTweakValueState.Applied;
            bool hasExistingValue = _nvidiaTweakValues.TryGetValue(id, out value) && value.State != NvidiaTweakValueState.NotSet;
            bool isDirty = definition.SupportsCustomValue && !string.Equals(row.ValueBox.Text.Trim(), row.LoadedValueText.Trim(), StringComparison.OrdinalIgnoreCase);
            bool shouldRemove = (!isDirty && isApplied) || (isDirty && hasExistingValue && string.IsNullOrWhiteSpace(row.ValueBox.Text));

            await ApplyNvidiaTweakAsync(
                device,
                () => shouldRemove
                    ? NvidiaPerformanceTweaksService.RemoveItemAsync(device, id)
                    : isDirty
                        ? NvidiaPerformanceTweaksService.SetItemValueAsync(device, id, row.ValueBox.Text)
                        : NvidiaPerformanceTweaksService.ApplyItemAsync(device, id),
                definition.Name,
                shouldRemove,
                id);
        }

        private async Task ApplyNvidiaTweakAsync(
            GpuDeviceInfo device,
            Func<Task<(bool Success, string Message)>> apply,
            string operationName,
            bool isRemoving = false,
            string? refreshItemId = null)
        {
            try
            {
                var result = await apply();
                if (!result.Success)
                {
                    _viewModel.StatusMessage = result.Message;
                    _viewModel.HasError = true;
                }
                else
                {
                    _viewModel.StatusMessage = isRemoving
                        ? $"Removed {operationName}. Restart Windows to finish applying changes."
                        : $"Applied {operationName}. Restart Windows to finish applying changes.";
                }

                if (refreshItemId is null)
                {
                    await RefreshNvidiaTweakStatesAsync(device);
                }
                else
                {
                    await RefreshNvidiaTweakStateAsync(device, refreshItemId);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] NVIDIA performance tweak update failed.");
                _viewModel.StatusMessage = exception.Message;
                _viewModel.HasError = true;
            }
        }

        private void BuildNvidiaTweakLists()
        {
            _nvidiaTweakRows.Clear();
            BuildNvidiaTweakList(NvidiaSettingsListPanel, NvidiaTweakGroup.NvidiaSettings);
            BuildNvidiaTweakList(NvidiaRmPowerFeatureListPanel, NvidiaTweakGroup.RmPowerFeature);
            BuildNvidiaTweakList(NvidiaTelemetryListPanel, NvidiaTweakGroup.Telemetry);
            BuildNvidiaTweakList(NvidiaEccListPanel, NvidiaTweakGroup.Ecc);
            BuildNvidiaTweakList(NvidiaHdcpListPanel, NvidiaTweakGroup.Hdcp);
        }

        private void BuildNvidiaTweakList(StackPanel panel, NvidiaTweakGroup group)
        {
            panel.Children.Clear();
            IReadOnlyList<NvidiaTweakDefinition> definitions = NvidiaPerformanceTweaksService.GetDefinitions(group);
            for (int index = 0; index < definitions.Count; index++)
            {
                NvidiaTweakDefinition definition = definitions[index];
                if (index > 0)
                {
                    panel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
                    });
                }

                Grid row = new() { Padding = new Thickness(0, 12, 0, 12), ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

                StackPanel labelStack = new() { Spacing = 1 };
                labelStack.Children.Add(new TextBlock { Text = definition.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                labelStack.Children.Add(new TextBlock { Text = definition.LocationText, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(labelStack);

                TextBlock recommendedValueText = new()
                {
                    Text = definition.RecommendedValueText,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(recommendedValueText, 1);
                row.Children.Add(recommendedValueText);

                TextBox valueBox = new()
                {
                    Tag = definition.Id,
                    PlaceholderText = "Not set",
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsReadOnly = !definition.SupportsCustomValue
                };
                valueBox.TextChanged += NvidiaTweakValueBox_TextChanged;
                Grid.SetColumn(valueBox, 2);
                row.Children.Add(valueBox);

                Button applyButton = new()
                {
                    Tag = definition.Id,
                    Content = new FontIcon { Glyph = "\uE73E", FontSize = 14 },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                applyButton.Click += ApplyNvidiaTweakItem_Click;
                Grid.SetColumn(applyButton, 3);
                row.Children.Add(applyButton);

                _nvidiaTweakRows[definition.Id] = new NvidiaTweakRowView(valueBox, applyButton);
                panel.Children.Add(row);
            }
        }

        private void NvidiaTweakValueBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingNvidiaTweakRows)
            {
                return;
            }

            if (sender is TextBox { Tag: string id })
            {
                UpdateNvidiaTweakActionAvailability(id);
            }
        }

        private async Task RefreshNvidiaTweakStatesAsync(GpuDeviceInfo device)
        {
            int requestVersion = ++_nvidiaTweakCheckVersion;
            foreach (NvidiaTweakRowView row in _nvidiaTweakRows.Values)
            {
                row.ValueBox.IsEnabled = false;
                row.ValueBox.PlaceholderText = "Checking...";
            }

            NvidiaTweakCheckResult result = await NvidiaPerformanceTweaksService.CheckAsync(device);
            if (requestVersion != _nvidiaTweakCheckVersion ||
                _viewModel.SelectedGpuDevice is not GpuDeviceInfo selectedDevice ||
                !string.Equals(selectedDevice.PnpDeviceId, device.PnpDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.Success)
            {
                _nvidiaTweakValues = new Dictionary<string, NvidiaTweakValueSnapshot>();
                _nvidiaTweakCheckSucceeded = false;
                _updatingNvidiaTweakRows = true;
                try
                {
                    foreach (NvidiaTweakRowView row in _nvidiaTweakRows.Values)
                    {
                        row.ValueBox.Text = string.Empty;
                        row.LoadedValueText = string.Empty;
                        row.ValueBox.PlaceholderText = "Unavailable";
                    }
                }
                finally
                {
                    _updatingNvidiaTweakRows = false;
                }
                UpdateNvidiaTweakActionAvailability();
                return;
            }

            _nvidiaTweakValues = result.Values;
            _nvidiaTweakCheckSucceeded = true;
            _updatingNvidiaTweakRows = true;
            foreach (NvidiaTweakDefinition definition in NvidiaPerformanceTweaksService.Definitions)
            {
                if (_nvidiaTweakRows.TryGetValue(definition.Id, out NvidiaTweakRowView? row) &&
                    result.Values.TryGetValue(definition.Id, out NvidiaTweakValueSnapshot? value))
                {
                    string displayedValue = value.State == NvidiaTweakValueState.NotSet ? string.Empty : value.CurrentValueText;
                    row.ValueBox.Text = displayedValue;
                    row.LoadedValueText = displayedValue;
                    row.ValueBox.PlaceholderText = "Not set";
                }
            }
            _updatingNvidiaTweakRows = false;
            UpdateNvidiaTweakActionAvailability();
        }

        private async Task RefreshNvidiaTweakStateAsync(GpuDeviceInfo device, string id)
        {
            int requestVersion = ++_nvidiaTweakCheckVersion;
            if (!_nvidiaTweakRows.TryGetValue(id, out NvidiaTweakRowView? row))
            {
                return;
            }

            row.ValueBox.IsEnabled = false;
            row.ValueBox.PlaceholderText = "Checking...";

            var result = await NvidiaPerformanceTweaksService.CheckItemAsync(device, id);
            if (requestVersion != _nvidiaTweakCheckVersion ||
                _viewModel.SelectedGpuDevice is not GpuDeviceInfo selectedDevice ||
                !string.Equals(selectedDevice.PnpDeviceId, device.PnpDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.Success || result.Value is null)
            {
                row.ValueBox.PlaceholderText = "Unavailable";
                UpdateNvidiaTweakActionAvailability();
                return;
            }

            _nvidiaTweakValues = new Dictionary<string, NvidiaTweakValueSnapshot>(_nvidiaTweakValues, StringComparer.Ordinal)
            {
                [id] = result.Value
            };

            string displayedValue = result.Value.State == NvidiaTweakValueState.NotSet ? string.Empty : result.Value.CurrentValueText;
            _updatingNvidiaTweakRows = true;
            try
            {
                row.ValueBox.Text = displayedValue;
                row.LoadedValueText = displayedValue;
                row.ValueBox.PlaceholderText = "Not set";
            }
            finally
            {
                _updatingNvidiaTweakRows = false;
            }

            UpdateNvidiaTweakActionAvailability();
        }

        private void UpdateNvidiaTweakActionAvailability(string? changedId = null)
        {
            bool canApply = _viewModel.SelectedGpuDevice is { IsNvidia: true } && _nvidiaTweakCheckSucceeded;
            IEnumerable<NvidiaTweakDefinition> definitions = changedId is null
                ? NvidiaPerformanceTweaksService.Definitions
                : NvidiaPerformanceTweaksService.Definitions.Where(definition => definition.Id == changedId);

            foreach (NvidiaTweakDefinition definition in definitions)
            {
                if (!_nvidiaTweakRows.TryGetValue(definition.Id, out NvidiaTweakRowView? row))
                {
                    continue;
                }

                bool isApplied = _nvidiaTweakValues.TryGetValue(definition.Id, out NvidiaTweakValueSnapshot? value) && value.State == NvidiaTweakValueState.Applied;
                bool hasExistingValue = _nvidiaTweakValues.TryGetValue(definition.Id, out value) && value.State != NvidiaTweakValueState.NotSet;
                bool isDirty = definition.SupportsCustomValue && !string.Equals(row.ValueBox.Text.Trim(), row.LoadedValueText.Trim(), StringComparison.OrdinalIgnoreCase);
                bool showRemove = (!isDirty && isApplied) || (isDirty && hasExistingValue && string.IsNullOrWhiteSpace(row.ValueBox.Text));

                row.ValueBox.IsEnabled = canApply;
                row.ActionButton.IsEnabled = canApply;
                row.ActionButton.Content = new FontIcon { Glyph = showRemove ? "\uE74D" : "\uE73E", FontSize = 14 };
            }

            if (changedId is not null)
            {
                return;
            }

            foreach (NvidiaTweakGroup group in Enum.GetValues<NvidiaTweakGroup>())
            {
                bool hasPending = NvidiaPerformanceTweaksService.GetDefinitions(group).Any(definition => !_nvidiaTweakValues.TryGetValue(definition.Id, out NvidiaTweakValueSnapshot? value) || value.State != NvidiaTweakValueState.Applied);
                bool hasExistingValues = NvidiaPerformanceTweaksService.GetDefinitions(group).Any(definition => _nvidiaTweakValues.TryGetValue(definition.Id, out NvidiaTweakValueSnapshot? value) && value.State != NvidiaTweakValueState.NotSet);
                var buttons = GetNvidiaGroupButtons(group);
                buttons.Apply.IsEnabled = canApply && hasPending;
                buttons.Revert.IsEnabled = canApply && hasExistingValues;
            }
        }

        private (Button Apply, Button Revert) GetNvidiaGroupButtons(NvidiaTweakGroup group) => group switch
        {
            NvidiaTweakGroup.NvidiaSettings => (ApplyNvidiaSettingsGroupButton, RevertNvidiaSettingsGroupButton),
            NvidiaTweakGroup.RmPowerFeature => (ApplyNvidiaRmPowerFeatureGroupButton, RevertNvidiaRmPowerFeatureGroupButton),
            NvidiaTweakGroup.Telemetry => (ApplyNvidiaTelemetryGroupButton, RevertNvidiaTelemetryGroupButton),
            NvidiaTweakGroup.Ecc => (ApplyNvidiaEccGroupButton, RevertNvidiaEccGroupButton),
            _ => (ApplyNvidiaHdcpGroupButton, RevertNvidiaHdcpGroupButton)
        };

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
                Title = "Apply profiles to the NVIDIA driver?",
                Content = "This writes the profiles above directly to your NVIDIA driver settings, creating any profiles that do not already exist.",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
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
            string suggestedFileName = string.IsNullOrWhiteSpace(_viewModel.NewProfileName) ? "New Profile.nip" : $"{_viewModel.NewProfileName.Trim()}.nip";
            string? filePath = NativeFileDialogHelper.ShowSaveFileDialog(windowHandle, NipFileFilter, suggestedFileName);
            if (filePath is not null)
            {
                await _viewModel.ExportNewProfileAsync(filePath);
            }
        }
    }
}
