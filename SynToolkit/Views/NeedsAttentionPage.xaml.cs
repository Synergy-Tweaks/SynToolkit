#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using Windows.System;

namespace SynToolkit.Views;

public sealed partial class NeedsAttentionPage : Page
{
    private readonly NeedsAttentionService _needsAttentionService;
    private CancellationTokenSource _lifetimeCancellation = new();
    private bool _hasLoaded;
    private bool _hasIgnoredChecks;

    public NeedsAttentionPage()
    {
        InitializeComponent();
        _needsAttentionService = App._host.Services.GetRequiredService<NeedsAttentionService>();
        Unloaded += Page_Unloaded;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_lifetimeCancellation.IsCancellationRequested)
        {
            _lifetimeCancellation.Dispose();
            _lifetimeCancellation = new CancellationTokenSource();
        }

        if (!_hasLoaded)
        {
            _hasLoaded = true;
            await LoadItemsAsync(forceRefresh: false);
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => _lifetimeCancellation.Cancel();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadItemsAsync(forceRefresh: true);

    private async void IgnoreOrRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hasIgnoredChecks)
        {
            await RestoreIgnoredChecksAsync();
            return;
        }

        await IgnoreAllChecksAsync();
    }

    private async Task IgnoreAllChecksAsync()
    {
        var dialog = new ContentDialog
        {
            Title = App.GetValueFromItemList("NeedsAttention_IgnoreAll"),
            Content = App.GetValueFromItemList("NeedsAttention_IgnoreAllConfirm"),
            PrimaryButtonText = App.GetValueFromItemList("NeedsAttention_IgnoreAll"),
            CloseButtonText = App.GetValueFromItemList("Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out object? style) &&
            style is Style dialogStyle)
        {
            dialog.Style = dialogStyle;
        }

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await _needsAttentionService.IgnoreAllChecksAsync(_lifetimeCancellation.Token);
            await LoadItemsAsync(forceRefresh: false);
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_IgnoredAll");
            ActionInfoBar.Severity = InfoBarSeverity.Success;
            ActionInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // The page was closed before the preference could be saved.
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[NeedsAttention] Ignore All could not be saved.");
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_ActionFailed");
            ActionInfoBar.Severity = InfoBarSeverity.Error;
            ActionInfoBar.IsOpen = true;
            SetBusy(false);
        }
    }

    private async Task RestoreIgnoredChecksAsync()
    {
        try
        {
            SetBusy(true);
            await _needsAttentionService.RestoreIgnoredChecksAsync(_lifetimeCancellation.Token);
            await LoadItemsAsync(forceRefresh: true);
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_RestoredIgnored");
            ActionInfoBar.Severity = InfoBarSeverity.Success;
            ActionInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // The page was closed before ignored checks could be restored.
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[NeedsAttention] Ignored checks could not be restored.");
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_ActionFailed");
            ActionInfoBar.Severity = InfoBarSeverity.Error;
            ActionInfoBar.IsOpen = true;
            SetBusy(false);
        }
    }

    private async Task LoadItemsAsync(bool forceRefresh)
    {
        SetBusy(true);
        ActionInfoBar.IsOpen = false;
        AllClearPanel.Visibility = Visibility.Collapsed;

        try
        {
            NeedsAttentionSnapshot snapshot = await _needsAttentionService.GetDashboardSnapshotAsync(
                forceRefresh,
                _lifetimeCancellation.Token);
            AttentionItemsControl.ItemsSource = snapshot.Items;
            bool hasItems = snapshot.Items.Count > 0;
            AllClearPanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            AttentionItemsControl.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            AttentionHeaderIcon.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            UpdateIgnoredChecksUi(snapshot.IgnoredCheckCount);
            (App.m_window as MainWindow)?.UpdateNeedsAttentionBadge(snapshot.Items.Count);
            (App.m_window as MainWindow)?.UpdateInstallerUpdateBadge(snapshot.Items.Count(item =>
                item.Action == NeedsAttentionAction.OpenInstaller));
        }
        catch (OperationCanceledException)
        {
            // Navigating away makes a pending dashboard refresh irrelevant.
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[NeedsAttention] Unable to refresh the dashboard.");
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_RefreshFailed");
            ActionInfoBar.Severity = InfoBarSeverity.Error;
            ActionInfoBar.IsOpen = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        LoadingRing.IsActive = isBusy;
        LoadingRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !isBusy;
        IgnoreOrRestoreButton.IsEnabled = !isBusy;
    }

    private void UpdateIgnoredChecksUi(int ignoredCheckCount)
    {
        _hasIgnoredChecks = ignoredCheckCount > 0;
        if (_hasIgnoredChecks)
        {
            LocalizedText.SetContentKey(IgnoreOrRestoreButton, "NeedsAttention_RestoreIgnored");
            IgnoredChecksText.Text = string.Format(
                CultureInfo.CurrentCulture,
                App.GetValueFromItemList("NeedsAttention_IgnoredCount"),
                ignoredCheckCount);
            IgnoredChecksText.Visibility = Visibility.Visible;
        }
        else
        {
            LocalizedText.SetContentKey(IgnoreOrRestoreButton, "NeedsAttention_IgnoreAll");
            IgnoredChecksText.Visibility = Visibility.Collapsed;
        }
    }

    private async void AttentionActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NeedsAttentionItem item })
        {
            return;
        }

        try
        {
            switch (item.Action)
            {
                case NeedsAttentionAction.OpenDiskCleanup:
                    (App.m_window as MainWindow)?.NavigateToPage(typeof(CleanerPage), "SynToolkit.Views.CleanerPage");
                    break;
                case NeedsAttentionAction.CreateRestorePoint:
                    await RunActionAsync(() => NeedsAttentionService.CreateRestorePointAsync(_lifetimeCancellation.Token),
                        App.GetValueFromItemList("NeedsAttention_RestorePointCreated"));
                    break;
                case NeedsAttentionAction.SyncWindowsClock:
                    await RunActionAsync(() => NeedsAttentionService.SyncWindowsClockAsync(_lifetimeCancellation.Token),
                        App.GetValueFromItemList("NeedsAttention_ClockSynchronized"));
                    break;
                case NeedsAttentionAction.OpenInstaller:
                    (App.m_window as MainWindow)?.NavigateToPage(
                        typeof(AppFetchPage),
                        "SynToolkit.Views.AppFetchPage",
                        new InstallerNavigationRequest(item.ActionTarget ?? string.Empty));
                    break;
                case NeedsAttentionAction.OpenGraphicsDriverPage:
                    if (Uri.TryCreate(item.ActionTarget, UriKind.Absolute, out Uri? driverUri))
                    {
                        await Launcher.LaunchUriAsync(driverUri);
                    }
                    break;
                case NeedsAttentionAction.OpenDeviceManager:
                    ProcessHelper.StartShellExecute("devmgmt.msc");
                    break;
                case NeedsAttentionAction.OpenSettings:
                    (App.m_window as MainWindow)?.NavigateToPage(typeof(SettingsPage), "SettingsPage");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The page was closed before the action finished.
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[NeedsAttention] Action {Action} failed.", item.Action);
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_ActionFailed");
            ActionInfoBar.Severity = InfoBarSeverity.Error;
            ActionInfoBar.IsOpen = true;
        }
    }

    private async void IgnoreAttentionItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NeedsAttentionItem { CanIgnore: true } item })
        {
            return;
        }

        try
        {
            // Prefer the existing fine-grained, duration-based ignore when available.
            // Otherwise suppress the whole check category via the shared ignored-check list.
            if (!string.IsNullOrWhiteSpace(item.IgnoreKey))
            {
                NeedsAttentionIgnoreDuration? duration = await ShowIgnoreDurationDialogAsync();
                if (duration is null)
                {
                    return;
                }

                await _needsAttentionService.IgnoreItemAsync(item, duration.Value, _lifetimeCancellation.Token);
            }
            else if (!string.IsNullOrWhiteSpace(item.CheckId))
            {
                await _needsAttentionService.IgnoreCheckAsync(item.CheckId, _lifetimeCancellation.Token);
            }
            else
            {
                return;
            }

            await LoadItemsAsync(forceRefresh: false);
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_Ignored");
            ActionInfoBar.Severity = InfoBarSeverity.Success;
            ActionInfoBar.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // The page was closed before the preference could be saved.
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[NeedsAttention] Warning could not be ignored.");
            ActionInfoBar.Message = App.GetValueFromItemList("NeedsAttention_ActionFailed");
            ActionInfoBar.Severity = InfoBarSeverity.Error;
            ActionInfoBar.IsOpen = true;
        }
    }

    private async Task<NeedsAttentionIgnoreDuration?> ShowIgnoreDurationDialogAsync()
    {
        var options = new List<NeedsAttentionIgnoreDurationOption>(NeedsAttentionIgnorePolicy.All.Count);
        NeedsAttentionIgnoreDurationOption? selected = null;

        foreach (NeedsAttentionIgnoreDuration duration in NeedsAttentionIgnorePolicy.All)
        {
            var option = new NeedsAttentionIgnoreDurationOption(
                duration,
                App.GetValueFromItemList(NeedsAttentionIgnorePolicy.GetLocalizationKey(duration)));
            options.Add(option);

            if (duration == NeedsAttentionIgnorePolicy.Default)
            {
                selected = option;
            }
        }

        var durationBox = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(NeedsAttentionIgnoreDurationOption.Label),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 260
        };
        durationBox.SelectedItem = selected ?? options[0];

        var panel = new StackPanel { Spacing = 8, MinWidth = 300 };
        panel.Children.Add(new TextBlock
        {
            Text = App.GetValueFromItemList("NeedsAttention_IgnoreDialogDescription"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(durationBox);

        var dialog = new ContentDialog
        {
            Title = App.GetValueFromItemList("NeedsAttention_IgnoreDialogTitle"),
            Content = panel,
            PrimaryButtonText = App.GetValueFromItemList("NeedsAttention_Ignore"),
            CloseButtonText = App.GetValueFromItemList("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary &&
            durationBox.SelectedItem is NeedsAttentionIgnoreDurationOption chosen
            ? chosen.Duration
            : null;
    }

    private sealed record NeedsAttentionIgnoreDurationOption(NeedsAttentionIgnoreDuration Duration, string Label);

    private async Task RunActionAsync(Func<Task<string?>> action, string successMessage)
    {
        string? error = await action();
        ActionInfoBar.Message = error ?? successMessage;
        ActionInfoBar.Severity = error is null ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        ActionInfoBar.IsOpen = true;
        if (error is null)
        {
            await LoadItemsAsync(forceRefresh: true);
        }
    }
}
