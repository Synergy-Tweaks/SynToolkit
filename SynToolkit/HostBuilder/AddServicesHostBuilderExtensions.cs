using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Services;
using SynToolkit.Services.AudioMixer;
using SynToolkit.Services.ConfigurationSubMenu;
using SynToolkit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using SynToolkit.Services.Bcd;
using SynToolkit.Services.GpuDrivers;

namespace SynToolkit.HostBuilder
{
    public static class AddServicesHostBuilderExtensions
    {
        public static IHostBuilder AddServices(this IHostBuilder host)
        {
            host.ConfigureServices((_,services) =>
            {
                services.AddTransient<IDismService, DismService>();
                services.AddTransient<IBcdWmiProvider, WmiBcdProvider>();
                services.AddTransient<IBcdService, BcdService>();
                services.AddSingleton<ISystemInformationService, SystemInformationService>();
                services.AddSingleton<IUserAccountInfoService, UserAccountInfoService>();
                services.AddSingleton<AppFetchService>();
                services.AddSingleton<WingetInstallerService>();
                services.AddSingleton<NeedsAttentionService>();
                services.AddSingleton<AudioMixerSettingsStore>();
                services.AddSingleton<IAudioMixerService, WindowsAudioMixerService>();
                services.AddSingleton<IMediaSessionService, WindowsMediaSessionService>();
                services.AddSingleton<AudioMixerHotkeyService>();
                services.AddSingleton<IGpuDriverCatalogService, GpuDriverCatalogService>();
                services.AddSingleton<IGpuDriverPackageService, GpuDriverPackageService>();
            });

            host.AddConfigurationServices();
            host.AddConfigurationMenus();

            return host;
        }

        /// <summary>
        /// Register IConfigurationServices
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationServices(this IHostBuilder host)
        {
            host.ConfigureServices((_, services) =>
            {
                services.AddKeyedTransient<IConfigurationService, AnimationsConfigurationService>("Animations");
                services.AddKeyedTransient<IConfigurationService, AppStoreArchivingConfigurationService>("AppStoreArchiving");
                services.AddKeyedTransient<IConfigurationService, BluetoothConfigurationService>("Bluetooth");
                services.AddKeyedTransient<IConfigurationService, XboxServicesConfigurationService>("XboxServices");
                services.AddKeyedTransient<IConfigurationService, FsoAndGameBarConfigurationService>("FsoAndGameBar");
                services.AddKeyedTransient<IConfigurationService, LanmanWorkstationConfigurationService>("LanmanWorkstation");
                services.AddKeyedTransient<IConfigurationService, SearchIndexingConfigurationService>("SearchIndexing");
                services.AddKeyedTransient<IConfigurationService, CpuIdleContextMenuConfigurationService>("CpuIdleContextMenu");
                services.AddKeyedTransient<IConfigurationService, LockScreenConfigurationService>("LockScreen");
                services.AddKeyedTransient<IConfigurationService, RunWithPriorityConfigurationService>("RunWithPriority");
                services.AddKeyedTransient<IConfigurationService, ShortcutTextConfigurationService>("ShortcutText");
                services.AddKeyedTransient<IConfigurationService, BootLogoConfigurationService>("BootLogo");
                services.AddKeyedTransient<IConfigurationService, BootMessagesConfigurationService>("BootMessages");
                services.AddKeyedTransient<IConfigurationService, NewBootMenuConfigurationService>("NewBootMenu");
                services.AddKeyedTransient<IConfigurationService, SpinningAnimationConfigurationService>("SpinningAnimation");
                services.AddKeyedTransient<IConfigurationService, AdvancedBootOptionsConfigurationService>("AdvancedBootOptions");
                services.AddKeyedTransient<IConfigurationService, AutomaticRepairConfigurationService>("AutomaticRepair");
                services.AddKeyedTransient<IConfigurationService, KernelParametersConfigurationService>("KernelParameters");
                services.AddKeyedTransient<IConfigurationService, HighestModeConfigurationService>("HighestMode");
                services.AddKeyedTransient<IConfigurationService, CompactViewConfigurationService>("CompactView");
                services.AddKeyedTransient<IConfigurationService, RemovableDrivesInSidebarConfigurationService>("RemovableDrivesInSidebar");
                services.AddKeyedTransient<IConfigurationService, AutomaticUpdatesConfigurationService>("AutomaticUpdates");
                services.AddKeyedTransient<IConfigurationService, BackgroundAppsConfigurationService>("BackgroundApps");
                services.AddKeyedTransient<IConfigurationService, DeliveryOptimisationConfigurationService>("DeliveryOptimisation");
                services.AddKeyedTransient<IConfigurationService, HibernationConfigurationService>("Hibernation");
                services.AddKeyedTransient<IConfigurationService, LocationConfigurationService>("Location");
                services.AddKeyedTransient<IConfigurationService, SleepConfigurationService>("Sleep");
                services.AddKeyedTransient<IConfigurationService, UpdateNotificationsConfigurationService>("UpdateNotifications");
                services.AddKeyedTransient<IConfigurationService, WidgetsConfigurationService>("Widgets");
                services.AddKeyedTransient<IConfigurationService, MultiPlaneOverlayConfigurationService>("MultiPlaneOverlay");
                services.AddKeyedTransient<IConfigurationService, HagsConfigurationService>("Hags");
                services.AddKeyedTransient<IConfigurationService, WindowedGamesOptimizationConfigurationService>("WindowedGamesOptimization");
                services.AddKeyedTransient<IConfigurationService, DefenderRealtimeProtectionConfigurationService>("DefenderRealtimeProtection");
                services.AddKeyedTransient<IConfigurationService, UsernameRequirementConfigurationService>("UsernameRequirement");
                services.AddKeyedTransient<IConfigurationService, UacConfigurationService>("UAC");
                services.AddKeyedTransient<IConfigurationService, WiFiConfigurationService>("WiFi");
                services.AddKeyedTransient<IConfigurationService, PrintingConfigurationService>("Printing");
                services.AddKeyedTransient<IConfigurationService, VbsStateConfigurationService>("VbsState");
                services.AddKeyedTransient<IConfigurationService, ExtractContextMenuConfigurationService>("ExtractContextMenu");
                services.AddKeyedTransient<IConfigurationService, CpuIdleConfigurationService>("CpuIdle");
                services.AddKeyedTransient<IConfigurationService, OldContextMenuConfigurationService>("OldContextMenu");
                services.AddKeyedTransient<IConfigurationService, EdgeSwipeConfigurationService>("EdgeSwipe");
                services.AddKeyedTransient<IConfigurationService, AppIconsThumbnailConfigurationService>("AppIconsThumbnail");
                services.AddKeyedTransient<IConfigurationService, AutomaticFolderDiscoveryConfigurationService>("AutomaticFolderDiscovery");
                services.AddKeyedTransient<IConfigurationService, GalleryConfigurationService>("Gallery");
                services.AddKeyedTransient<IConfigurationService, SnapLayoutsConfigurationService>("SnapLayout");
                services.AddKeyedTransient<IConfigurationService, RecentItemsConfigurationService>("RecentItems");
                services.AddKeyedTransient<IConfigurationService, VerboseStatusMessageConfiguarationServices>("VerboseStatusMessage");
                services.AddKeyedTransient<IConfigurationService, NvidiaDispayContainerConfigurationService>("NvidiaDispayContainer");
                services.AddKeyedTransient<IConfigurationService, AddNvidiaDisplayContainerContextMenuConfigurationService>("AddNvidiaDisplayContainerContextMenu");
                services.AddKeyedTransient<IConfigurationService, HideAppBrowserControlConfigurationService>("HideAppBrowserControl");
                services.AddKeyedTransient<IConfigurationService, SecurityHealthTrayConfigurationService>("SecurityHealthTray");
                services.AddKeyedTransient<IConfigurationService, FaultTolerantHeapConfigurationService>("FaultTolerantHeap");
                services.AddKeyedTransient<IConfigurationService, GiveAccessToMenuConfigurationService>("GiveAccessToMenu");
                services.AddKeyedTransient<IConfigurationService, NetworkNavigationPaneConfigurationService>("NetworkNavigationPane");
                services.AddKeyedTransient<IConfigurationService, ToggleWindowsUpdateConfigurationService>("ToggleWindowsUpdates");
                services.AddKeyedTransient<IMultiOptionConfigurationServices, ContextMenuTerminalsConfigurationService>("ContextMenuTerminals");
                services.AddKeyedTransient<IMultiOptionConfigurationServices, ShortcutIconConfigurationService>("ShortcutIcon");
                services.AddKeyedTransient<IMultiOptionConfigurationServices, MitigationsConfigurationService>("Mitigations");
                services.AddKeyedTransient<IMultiOptionConfigurationServices, SafeModeConfigurationService>("SafeMode");
            });
            App.logger.Info($"[SERVICES] Added services to host");
            return host;
        }

        /// <summary>
        /// Registers Configuration sub menus
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationMenus(this IHostBuilder host)
        {
            host.ConfigureServices((_,services) =>
            {
                services.AddKeyedTransient<IConfigurationSubMenu, ContextMenuSubMenu>("ContextMenuSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, ServicesSubMenu>("ServicesSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, BootConfigurationSubMenu>("BootConfigurationSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, FileExplorerSubMenu>("FileExplorerSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, StartMenuSubMenu>("StartMenuSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, BootMenuAppearance>("BootConfigAppearance");
                services.AddKeyedTransient<IConfigurationSubMenu, BootConfigBehavior>("BootConfigBehavior");
                services.AddKeyedTransient<IConfigurationSubMenu, DriverConfigurationSubMenu>("DriverConfigurationSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, NvidiaDisplayContainerSubMenu>("NvidiaDisplayContainerSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, CoreIsolationSubMenu>("CoreIsolationSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, DefenderSubMenu>("DefenderSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, MitigationsSubMenu>("MitigationsSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, TroubleshootingNetworkSubMenu>("TroubleshootingNetwork");
                services.AddKeyedTransient<IConfigurationSubMenu, FileSharingSubMenu>("FileSharingSubMenu");
                services.AddKeyedTransient<IConfigurationSubMenu, WindowsUpdateSubMenu>("WindowsUpdate");
            });
            App.logger.Info($"[SERVICES] Added submenu services to host");
            return host;
        }
    }
}
