using System;
using System.Collections.ObjectModel;
using System.Linq;
using SynToolkit.Enums;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using NLog.Filters;
using System.Threading;

namespace SynToolkit.Views;

public sealed partial class ConfigPage : Page
{
    private readonly ConfigPageViewModel _viewModel;
    private CancellationTokenSource _lifetimeCancellation = new();
    private DispatcherTimer _highlightTimer;

    public ConfigPage()
    {
        this.InitializeComponent();

        try
        {
            _viewModel = App._host.Services.GetRequiredService<ConfigPageViewModel>();
            
            // Gets all the items for the chosen category
            ConfigurationType type = ConfigurationType.General;
            
            if (Enum.TryParse(typeof(ConfigurationType), App.CurrentCategory, out object parsedType) && parsedType != null)
            {
                type = (ConfigurationType)parsedType;
            }
            else
            {
                App.logger.Warn($"Failed to parse ConfigurationType from: {App.CurrentCategory}. Defaulting to General.");
            }
            
            _viewModel.ShowForType(type);

            this.DataContext = _viewModel;

            BreadcrumbBar.ItemsSource = new ObservableCollection<Folder> {
                new Folder {Name = type.GetDescription() ?? "Configuration"}
            };
            BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
            ConfigureTweaksIntro(type);

            this.Loaded += ConfigPage_Loaded;
            this.Unloaded += ConfigPage_Unloaded;
        }
        catch (Exception ex)
        {
            App.logger.Error($"Error initializing ConfigPage: {ex.Message}");
        }
    }

    private void ConfigureTweaksIntro(ConfigurationType type)
    {
        bool isTweaks = type == ConfigurationType.Tweaks;
        TweaksIntroPanel.Visibility = isTweaks ? Visibility.Visible : Visibility.Collapsed;
        TweaksWarningBanner.IsOpen = isTweaks;
        if (!isTweaks)
        {
            return;
        }

        TweaksDescriptionText.Text = App.GetValueFromItemList("TweaksPageDescription");
        TweaksWarningBanner.Title = App.GetValueFromItemList("TweaksWarningTitle");
        TweaksWarningBanner.Message = App.GetValueFromItemList("TweaksWarningMessage");
    }

    private void RefreshVisibleConfigurationStates()
    {
        foreach (IConfigurationItem item in _viewModel.ConfigurationItems)
        {
            if (item is ConfigurationItemViewModel { Key: "Hags" } hagsItem)
            {
                hagsItem.RefreshCurrentSetting();
                break;
            }
        }
    }

    private async void ConfigPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.ConfigurationActionSucceeded += ConfigurationActionSucceeded;
        RefreshVisibleConfigurationStates();

        if (_lifetimeCancellation.IsCancellationRequested)
        {
            _lifetimeCancellation.Dispose();
            _lifetimeCancellation = new CancellationTokenSource();
        }

        // Check if there's an item to highlight from search
        if (!string.IsNullOrEmpty(App.SearchHighlightItemKey))
        {
            string targetKey = App.SearchHighlightItemKey;
            App.SearchHighlightItemKey = null; 

            try
            {
                await System.Threading.Tasks.Task.Delay(100, _lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsLoaded && !_lifetimeCancellation.IsCancellationRequested)
            {
                ScrollToAndHighlightItem(targetKey);
            }
        }
    }

    private void ConfigPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.ConfigurationActionSucceeded -= ConfigurationActionSucceeded;
        _lifetimeCancellation.Cancel();
        _highlightTimer?.Stop();
        _highlightTimer = null;
    }

    private void ConfigurationActionSucceeded(string message)
    {
        if (!IsLoaded)
        {
            return;
        }

        OperationInfoBar.Title = "Done";
        OperationInfoBar.Message = message;
        OperationInfoBar.Severity = InfoBarSeverity.Success;
        OperationInfoBar.IsOpen = true;
    }

    private void ScrollToAndHighlightItem(string itemKey)
    {
        // Find the index of the item in the vm
        int index = -1;
        for (int i = 0; i < _viewModel.ConfigurationItems.Count; i++)
        {
            if (_viewModel.ConfigurationItems[i].Key == itemKey)
            {
                index = i;
                break;
            }
        }

        if (index < 0) return;

        // Get the item SettingsCard
        var container = ConfigItemsControl.ContainerFromIndex(index) as ContentPresenter;
        if (container == null) return;

        var settingsCard = FindDescendant<SettingsCard>(container);
        if (settingsCard == null) return;

        // Scroll to the item
        var transform = settingsCard.TransformToVisual(ConfigScrollViewer);
        var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        
        double scrollPosition = ConfigScrollViewer.VerticalOffset + position.Y - (ConfigScrollViewer.ActualHeight / 2) + (settingsCard.ActualHeight / 2);
        ConfigScrollViewer.ChangeView(null, Math.Max(0, scrollPosition), null);

        HighlightSettingsCard(settingsCard);
    }

    private void HighlightSettingsCard(SettingsCard settingsCard)
    {
        var originalBrush = settingsCard.BorderBrush;
        var originalThickness = settingsCard.BorderThickness;

        var highlightBrush = new SolidColorBrush(Microsoft.UI.Colors.Gold);
        highlightBrush.Opacity = 0.3;
        settingsCard.BorderBrush = highlightBrush;
        settingsCard.BorderThickness = new Thickness(3);

        // Create a timer to fade out the highlight
        _highlightTimer?.Stop();
        DispatcherTimer timer = new();
        _highlightTimer = timer;
        timer.Interval = TimeSpan.FromMilliseconds(1500);
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (ReferenceEquals(_highlightTimer, timer))
            {
                _highlightTimer = null;
            }

            if (IsLoaded)
            {
                settingsCard.BorderBrush = originalBrush;
                settingsCard.BorderThickness = originalThickness;
            }
        };
        timer.Start();
    }

    private T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }
        return null;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (BreadcrumbBar.ItemsSource is ObservableCollection<Folder> items)
        {
            for (int i = items.Count - 1; i >= args.Index + 1; i--)
            {
                items.RemoveAt(i);
            }
        }
    }

    private void ConfigurationButtonCard_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (sender is not SettingsCard settingsCard) return;
        if (settingsCard.DataContext is not ConfigurationButtonViewModel item) return;
        if (item.ExecuteCommandCommand == null) return;
        if (!item.ExecuteCommandCommand.CanExecute(null)) return;

        item.ExecuteCommandCommand.Execute(null);
    }

    private void OnCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not SettingsCard settingCard) return;
        if (settingCard.DataContext is not ConfigurationSubMenuViewModel item) return;
        if (!item.CanOpen) return;

        DataTemplate template = (DataTemplate)MainGrid.Resources["ConfigurationSubMenuTemplate"];

        try
        {
            Frame.Navigate(typeof(SubSection), new Tuple<ConfigurationSubMenuViewModel, DataTemplate, object>(item, template, this.BreadcrumbBar.ItemsSource), new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
        }
        catch (Exception ex)
        {
            App.logger.Error($"Exception when attempting to navigate to {item.Type}: \n\t{ex.Message}\n\n{ex.InnerException}");
        }
    }

    private void ToggleSwitch_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            toggleSwitch.Toggled -= ToggleSwitchBehavior.OnToggled;
            toggleSwitch.Toggled += ToggleSwitchBehavior.OnToggled;
        }
    }

    private async void LinkCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is SettingsCard linkCard && linkCard.DataContext is LinksViewModel linkVM && !string.IsNullOrEmpty(linkVM.Link))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(linkVM.Link));
        }
    }

    private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuFlyoutItem && menuFlyoutItem.Tag != null)
        {
            RegistryHelper.SetValue(@"HKLM\SOFTWARE\\SynToolkit\\Favorites", menuFlyoutItem.Tag.ToString(), true);
        }
    }
}
