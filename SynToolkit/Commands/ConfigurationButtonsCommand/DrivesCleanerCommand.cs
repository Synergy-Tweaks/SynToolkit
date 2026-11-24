using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

#nullable enable

namespace SynToolkit.Commands.ConfigurationButtonsCommand;

public class DrivesCleanerCommand : AsyncCommandBase
{
    protected override async Task ExecuteAsync(object? parameter)
    {
        await Task.CompletedTask;
        if (App.m_window?.Content is not Grid rootGrid)
        {
            return;
        }

        NavigationView? navView = FindNavigationView(rootGrid);
        if (navView is null)
        {
            return;
        }

        foreach (object item in navView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "SynToolkit.Views.DrivesCleanerPage")
            {
                navView.SelectedItem = navItem;
                break;
            }
        }
    }

    private static NavigationView? FindNavigationView(Microsoft.UI.Xaml.DependencyObject parent)
    {
        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            Microsoft.UI.Xaml.DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is NavigationView nav)
            {
                return nav;
            }

            NavigationView? result = FindNavigationView(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
