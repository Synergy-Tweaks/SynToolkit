#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Views
{
    internal sealed record PowerPlanChoice(string DisplayName, string? FilePath, bool IsBuiltIn = false);

    internal sealed class PowerSettingRowView
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string SettingId { get; init; }
        public required string CurrentAcLabel { get; init; }
        public required string CurrentDcLabel { get; init; }
        public required string SelectedAcLabel { get; init; }
        public required string SelectedDcLabel { get; init; }
        public Visibility CurrentVisibility { get; init; }
        public Visibility DifferenceVisibility { get; init; }
    }

    internal sealed class PowerSettingGroupView
    {
        public required string Name { get; init; }
        public required string CountLabel { get; init; }
        public required IReadOnlyList<PowerSettingRowView> Rows { get; init; }
    }

    internal sealed record PowerSettingCategoryView(string Name, int Count, bool IsAll = false);

    public sealed partial class PowerPlanSettingsDialog : ContentDialog
    {
        private readonly ObservableCollection<PowerPlanChoice> _choices = new();
        private readonly Guid? _currentSchemeId;
        private readonly string _currentSchemeName;
        private CancellationTokenSource? _readCancellation;
        private PowerPlanInspection? _inspection;
        private int _readVersion;
        private bool _isOpen;
        private bool _updatingCategories;

        public PowerPlanSettingsDialog(
            IReadOnlyList<BundledPowerPlan> bundledPlans,
            Guid? currentSchemeId,
            string currentSchemeName,
            string? initialFilePath = null,
            bool initialBuiltIn = false,
            bool startComparing = false)
        {
            InitializeComponent();
            bool lightTheme = Application.Current.RequestedTheme == ApplicationTheme.Light;
            Resources["ContentDialogSmokeFill"] = new AcrylicBrush
            {
                TintColor = lightTheme
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 246, 246, 246)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 32, 35, 43),
                TintOpacity = lightTheme ? 0.50 : 0.66,
                FallbackColor = lightTheme
                    ? Microsoft.UI.ColorHelper.FromArgb(184, 246, 246, 246)
                    : Microsoft.UI.ColorHelper.FromArgb(217, 32, 35, 43)
            };
            _currentSchemeId = currentSchemeId;
            _currentSchemeName = string.IsNullOrWhiteSpace(currentSchemeName)
                ? "current plan"
                : currentSchemeName;

            _choices.Add(new PowerPlanChoice("SynToolkit SOS Performance", null, IsBuiltIn: true));
            foreach (BundledPowerPlan plan in bundledPlans)
            {
                _choices.Add(new PowerPlanChoice(plan.DisplayName, plan.FilePath));
            }
            PlanPicker.ItemsSource = _choices;
            CompareToggle.IsEnabled = currentSchemeId.HasValue;
            CompareToggle.IsOn = startComparing && currentSchemeId.HasValue;
            DifferencesOnlyBox.Visibility = CompareToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

            if (initialBuiltIn)
            {
                PlanPicker.SelectedItem = _choices[0];
            }
            else if (initialFilePath is not null)
            {
                PowerPlanChoice? choice = _choices.FirstOrDefault(item =>
                    string.Equals(item.FilePath, initialFilePath, StringComparison.OrdinalIgnoreCase));
                if (choice is not null)
                {
                    PlanPicker.SelectedItem = choice;
                }
            }
        }

        private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            _isOpen = true;
            if (XamlRoot is not null)
            {
                DialogContent.Width = Math.Min(900, Math.Max(320, XamlRoot.Size.Width - 56));
                DialogContent.Height = Math.Min(640, Math.Max(340, XamlRoot.Size.Height - 120));
            }
            await LoadSelectionAsync();
            if (_isOpen)
            {
                if (PlanPicker.SelectedItem is null)
                {
                    PlanPicker.Focus(FocusState.Programmatic);
                }
                else
                {
                    SettingsSearchBox.Focus(FocusState.Programmatic);
                }
            }
        }

        private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            _isOpen = false;
            _readVersion++;
            _readCancellation?.Cancel();
        }

        private async void PlanPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isOpen)
            {
                await LoadSelectionAsync();
            }
        }

        private async void CompareToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isOpen)
            {
                return;
            }

            DifferencesOnlyBox.Visibility = CompareToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            if (!CompareToggle.IsOn)
            {
                DifferencesOnlyBox.IsChecked = false;
            }
            await LoadSelectionAsync();
        }

        private void DifferencesOnlyBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isOpen)
            {
                RenderSettings();
            }
        }

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isOpen)
            {
                if (!string.IsNullOrWhiteSpace(SettingsSearchBox.Text) && CategoryList.SelectedIndex > 0)
                {
                    CategoryList.SelectedIndex = 0;
                    return;
                }
                RenderSettings();
            }
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isOpen && !_updatingCategories)
            {
                RenderSettings();
            }
        }

        private void BrowsePowButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            string? path = NativeFileDialogHelper.ShowOpenFileDialog(
                windowHandle, "Windows power plan (*.pow)|*.pow");
            if (path is null)
            {
                return;
            }

            PowerPlanChoice? existing = _choices.FirstOrDefault(item =>
                string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new PowerPlanChoice(Path.GetFileNameWithoutExtension(path), path);
                _choices.Add(existing);
            }
            PlanPicker.SelectedItem = existing;
        }

        private async Task LoadSelectionAsync()
        {
            _readCancellation?.Cancel();
            int version = ++_readVersion;
            string? selectedCategory = (CategoryList.SelectedItem as PowerSettingCategoryView)?.Name;
            _inspection = null;
            SettingsGroupsList.ItemsSource = null;
            SettingsPane.Visibility = Visibility.Collapsed;
            ReadErrorBar.IsOpen = false;

            if (PlanPicker.SelectedItem is not PowerPlanChoice choice)
            {
                EmptyMessage.Text = "Choose a bundled plan or a .pow file to inspect.";
                EmptyMessage.Visibility = Visibility.Visible;
                ResultsText.Text = "Choose a plan to inspect.";
                return;
            }

            CancellationTokenSource source = new();
            _readCancellation = source;
            CancellationToken token = source.Token;
            Guid? currentId = CompareToggle.IsOn ? _currentSchemeId : null;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            EmptyMessage.Text = "Reading power settings…";
            EmptyMessage.Visibility = Visibility.Visible;
            ResultsText.Text = $"Reading {choice.DisplayName}…";

            try
            {
                PowerPlanInspection inspection = await Task.Run(() => choice.IsBuiltIn
                    ? PowerPlanSettingsReader.ReadBuiltIn(currentId, token)
                    : PowerPlanSettingsReader.ReadFile(choice.FilePath!, currentId, token), token);
                if (_isOpen && version == _readVersion && !token.IsCancellationRequested)
                {
                    _inspection = inspection;
                    PopulateCategories(selectedCategory);
                    RenderSettings();
                }
            }
            catch (OperationCanceledException)
            {
                // A different plan was selected or the dialog closed.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Unable to inspect power plan {PlanName}.", choice.DisplayName);
                if (_isOpen && version == _readVersion)
                {
                    ReadErrorBar.Message = exception.Message;
                    ReadErrorBar.IsOpen = true;
                    EmptyMessage.Text = "Choose another plan or try a different .pow file.";
                    ResultsText.Text = "Settings unavailable";
                }
            }
            finally
            {
                if (version == _readVersion)
                {
                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                    _readCancellation = null;
                }
                source.Dispose();
            }
        }

        private void PopulateCategories(string? selectedCategory)
        {
            if (_inspection is null)
            {
                return;
            }

            PowerSettingCategoryView[] categories =
            [
                new("All settings", _inspection.Settings.Count, IsAll: true),
                .. _inspection.Settings
                    .GroupBy(setting => setting.GroupName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(group => new PowerSettingCategoryView(group.Key, group.Count()))
            ];
            _updatingCategories = true;
            CategoryList.ItemsSource = categories;
            CategoryList.SelectedItem = categories.FirstOrDefault(category =>
                string.Equals(category.Name, selectedCategory, StringComparison.CurrentCultureIgnoreCase))
                ?? categories[0];
            _updatingCategories = false;
        }

        private void RenderSettings()
        {
            if (_inspection is null)
            {
                return;
            }

            bool comparing = CompareToggle.IsOn && _currentSchemeId.HasValue;
            bool differencesOnly = comparing && DifferencesOnlyBox.IsChecked == true;
            CurrentColumnHeader.Visibility = comparing ? Visibility.Visible : Visibility.Collapsed;
            SelectedColumnHeader.Text = comparing ? "Selected plan" : "Plan value";
            string query = SettingsSearchBox.Text.Trim();
            IEnumerable<PowerPlanSettingInspection> matches = _inspection.Settings;
            if (query.Length == 0 && CategoryList.SelectedItem is PowerSettingCategoryView { IsAll: false } category)
            {
                matches = matches.Where(setting =>
                    string.Equals(setting.GroupName, category.Name, StringComparison.CurrentCultureIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                matches = matches.Where(setting =>
                    setting.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    setting.GroupName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    setting.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    setting.SettingId.ToString("D").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    setting.SubgroupId.ToString("D").Contains(query, StringComparison.OrdinalIgnoreCase));
            }
            if (differencesOnly)
            {
                matches = matches.Where(setting => setting.IsDifferent);
            }

            PowerPlanSettingInspection[] visible = matches.ToArray();
            IReadOnlyList<PowerSettingGroupView> groups = visible
                .GroupBy(setting => setting.GroupName, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new PowerSettingGroupView
                {
                    Name = group.Key,
                    CountLabel = group.Count() == 1 ? "1 setting" : $"{group.Count()} settings",
                    Rows = group.Select(setting => new PowerSettingRowView
                    {
                        Name = setting.Name,
                        Description = setting.Description,
                        SettingId = setting.SettingId.ToString("D"),
                        CurrentAcLabel = $"AC  {setting.CurrentAcText}",
                        CurrentDcLabel = $"DC  {setting.CurrentDcText}",
                        SelectedAcLabel = $"AC  {setting.AcText}",
                        SelectedDcLabel = $"DC  {setting.DcText}",
                        CurrentVisibility = comparing ? Visibility.Visible : Visibility.Collapsed,
                        DifferenceVisibility = comparing && setting.IsDifferent
                            ? Visibility.Visible : Visibility.Collapsed
                    }).ToArray()
                }).ToArray();

            SettingsGroupsList.ItemsSource = groups;
            SettingsScrollViewer.ChangeView(null, 0, null);
            SettingsPane.Visibility = Visibility.Visible;
            SettingsScrollViewer.Visibility = visible.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoResultsMessage.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyMessage.Visibility = Visibility.Collapsed;

            int total = _inspection.Settings.Count;
            int changed = comparing ? _inspection.Settings.Count(setting => setting.IsDifferent) : 0;
            ResultsText.Text = comparing
                ? $"{visible.Length} of {total} settings · {changed} differ from {_currentSchemeName}"
                : $"{visible.Length} of {total} settings · {groups.Count} categories";
        }
    }
}
