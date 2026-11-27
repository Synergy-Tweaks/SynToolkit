#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SynToolkit.Drives.Core;
using SynToolkit.ViewModels;

namespace SynToolkit.Views;

public sealed partial class DrivesCleanerPage : Page
{
    private readonly DrivesCleanerViewModel _viewModel;

    public DrivesCleanerPage()
    {
        InitializeComponent();
        _viewModel = App._host.Services.GetRequiredService<DrivesCleanerViewModel>();
        DataContext = _viewModel;
    }

    private void CategoryHeader_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CleanerCategoryViewModel category } && category.ShowExpand)
        {
            category.ToggleExpandCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SpaceMapList_ItemClick(object sender, ItemClickEventArgs e)
    {
        _viewModel.OpenSpaceMapRow(e.ClickedItem as SpaceMapRowViewModel);
    }

    private async void CleanSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanClean)
        {
            return;
        }

        IReadOnlyList<CleanupItem> reviewItems = _viewModel.SelectedReviewItems();
        long totalBytes = _viewModel.SelectedTotal;
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            Title = reviewItems.Count > 0 ? "Review selected items" : App.GetValueFromItemList("DrivesCleaner_ConfirmTitle"),
            Content = reviewItems.Count > 0
                ? BuildReviewContent(reviewItems, totalBytes)
                : $"{App.GetValueFromItemList("DrivesCleaner_ConfirmBody")}\n\nTotal selected: {Format.Bytes(totalBytes)}",
            PrimaryButtonText = App.GetValueFromItemList("DrivesCleaner_ConfirmPrimary"),
            CloseButtonText = App.GetValueFromItemList("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await _viewModel.CleanSelectedCommand.ExecuteAsync(null);
        }
    }

    private static string BuildReviewContent(IReadOnlyList<CleanupItem> reviewItems, long totalBytes)
    {
        const int maxLines = 12;
        List<string> lines = [];
        foreach (CleanupItem item in reviewItems.Take(maxLines))
        {
            lines.Add($"- {item.Name} ({Format.Bytes(item.Size)})");
        }

        if (reviewItems.Count > maxLines)
        {
            lines.Add($"- +{reviewItems.Count - maxLines} more");
        }

        return $"You selected {reviewItems.Count} review item(s) totaling {Format.Bytes(totalBytes)}.\n\n{string.Join("\n", lines)}";
    }
}
