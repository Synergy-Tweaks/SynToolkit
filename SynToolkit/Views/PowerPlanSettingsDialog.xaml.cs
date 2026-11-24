#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
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

    internal sealed class PowerSettingRowView : PowerSettingListRow
    {
        public required PowerPlanSettingInspection Setting { get; init; }
        public override string GroupName => Setting.GroupName;
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string SettingId { get; init; }
        public required string CurrentAcLabel { get; init; }
        public required string CurrentDcLabel { get; init; }
        public required string SelectedAcLabel { get; init; }
        public required string SelectedDcLabel { get; init; }
        private Visibility _currentVisibility;
        private Visibility _differenceVisibility;
        public Visibility CurrentVisibility
        {
            get => _currentVisibility;
            set => SetProperty(ref _currentVisibility, value);
        }
        public Visibility DifferenceVisibility
        {
            get => _differenceVisibility;
            set => SetProperty(ref _differenceVisibility, value);
        }
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
        private PowerSettingRowView[] _rows = Array.Empty<PowerSettingRowView>();
        private readonly SemaphoreSlim _readGate = new(1, 1);
        private readonly DispatcherQueueTimer _searchTimer;

        public PowerPlanSettingsDialog(
            IReadOnlyList<BundledPowerPlan> bundledPlans,
            Guid? currentSchemeId,
            string currentSchemeName,
            string? initialFilePath = null,
            bool initialBuiltIn = false,
            bool startComparing = false)
        {
            InitializeComponent();
            _searchTimer = DispatcherQueue.CreateTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchTimer.IsRepeating = false;
            _searchTimer.Tick += SearchTimer_Tick;
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
                // Grow with the maximized window, keep the old size when windowed.
                // The window runs 1250 DIP wide by default, so the size fallback also
                // catches windows maximized before WinUI updated the presenter state.
                bool maximized = (App.m_window?.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.State
                    == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized
                    || XamlRoot.Size.Width >= 1300;
                double maxWidth = maximized ? 1360 : 900;
                double maxHeight = maximized ? 900 : 640;
                DialogContent.Width = Math.Min(maxWidth, Math.Max(320, XamlRoot.Size.Width - 56));
                DialogContent.Height = Math.Min(maxHeight, Math.Max(340, XamlRoot.Size.Height - 120));
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
            _readCancellation = null;
            _searchTimer.Stop();
            _searchTimer.Tick -= SearchTimer_Tick;
            SettingsRowsList.ItemsSource = null;
            CategoryList.ItemsSource = null;
            PlanPicker.ItemsSource = null;
            _inspection = null;
            _rows = Array.Empty<PowerSettingRowView>();
        }

        private void CloseViewerButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private async void PlanPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isOpen)
            {
                await LoadSelectionAsync();
            }
        }

        private void CompareToggle_Toggled(object sender, RoutedEventArgs e)
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
            RenderSettings();
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
                    _updatingCategories = true;
                    CategoryList.SelectedIndex = 0;
                    _updatingCategories = false;
                }
                _searchTimer.Stop();
                _searchTimer.Start();
            }
        }

        private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isOpen && !_updatingCategories)
            {
                RenderSettings();
            }
        }

        private void SearchTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (_isOpen) RenderSettings();
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
            _rows = Array.Empty<PowerSettingRowView>();
            SettingsRowsList.ItemsSource = null;
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
            Guid? currentId = _currentSchemeId;
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            EmptyMessage.Text = "Reading power settings…";
            EmptyMessage.Visibility = Visibility.Visible;
            ResultsText.Text = $"Reading {choice.DisplayName}…";

            try
            {
                // Cancel obsolete reads and serialize them so rapid selection cannot
                // accumulate simultaneous registry hives or enumeration buffers.
                await _readGate.WaitAsync(token);
                PowerPlanInspection inspection;
                PowerSettingRowView[] rows;
                try
                {
                    (inspection, rows) = await Task.Run(() =>
                    {
                        PowerPlanInspection read = choice.IsBuiltIn
                            ? PowerPlanSettingsReader.ReadBuiltIn(currentId, token)
                            : PowerPlanSettingsReader.ReadFile(choice.FilePath!, currentId, token);
                        PowerSettingRowView[] views = read.Settings.Select(setting => new PowerSettingRowView
                        {
                            Setting = setting,
                            Name = setting.Name,
                            Description = setting.Description,
                            SettingId = setting.SettingId.ToString("D"),
                            CurrentAcLabel = $"AC  {setting.CurrentAcText}",
                            CurrentDcLabel = $"DC  {setting.CurrentDcText}",
                            SelectedAcLabel = $"AC  {setting.AcText}",
                            SelectedDcLabel = $"DC  {setting.DcText}"
                        }).ToArray();
                        return (read, views);
                    }, token);
                }
                finally { _readGate.Release(); }
                if (_isOpen && version == _readVersion && !token.IsCancellationRequested)
                {
                    _inspection = inspection;
                    _rows = rows;
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
            _searchTimer.Stop();
            if (_inspection is null)
            {
                return;
            }

            bool comparing = CompareToggle.IsOn && _currentSchemeId.HasValue;
            bool differencesOnly = comparing && DifferencesOnlyBox.IsChecked == true;
            CurrentColumnHeader.Visibility = comparing ? Visibility.Visible : Visibility.Collapsed;
            SelectedColumnHeader.Text = comparing ? "Selected plan" : "Plan value";
            string query = SettingsSearchBox.Text.Trim();
            IEnumerable<PowerSettingRowView> matches = _rows;
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
                    setting.SettingId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    setting.Setting.SubgroupId.ToString("D").Contains(query, StringComparison.OrdinalIgnoreCase));
            }
            if (differencesOnly)
            {
                matches = matches.Where(row => row.Setting.IsDifferent);
            }

            PowerSettingRowView[] visible = matches.ToArray();
            foreach (PowerSettingRowView row in visible)
            {
                row.CurrentVisibility = comparing ? Visibility.Visible : Visibility.Collapsed;
                row.DifferenceVisibility = comparing && row.Setting.IsDifferent
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            PowerSettingListRow.UpdateHeaders(visible);
            SettingsRowsList.ItemsSource = visible;
            if (visible.Length > 0) SettingsRowsList.ScrollIntoView(visible[0]);
            SettingsPane.Visibility = Visibility.Visible;
            SettingsRowsList.Visibility = visible.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoResultsMessage.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyMessage.Visibility = Visibility.Collapsed;

            int total = _inspection.Settings.Count;
            int changed = comparing ? _inspection.Settings.Count(setting => setting.IsDifferent) : 0;
            int unspecified = _inspection.Settings.Count(setting => setting.IsAbsentFromFile);
            ResultsText.Text = comparing
                ? $"{visible.Length} of {total} settings · {changed} differ from {_currentSchemeName} · {unspecified} not in .pow"
                : $"{visible.Length} of {total} settings · {visible.Select(row => row.GroupName).Distinct().Count()} categories · {unspecified} not in .pow";
        }
    }
}
