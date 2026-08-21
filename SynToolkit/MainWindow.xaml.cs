using SynToolkit.Enums;
using SynToolkit.Services;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using SynToolkit.Views;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using WinUIEx;

namespace SynToolkit
{
    public sealed partial class MainWindow : Window
    {
        public List<IConfigurationItem> RootList { get; set; }
        private bool _isSynchronizingNavigationSelection;
        private bool _isNavigating;

        private const int DefaultWindowWidth = 1250;
        private const int DefaultWindowHeight = 850;
        private const int MinimumWindowWidth = 516;
        private const int MinimumWindowHeight = 491;
        private const int PersistableMinimumWidth = 800;
        private const int PersistableMinimumHeight = 600;

        public MainWindow()
        {
            this.InitializeComponent();
            BackdropHelper.ApplySafeMicaFallback(this, RootGrid);

            // Configure the existing presenter. Replacing it with OverlappedPresenter.Create()
            // resets the window to a compact default and is a common WinUI 3 size-loss bug,
            // especially when a smaller splash window is already visible.
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = MinimumWindowWidth;
                presenter.PreferredMinimumHeight = MinimumWindowHeight;
                presenter.IsMaximizable = true;
            }

            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
            ExtendsContentIntoTitleBar = true;

            LoadText();
            LoadExperiments();

            // Setup root list
            RootList = new List<IConfigurationItem>();
            foreach (IConfigurationItem item in App._host.Services.GetServices<LinksViewModel>())
            {
                /*if (!item.Type.ToString().Contains("SubMenu"))*/
                RootList.Add(item);
            }
            foreach (IConfigurationItem item in App._host.Services.GetServices<ConfigurationItemViewModel>())
            {
                /*if (!item.Type.ToString().Contains("SubMenu"))*/
                RootList.Add(item);
            }
            foreach (IConfigurationItem item in App._host.Services.GetServices<MultiOptionConfigurationItemViewModel>())
            {
                /*if (!item.Type.ToString().Contains("SubMenu"))*/
                RootList.Add(item);
            }
            foreach (IConfigurationItem item in App._host.Services.GetServices<ConfigurationSubMenuViewModel>())
            {
                /*if (!item.Type.ToString().Contains("SubMenu"))*/
                RootList.Add(item);
            }
            foreach (IConfigurationItem item in App._host.Services.GetServices<ConfigurationButtonViewModel>())
            {
                /*if (!item.Type.ToString().Contains("SubMenu"))*/
                RootList.Add(item);
            }
            App.RootList = this.RootList;
            App.CurrentCategory = "SynToolkit.Views.HomePage";
            SynchronizeNavigationSelection(Home);
            Navigate(
                typeof(Views.HomePage),
                new Microsoft.UI.Xaml.Media.Animation.EntranceNavigationTransitionInfo());
            SetTitleBar(AppTitleBar);
            SetWindowPosSize();
            InitializeAccountHeader();
            Activated += OnMainWindowActivated;
            this.Closed += AppBehaviorHelper.HandleMainWindowClosed;

            SubscribeToConfigurationChanges();
        }

        private void OnMainWindowActivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                return;
            }

            Activated -= OnMainWindowActivated;
            ApplyWindowPlacement();
        }

        internal void ApplyWindowPlacement()
        {
            SetWindowPosSize();
        }

        private void InitializeAccountHeader()
        {
            try
            {
                IUserAccountInfoService accountInfoService =
                    App._host.Services.GetRequiredService<IUserAccountInfoService>();
                AccountHeader.SetAccountInfo(accountInfoService.GetPlaceholder());
                _ = LoadAccountHeaderAsync(accountInfoService);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to initialize the navigation account header.");
            }
        }

        private async Task LoadAccountHeaderAsync(IUserAccountInfoService accountInfoService)
        {
            try
            {
                Models.UserAccountInfo accountInfo = await accountInfoService.GetCurrentUserAsync();
                AccountHeader.SetAccountInfo(accountInfo);
                App.UpdateDisplayUserName(accountInfo.DisplayName);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "Unable to load the navigation account header.");
            }
        }

        private void SubscribeToConfigurationChanges()
        {
            try
            {
                var stores = App._host?.Services?.GetServices<Stores.ConfigurationStore>();
                if (stores == null) return;

                foreach (var store in stores)
                {
                    store.CurrentSettingChanged += OnConfigurationChanged;
                }
            }
            catch (Exception ex)
            {
                App.logger?.Warn(ex, "Failed to subscribe to configuration changes.");
            }
        }

        private void OnConfigurationChanged()
        {
            DispatcherQueue?.TryEnqueue(() => UpdateNavigationBadges());
        }

        public void LoadExperiments()
        {
            
        }

        public bool IsFullscreen()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                if (presenter.State == OverlappedPresenterState.Maximized)
                {
                    return true;
                }
            }
            return false;
        }

        public void LoadText()
        {
            // Updates
            UpdateTitleBar.Title = App.GetValueFromItemList("NewUpdateDesc");
            LearnMoreBtn.Content = App.GetValueFromItemList("LearnMore");

            // Navigation Items
            Home.Content = App.GetValueFromItemList("Home");
            InstallerText.Text = App.GetValueFromItemList("Installer");
            PowerPlansText.Text = App.GetValueFromItemList("PowerPlans");
            AdjustmentsText.Text = App.GetValueFromItemList("Adjustments");
            Gpu.Content = App.GetValueFromItemList("Gpu");
            Specs.Content = App.GetValueFromItemList("Specs");
            DiskCleanup.Content = App.GetValueFromItemList("Cleaner");
            GeneralConfigText.Text = App.GetValueFromItemList("GeneralConfig");
            InterfaceText.Text = App.GetValueFromItemList("Interface");
            WindowsText.Text = App.GetValueFromItemList("Windows");
            AdvancedText.Text = App.GetValueFromItemList("Advanced");
            SecurityText.Text = App.GetValueFromItemList("Security");
            TroubleshootingText.Text = App.GetValueFromItemList("Troubleshooting");
            Setting.Content = App.GetValueFromItemList("Settings");

            // Search Box
            SearchBox.PlaceholderText = App.GetValueFromItemList("SearchPlaceholder");

            // Initialize navigation badges
            UpdateNavigationBadges();
            UpdateNewBadges();
        }

        /// <summary>
        /// Shows or hides persisted NEW badges for flagged navigation tabs.
        /// </summary>
        public void UpdateNewBadges()
        {
            SetNewBadgeVisibility(InstallerNewBadgeBorder, "Installer");
            SetNewBadgeVisibility(PowerPlansNewBadgeBorder, "PowerPlans");
            SetNewBadgeVisibility(AdjustmentsNewBadgeBorder, "Customizations");
            SetNewBadgeVisibility(AdvancedNewBadgeBorder, "AdvancedConfigurations");
        }

        private static void SetNewBadgeVisibility(Border border, string tabId)
        {
            border.Visibility = NavigationNewBadgeService.ShouldShowNewBadge(tabId)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void MarkCurrentTabNewBadgeSeenIfNeeded()
        {
            if (NavigationNewBadgeService.MarkTabSeenForNavigationTag(App.CurrentCategory))
            {
                UpdateNewBadges();
            }
        }

        /// <summary>
        /// Updates the navigation badge counters showing enabled/total settings per category.
        /// </summary>
        public void UpdateNavigationBadges()
        {
            try
            {
                var configItems = App._host?.Services?.GetServices<ConfigurationItemViewModel>()?.ToList();
                if (configItems == null || configItems.Count == 0) return;

                UpdateBadgeForType(GeneralBadge, GeneralBadgeBorder, configItems, ConfigurationType.General);
                UpdateBadgeForType(InterfaceBadge, InterfaceBadgeBorder, configItems, ConfigurationType.Interface);
                UpdateBadgeForType(WindowsBadge, WindowsBadgeBorder, configItems, ConfigurationType.Windows);
                UpdateBadgeForType(AdvancedBadge, AdvancedBadgeBorder, configItems, ConfigurationType.Advanced);
                UpdateBadgeForType(SecurityBadge, SecurityBadgeBorder, configItems, ConfigurationType.Security);
                UpdateBadgeForType(TroubleshootingBadge, TroubleshootingBadgeBorder, configItems, ConfigurationType.Troubleshooting);
            }
            catch (Exception ex)
            {
                App.logger?.Warn(ex, "Failed to update navigation badges.");
            }
        }

        private void UpdateBadgeForType(TextBlock badge, Border border, List<ConfigurationItemViewModel> items, ConfigurationType type)
        {
            var typeItems = items.Where(x => x.Type == type).ToList();
            if (typeItems.Count == 0)
            {
                border.Visibility = Visibility.Collapsed;
                return;
            }

            int enabled = typeItems.Count(x => x.CurrentSetting);
            int total = typeItems.Count;
            badge.Text = $"{enabled}/{total}";
            border.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Gets the window Xaml root for ContentDialogs
        /// </summary>
        /// <returns></returns>
        public XamlRoot GetXamlRoot()
        {
            return this.Content.XamlRoot;
        }

        #region Navigation Control
        /// <summary>
        /// navigates to the correct page when a navigation item is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void NavigationViewControl_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isSynchronizingNavigationSelection || _isNavigating)
            {
                return;
            }

            string selectedItem = args.SelectedItemContainer?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(selectedItem) ||
                string.Equals(App.CurrentCategory, selectedItem, StringComparison.Ordinal))
            {
                return;
            }

            Type destination = selectedItem switch
            {
                "SettingsPage" => typeof(SettingsPage),
                "SynToolkit.Views.AppFetchPage" => typeof(AppFetchPage),
                "SynToolkit.Views.PowerPlansPage" => typeof(PowerPlansPage),
                "SynToolkit.Views.AdjustmentsPage" => typeof(AdjustmentsPage),
                "SynToolkit.Views.GpuPage" => typeof(GpuPage),
                "SynToolkit.Views.SpecsPage" => typeof(SpecsPage),
                "SynToolkit.Views.CleanerPage" => typeof(CleanerPage),
                "SynToolkit.Views.HomePage" => typeof(HomePage),
                _ => typeof(ConfigPage)
            };

            string previousCategory = App.CurrentCategory;
            App.CurrentCategory = selectedItem;
            if (!Navigate(destination))
            {
                App.CurrentCategory = previousCategory;
                NavigateTo();
            }

            App.XamlRoot = this.Content.XamlRoot;
        }

        /// <summary>
        /// Navigates the ContentFrame to the selected page
        /// </summary>
        /// <param name="tag"></param>
        private bool Navigate(Type type, NavigationTransitionInfo transitionInfo = null)
        {
            if (type is null || ContentFrame is null || _isNavigating)
            {
                return false;
            }

            if (ContentFrame.SourcePageType == type && type != typeof(ConfigPage))
            {
                return true;
            }

            try
            {
                _isNavigating = true;
                return ContentFrame.Navigate(
                    type,
                    App.CurrentCategory,
                    transitionInfo ?? new DrillInNavigationTransitionInfo());
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, $"Navigation to {type.FullName} failed.");
                return false;
            }
            finally
            {
                _isNavigating = false;
            }
        }

        public void GoBack()
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }
        public void NavigationViewControl_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Parameter is string category && !string.IsNullOrWhiteSpace(category))
            {
                // The frame journal preserves navigation parameters, so restoring the
                // category here keeps Back/Forward content and sidebar state in sync.
                App.CurrentCategory = category;
            }

            App.UpdateDiscordPresence(GetDiscordPresenceState(App.CurrentCategory));
            MarkCurrentTabNewBadgeSeenIfNeeded();
            NavigateTo();
        }

        private static string GetDiscordPresenceState(string category)
        {
            return category switch
            {
                "SynToolkit.Views.HomePage" => "Home",
                "SynToolkit.Views.AppFetchPage" => "Installer",
                "SynToolkit.Views.PowerPlansPage" => "Power Plans",
                "SynToolkit.Views.AdjustmentsPage" => "Adjustments",
                "SynToolkit.Views.GpuPage" => "GPU",
                "SynToolkit.Views.SpecsPage" => "Specs",
                "SynToolkit.Views.CleanerPage" => "Disk Cleanup",
                "General" => "General Configuration",
                "Interface" => "Interface Tweaks",
                "Windows" => "Windows Settings",
                "Advanced" => "Advanced Configuration",
                "Security" => "Security",
                "Troubleshooting" => "Restoration",
                "SettingsPage" => "Settings",
                _ => "Configuring Windows"
            };
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            App.logger.Error(e.Exception, $"The content frame could not navigate to {e.SourcePageType?.FullName ?? "an unknown page"}.");
            e.Handled = true;
            NavigateTo();
        }

        private void NavigateTo()
        {
            if (ContentFrame is null || NavigationViewControl is null)
            {
                return;
            }

            NavigationViewControl.IsBackEnabled = ContentFrame.CanGoBack;
            NavigationViewControl.Header = null;

            if (ContentFrame.SourcePageType == typeof(Views.SettingsPage))
            {
                NavigationViewItem settingsItem = NavigationViewControl.FooterMenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(n => string.Equals(n.Tag?.ToString(), "SettingsPage", StringComparison.Ordinal));
                SynchronizeNavigationSelection(settingsItem);
                return;
            }
            if (ContentFrame.SourcePageType != typeof(Views.SubSection))
            {
                NavigationViewItem matchingItem = NavigationViewControl.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(n => string.Equals(
                        n.Tag?.ToString(),
                        App.CurrentCategory,
                        StringComparison.Ordinal));
                if (matchingItem is null)
                {
                    App.logger.Error($"No matching NavigationViewItem found for category: {App.CurrentCategory}");
                    return;
                }

                SynchronizeNavigationSelection(matchingItem);
            }
        }

        private void SynchronizeNavigationSelection(NavigationViewItem item)
        {
            if (item is null || ReferenceEquals(NavigationViewControl.SelectedItem, item))
            {
                return;
            }

            try
            {
                _isSynchronizingNavigationSelection = true;
                NavigationViewControl.SelectedItem = item;
            }
            finally
            {
                _isSynchronizingNavigationSelection = false;
            }
        }
        #endregion Navigation Control

        private void AppTitleBar_PaneToggleRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
        {
            NavigationViewControl.IsPaneOpen = !NavigationViewControl.IsPaneOpen;
        }


        /// <summary>
        /// Creates a ContentDialog with the required type
        /// </summary>
        /// <param name="type">type of content dialog</param>
        /// <exception cref="Exception"></exception>
        public async void ContentDialogContoller(string type)
        {
            string title = "", desc = "", primBtnTxt = "";
            ICommand command = null;

            switch (type)
            {
                case "newUpdate":
                    title = App.GetValueFromItemList("NewUpdate");
                    desc = App.GetValueFromItemList("NewUpdateDesc");
                    primBtnTxt = App.GetValueFromItemList("Yes");
                    command = new RelayCommand(SynToolkitUpdateHelper.InstallUpdate);
                    break;
                case "restartApp":
                    title = App.GetValueFromItemList("RestartApp");
                    desc = App.GetValueFromItemList("RestartAppDesc");
                    primBtnTxt = App.GetValueFromItemList("RestartAppBtn");
                    command = new RelayCommand(ComputerStateHelper.RestartApp);
                    break;
                case "restart":
                    title = App.GetValueFromItemList("RestartPC");
                    desc = App.GetValueFromItemList("RestartPCDesc");
                    primBtnTxt = App.GetValueFromItemList("RestartAppBtn");
                    command = new RelayCommand(ComputerStateHelper.RestartComputer);
                    break;
                case "logoff":
                    title = App.GetValueFromItemList("RelogApply");
                    desc = App.GetValueFromItemList("RelogApplyDesc");
                    primBtnTxt = App.GetValueFromItemList("RelogBtn");
                    command = new RelayCommand(ComputerStateHelper.LogOffComputer);
                    break;
                default:
                    throw new Exception("ContentDialog type was not set or does not match any possible type");
            }
            await DispatcherQueue.EnqueueAsync(() =>
            {
                ContentDialog dialog = new ContentDialog();

                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                dialog.XamlRoot = App.XamlRoot;
                dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
                dialog.Title = title;
                dialog.Content = desc;
                dialog.PrimaryButtonText = primBtnTxt;
                dialog.CloseButtonText = App.GetValueFromItemList("Later");
                dialog.DefaultButton = ContentDialogButton.Primary;
                dialog.PrimaryButtonCommand = command;

                try
                {
                    var result = dialog.ShowAsync();
                }
                catch
                { App.logger.Error("Program tried to open more than one ContentDialog"); }
            });
        }

        /// <summary>
        /// Sets the window position and size
        /// </summary>
        private void SetWindowPosSize()
        {
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);
            int width, height;
            bool usingSavedSize;
            try
            {
                width = int.Parse((string)RegistryHelper.GetValue(@"HKLM\SOFTWARE\SynToolkit", "AppWidth"));
                height = int.Parse((string)RegistryHelper.GetValue(@"HKLM\SOFTWARE\SynToolkit", "AppHeight"));
                if (width < PersistableMinimumWidth || height < PersistableMinimumHeight)
                {
                    throw new FormatException(
                        $"Saved window dimensions {width}x{height} are below the persistable minimum {PersistableMinimumWidth}x{PersistableMinimumHeight}.");
                }

                usingSavedSize = true;
            }
            catch (Exception ex)
            {
                width = DefaultWindowWidth;
                height = DefaultWindowHeight;
                usingSavedSize = false;
                App.logger.Warn("Window size values were incorrect. Using in-memory defaults for this launch.\n\n" + ex.Message);
            }

            if (!usingSavedSize)
            {
                if (screenWidth != 1920)
                {
                    width = (int)Math.Round((screenWidth / 1920d) * DefaultWindowWidth);
                }
                if (screenHeight != 1080)
                {
                    height = (int)Math.Round((screenHeight / 1080d) * DefaultWindowHeight);
                }
            }

            int maxWidth = Math.Max(MinimumWindowWidth, screenWidth);
            int maxHeight = Math.Max(MinimumWindowHeight, screenHeight);
            width = Math.Clamp(width, MinimumWindowWidth, maxWidth);
            height = Math.Clamp(height, MinimumWindowHeight, maxHeight);

            double centerX = (screenWidth - width) / 2;
            double centerY = (screenHeight - height) / 2;

            AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            this.Move((int)centerX, (int)centerY);
        }
        /// <summary>
        /// Formats a double into an int 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private int FormatDoubleInt(double value)
        {
            string valueString = value.ToString();
            string[] valueArr = valueString.Split('.');
            return int.Parse(valueArr[0]);
        }

        public void GetWindowSize(out int width, out int height)
        {
            width = AppWindow.Size.Width;
            height = AppWindow.Size.Height;
        }

        internal bool IsPersistableWindowSize(int width, int height) =>
            width >= PersistableMinimumWidth && height >= PersistableMinimumHeight;

        //[DllImport("user32.dll", SetLastError = true)]
        //private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        int timesClicked;
        private void NavigationPaneButton_Click(object sender, RoutedEventArgs e)
        {
            if (timesClicked == 10)
            {
                App.f_window = new FWindow();
                App.f_window.Activate();
                timesClicked = 0;
            }
            else
            {
                timesClicked++;
            }
        }

        #region Search experiment
        private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var configItem = RootList.Where(item => item.Name == args.SelectedItem.ToString()).FirstOrDefault();
            if (configItem is null) return;
            
            string type = configItem.Type.ToString();
            if (configItem is not null)
            {
                // Search bar logic. WIP.
                if (type.Contains("SubMenu"))
                {
                    SettingsCard settingCard = new SettingsCard();
                    try
                    {
                        IEnumerable<ConfigurationSubMenuViewModel> items = App._host.Services.GetServices<ConfigurationSubMenuViewModel>();
                        ConfigurationSubMenuViewModel itemViewModel = items.Where(vm => vm.Key == type).First();
                        ConfigurationSubMenuViewModel rootItemViewModel = null;
                        DataTemplate template = new DataTemplate();
                        ObservableCollection<Folder> folders = new ObservableCollection<Folder>();
                        while (type.Contains("SubMenu"))
                        {
                            string itemViewModelType = itemViewModel.Type.ToString();
                            folders.Add(new Folder() { Name = itemViewModel.Name });
                            if (rootItemViewModel is null) rootItemViewModel = items.Where(vm => vm.Key == type).First();
                            if (itemViewModelType.Contains("SubMenu"))
                            {
                                type = itemViewModelType;
                                itemViewModel = items.Where(vm => vm.Key == type).First();
                                configItem = itemViewModel;
                            }
                            else
                            {
                                folders.Add(new Folder() { Name = itemViewModelType });
                                type = itemViewModelType;
                            }
                        }
                    //folders.Remove(folders.First());
                    // Set the item key to highlight after navigation
                    App.SearchHighlightItemKey = configItem.Key;
                    
                    ContentFrame.Navigate(typeof(SubSection), new Tuple<ConfigurationSubMenuViewModel, DataTemplate, object>
                        (rootItemViewModel, template, new ObservableCollection<Folder>(folders.Reverse())), new SlideNavigationTransitionInfo()
                        { Effect = SlideNavigationTransitionEffect.FromRight });
                    }
                    catch (Exception ex)
                    {
                        App.logger.Error(ex.Message + ": An exception was thrown when trying to open a submenu:\n\n" + ex.InnerException);
                    }
                }
                else
                {
                    // Set the item key to highlight after navigation
                    App.SearchHighlightItemKey = configItem.Key;
                    
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems
                                    .OfType<NavigationViewItem>()
                                    .First(n => n.Tag.Equals(configItem.Type.ToString()));
                    App.CurrentCategory = configItem.Type.ToString();
                    Navigate(typeof(Views.ConfigPage));
                }
            }
            
            // Clear the search box after selection
            sender.Text = string.Empty;
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Since selecting an item will also change the text,
            // only listen to changes caused by user entering text.
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var suitableItems = new List<string>();
                var splitText = sender.Text.ToLower().Split(" ");
                foreach (var viewModel in RootList)
                {
                    var found = splitText.All((key) =>
                    {
                        return viewModel.Name.ToLower().Contains(key);
                    });
                    if (found)
                    {
                        suitableItems.Add(viewModel.Name);
                    }
                }
                if (suitableItems.Count == 0)
                {
                    suitableItems.Add("No results found");
                }
                sender.ItemsSource = suitableItems;
            }
        }
        #endregion Search experiment
    }
}
