#nullable enable

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Services;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Views
{
    internal sealed class BundledPowerPlanItem
    {
        public required BundledPowerPlan Plan { get; init; }
        public bool IsActive { get; init; }
        public bool IsApplying { get; init; }
        public bool IsApplyButtonEnabled { get; init; }

        public string DisplayName => Plan.DisplayName;
        public string Description => Plan.Description;

        public Brush BorderBrush => IsActive
            ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        public Thickness BorderThickness => IsActive ? new Thickness(2) : new Thickness(1);

        public string ApplyButtonLabel => IsApplying
            ? "Activating…"
            : "Activate";
    }

    public sealed partial class PowerPlansPage : Page
    {
        private readonly PowerPlanService _powerPlanService = new();
        private CancellationTokenSource _lifetimeCancellation = new();
        private PowerPlanSnapshot? _snapshot;
        private bool _isBusy = true;
        private bool _isPageLoaded;
        private bool _hasLoadedStatus;
        private int _lifetimeVersion;
        private IReadOnlyList<BundledPowerPlan> _allBundledPlans = Array.Empty<BundledPowerPlan>();
        private readonly Dictionary<string, Guid> _bundledPlanSchemeIds = new(StringComparer.OrdinalIgnoreCase);
        private string? _applyingBundledPlanFilePath;
        private bool _isBundledPlanOperationInProgress;

        public PowerPlansPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            LoadBundledPlans();
        }

        private void LoadBundledPlans()
        {
            _allBundledPlans = _powerPlanService.GetBundledPlans();
            ApplyBundledPlansFilter();
        }

        private void BundledPlansSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ApplyBundledPlansFilter();
            }
        }

        private void ApplyBundledPlansFilter()
        {
            string query = BundledPlansSearchBox?.Text?.Trim() ?? string.Empty;
            IReadOnlyList<BundledPowerPlan> matches = FilterBundledPlans(_allBundledPlans, query);

            BundledPlansListView.ItemsSource = matches
                .Select(CreateBundledPlanItem)
                .ToList();

            bool folderEmpty = _allBundledPlans.Count == 0;
            bool noMatches = !folderEmpty && matches.Count == 0;

            BundledPlansEmptyState.Visibility = folderEmpty ? Visibility.Visible : Visibility.Collapsed;
            BundledPlansNoMatchesState.Visibility = noMatches ? Visibility.Visible : Visibility.Collapsed;
            BundledPlansListView.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private BundledPowerPlanItem CreateBundledPlanItem(BundledPowerPlan plan)
        {
            bool isActive = IsBundledPlanActive(plan);
            bool isApplying = string.Equals(
                _applyingBundledPlanFilePath,
                plan.FilePath,
                StringComparison.OrdinalIgnoreCase);
            bool canMutate = !_isBusy &&
                !_isBundledPlanOperationInProgress &&
                _snapshot is not null &&
                _powerPlanService.CanMutatePowerPlans;

            return new BundledPowerPlanItem
            {
                Plan = plan,
                IsActive = isActive,
                IsApplying = isApplying,
                IsApplyButtonEnabled = canMutate && !isActive && !isApplying
            };
        }

        private bool IsBundledPlanActive(BundledPowerPlan plan)
        {
            if (_snapshot?.ActiveSchemeId is not Guid activeSchemeId)
            {
                return false;
            }

            if (_bundledPlanSchemeIds.TryGetValue(plan.FilePath, out Guid storedSchemeId))
            {
                return activeSchemeId == storedSchemeId;
            }

            if (!IsBundledPlanMatchingActiveScheme(plan, _snapshot.ActiveSchemeName))
            {
                return false;
            }

            _bundledPlanSchemeIds[plan.FilePath] = activeSchemeId;
            return true;
        }

        private static bool IsBundledPlanMatchingActiveScheme(BundledPowerPlan plan, string activeSchemeName)
        {
            if (string.IsNullOrWhiteSpace(activeSchemeName))
            {
                return false;
            }

            return string.Equals(plan.DisplayName, activeSchemeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(plan.FileName), activeSchemeName, StringComparison.OrdinalIgnoreCase) ||
                plan.ActiveSchemeNameHints.Any(hint =>
                    activeSchemeName.Contains(hint, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyActivePlanState(Guid schemeId, string schemeName)
        {
            _snapshot = _snapshot is not null
                ? _snapshot with
                {
                    ActiveSchemeId = schemeId,
                    ActiveSchemeName = schemeName
                }
                : new PowerPlanSnapshot(
                    schemeId,
                    schemeName,
                    false,
                    false,
                    false,
                    null,
                    null);

            CurrentPlanName.Text = schemeName;
            CurrentPlanId.Text = schemeId.ToString("D");
            ApplyBundledPlansFilter();
        }

        internal static IReadOnlyList<BundledPowerPlan> FilterBundledPlans(
            IReadOnlyList<BundledPowerPlan> plans,
            string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return plans;
            }

            return plans
                .Where(plan =>
                    plan.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    plan.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void RefreshBundledPlansButton_Click(object sender, RoutedEventArgs e)
        {
            LoadBundledPlans();
        }

        private async void ExploreBuiltInButton_Click(object sender, RoutedEventArgs e) =>
            await ShowPowerPlanSettingsDialogAsync(initialBuiltIn: true);

        private async void ExploreBundledPlanButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: BundledPowerPlanItem item })
            {
                await ShowPowerPlanSettingsDialogAsync(initialFilePath: item.Plan.FilePath);
            }
        }

        private async void ComparePlansButton_Click(object sender, RoutedEventArgs e) =>
            await ShowPowerPlanSettingsDialogAsync(startComparing: true);

        private async Task ShowPowerPlanSettingsDialogAsync(
            string? initialFilePath = null,
            bool initialBuiltIn = false,
            bool startComparing = false)
        {
            if (!_isPageLoaded)
            {
                return;
            }

            PowerPlanSettingsDialog dialog = new(
                _allBundledPlans,
                _snapshot?.ActiveSchemeId,
                _snapshot?.ActiveSchemeName ?? string.Empty,
                initialFilePath,
                initialBuiltIn,
                startComparing)
            {
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };
            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Power settings viewer could not be opened.");
                if (_isPageLoaded)
                {
                    ShowResult("Viewer unavailable", exception.Message, InfoBarSeverity.Error);
                }
            }
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            ElevationInfoBar.IsOpen = !_powerPlanService.CanMutatePowerPlans;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            _lifetimeCancellation = new CancellationTokenSource();
            int lifetimeVersion = ++_lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;

            if (_hasLoadedStatus)
            {
                SetBusy(false);
                return;
            }

            SetBusy(true);
            try
            {
                await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: true);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    _hasLoadedStatus = true;
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    SetBusy(false);
                }
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = false;
            _lifetimeVersion++;
            _lifetimeCancellation.Cancel();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            try
            {
                await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: true);
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    SetBusy(false);
                }
            }
        }

        private async void ImportBuiltInButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.ImportBuiltInPlanAsync(token),
                "SynToolkit SOS Performance was imported and activated.");

        private async void ActivateBuiltInButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.ActivateSynToolkitPlanAsync(token),
                "SynToolkit SOS Performance is now active.");

        private async void RestorePreviousButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.RestorePreviousPlanAsync(token),
                "The previous power plan was restored.");

        private async void ActivateBalancedButton_Click(object sender, RoutedEventArgs e) =>
            await RunOperationAsync(
                token => _powerPlanService.ActivateBalancedPlanAsync(token),
                "Windows Balanced is now active.");

        private async void RemoveBuiltInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Remove SynToolkit SOS Performance?",
                Content = "If the plan is active, SynToolkit will restore your previous plan first. No other power plan will be removed.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result;
            try
            {
                result = await confirmation.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "The remove-power-plan confirmation could not be displayed.");
                if (_isPageLoaded)
                {
                    ShowResult("Confirmation unavailable", exception.Message, InfoBarSeverity.Error);
                }
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (!_isPageLoaded)
            {
                return;
            }

            await RunOperationAsync(
                token => _powerPlanService.RemoveSynToolkitPlanAsync(token),
                "The SynToolkit power plan was removed.");
        }

        private async void RestoreDefaultSchemesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Restore default power plans?",
                Content = "This resets every Windows power plan to its default configuration, including SynToolkit's SOS Performance plan and any other custom plans you've created. This cannot be undone.",
                PrimaryButtonText = "Restore defaults",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result;
            try
            {
                result = await confirmation.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "The restore-default-power-plans confirmation could not be displayed.");
                if (_isPageLoaded)
                {
                    ShowResult("Confirmation unavailable", exception.Message, InfoBarSeverity.Error);
                }
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (!_isPageLoaded)
            {
                return;
            }

            await RunOperationAsync(
                token => _powerPlanService.RestoreDefaultSchemesAsync(token),
                "Windows power plans were restored to their defaults.");
        }

        private async void ImportCustomButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            string? immutablePlanPath = null;
            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            OperationInfoBar.IsOpen = false;
            try
            {
                string? selectedFilePath = ShowPowFilePicker();
                if (selectedFilePath is null)
                {
                    return;
                }

                immutablePlanPath = await SnapshotSelectedPlanAsync(selectedFilePath, cancellationToken);
                PowerPlanImportResult importResult = await _powerPlanService.ImportCustomPlanAsync(
                    immutablePlanPath,
                    cancellationToken);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult(
                        "Power plan imported",
                        $"{importResult.SchemeName} was imported and activated. ID: {importResult.SchemeId:D}",
                        InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away cancels pre-import work. A started Windows
                // power-plan transaction still finishes or rolls back safely.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Custom .pow import failed.");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power-plan import failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (immutablePlanPath is not null)
                {
                    TryDeleteTemporaryPlan(immutablePlanPath);
                }

                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: false);
                    if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                    {
                        SetBusy(false);
                    }
                }
            }
        }

        private async void ImportBundledPlanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy || _isBundledPlanOperationInProgress)
            {
                return;
            }

            if (sender is not Button button || button.Tag is not BundledPowerPlanItem item)
            {
                return;
            }

            BundledPowerPlan plan = item.Plan;
            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            _applyingBundledPlanFilePath = plan.FilePath;
            _isBundledPlanOperationInProgress = true;
            OperationInfoBar.IsOpen = false;
            ApplyBundledPlansFilter();
            UpdateButtonStates();
            try
            {
                PowerPlanImportResult importResult = await _powerPlanService.ImportCustomPlanAsync(
                    plan.FilePath,
                    cancellationToken);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    _bundledPlanSchemeIds[plan.FilePath] = importResult.SchemeId;
                    ApplyActivePlanState(importResult.SchemeId, importResult.SchemeName);
                    ShowResult(
                        "Power plan imported",
                        $"{plan.DisplayName} imported and activated.",
                        InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away cancels pre-import work.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Bundled .pow import failed for {PlanName}.", plan.DisplayName);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power-plan import failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                _applyingBundledPlanFilePath = null;
                _isBundledPlanOperationInProgress = false;
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: false);
                    if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                    {
                        ApplyBundledPlansFilter();
                        UpdateButtonStates();
                    }
                }
            }
        }

        private async void OpenPowerSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:powersleep"));
        }

        private async Task RunOperationAsync(
            Func<CancellationToken, Task> operation,
            string successMessage)
        {
            if (_isBusy)
            {
                return;
            }

            int lifetimeVersion = _lifetimeVersion;
            CancellationToken cancellationToken = _lifetimeCancellation.Token;
            SetBusy(true);
            OperationInfoBar.IsOpen = false;
            try
            {
                await operation(cancellationToken);
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power plan updated", successMessage, InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException)
            {
                // Navigating away cancels the operation without showing an error.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "A power-plan operation failed.");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    ShowResult("Power-plan operation failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    await RefreshStatusAsync(cancellationToken, lifetimeVersion, showErrors: false);
                    if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                    {
                        SetBusy(false);
                    }
                }
            }
        }

        private async Task RefreshStatusAsync(
            CancellationToken cancellationToken,
            int lifetimeVersion,
            bool showErrors)
        {
            if (!IsCurrentLifetime(lifetimeVersion, cancellationToken))
            {
                return;
            }

            StatusProgressRing.IsActive = true;
            StatusProgressRing.Visibility = Visibility.Visible;
            try
            {
                PowerPlanSnapshot snapshot = await _powerPlanService.GetStateAsync(cancellationToken);
                if (!IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    return;
                }

                _snapshot = snapshot;
                ApplyActivePlanDisplayFromSnapshot();

                BuiltInPlanState.Text = _snapshot.HasSynToolkitSchemeConflict
                    ? "Reserved ID conflict — not managed"
                    : _snapshot.IsSynToolkitPlanActive
                        ? "Active"
                        : _snapshot.IsSynToolkitPlanInstalled
                            ? "Installed"
                            : "Not imported";
                UpdateBuiltInPlanActiveBorder();

                PreviousPlanState.Text = _snapshot.PreviousSchemeId is Guid previousSchemeId
                    ? $"Previous plan: {_snapshot.PreviousSchemeName ?? previousSchemeId.ToString("D")}" 
                    : "No previous plan has been recorded.";
            }
            catch (OperationCanceledException)
            {
                // The page is no longer current.
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Unable to read the current power-plan state.");
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    _snapshot = null;
                    CurrentPlanName.Text = "Power-plan status unavailable";
                    CurrentPlanId.Text = string.Empty;
                    UpdateBuiltInPlanActiveBorder();
                    if (showErrors)
                    {
                        ShowResult("Status unavailable", exception.Message, InfoBarSeverity.Error);
                    }
                }
            }
            finally
            {
                if (IsCurrentLifetime(lifetimeVersion, cancellationToken))
                {
                    StatusProgressRing.IsActive = _isBusy;
                    StatusProgressRing.Visibility = _isBusy ? Visibility.Visible : Visibility.Collapsed;
                    ApplyBundledPlansFilter();
                    UpdateButtonStates();
                }
            }
        }

        private bool IsCurrentLifetime(int lifetimeVersion, CancellationToken cancellationToken) =>
            _isPageLoaded &&
            lifetimeVersion == _lifetimeVersion &&
            !cancellationToken.IsCancellationRequested;

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;
            StatusProgressRing.IsActive = isBusy;
            StatusProgressRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtonStates();
        }

        private void ApplyActivePlanDisplayFromSnapshot()
        {
            if (_snapshot is null)
            {
                return;
            }

            CurrentPlanName.Text = _snapshot.ActiveSchemeName;
            CurrentPlanId.Text = _snapshot.ActiveSchemeId?.ToString("D") ?? "Active plan ID unavailable";
        }

        private void UpdateBuiltInPlanActiveBorder()
        {
            if (_snapshot?.IsSynToolkitPlanActive == true)
            {
                BuiltInPlanActiveBorder.BorderBrush =
                    (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                BuiltInPlanActiveBorder.BorderThickness = new Thickness(2);
                return;
            }

            BuiltInPlanActiveBorder.BorderThickness = new Thickness(0);
        }

        private void UpdateButtonStates()
        {
            bool stateReady = _snapshot is not null;
            bool hasConflict = _snapshot?.HasSynToolkitSchemeConflict == true;
            bool canMutate = !_isBusy &&
                !_isBundledPlanOperationInProgress &&
                stateReady &&
                _powerPlanService.CanMutatePowerPlans;
            RefreshButton.IsEnabled = !_isBusy && !_isBundledPlanOperationInProgress;
            RefreshBundledPlansButton.IsEnabled = !_isBusy && !_isBundledPlanOperationInProgress;
            ComparePlansButton.IsEnabled = !_isBusy &&
                !_isBundledPlanOperationInProgress &&
                _snapshot?.ActiveSchemeId is not null;
            ImportBuiltInButton.IsEnabled = canMutate && !hasConflict;
            ImportCustomButton.IsEnabled = canMutate;
            ActivateBuiltInButton.IsEnabled = canMutate && !hasConflict && _snapshot?.IsSynToolkitPlanInstalled == true && _snapshot.IsSynToolkitPlanActive == false;
            RemoveBuiltInButton.IsEnabled = canMutate && !hasConflict && _snapshot?.IsSynToolkitPlanInstalled == true;
            RestorePreviousButton.IsEnabled = canMutate && _snapshot?.PreviousSchemeId is not null;
            ActivateBalancedButton.IsEnabled = canMutate && _snapshot?.ActiveSchemeId is Guid activeSchemeId && activeSchemeId != PowerPlanService.BalancedSchemeId;
            RestoreDefaultSchemesButton.IsEnabled = canMutate;
            ApplyBundledPlansFilter();
        }

        /// <summary>
        private static string? ShowPowFilePicker()
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            return NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, "Windows power plan (*.pow)|*.pow");
        }

        private static async Task<string> SnapshotSelectedPlanAsync(
            string sourceFilePath,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(Path.GetExtension(sourceFilePath), ".pow", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Only Windows .pow power-plan files can be imported.");
            }

            FileInfo sourceInfo = new(sourceFilePath);
            long maximumBytes = PowerPlanService.MaximumPlanFileBytes;
            if (!sourceInfo.Exists || sourceInfo.Length == 0 || sourceInfo.Length > maximumBytes)
            {
                throw new InvalidDataException("The selected .pow file is empty or larger than 64 MB.");
            }

            string destinationPath = Path.Combine(
                Path.GetTempPath(),
                $"SynToolkit-Picker-{Guid.NewGuid():N}.pow");

            try
            {
                await using FileStream source = new(
                    sourceFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using FileStream destination = new(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await PowerPlanService.CopyBoundedAsync(source, destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                return destinationPath;
            }
            catch
            {
                TryDeleteTemporaryPlan(destinationPath);
                throw;
            }
        }

        private static void TryDeleteTemporaryPlan(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Unable to remove temporary selected power-plan file {Path}.", path);
            }
        }

        private void ShowResult(string title, string message, InfoBarSeverity severity)
        {
            OperationInfoBar.Title = title;
            OperationInfoBar.Message = string.IsNullOrWhiteSpace(message)
                ? "Windows did not return any additional details. Check the SynToolkit log, then try again as administrator."
                : message.Trim();
            OperationInfoBar.Severity = severity;
            OperationInfoBar.IsOpen = true;
        }
    }
}
