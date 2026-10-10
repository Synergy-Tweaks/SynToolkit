using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SynToolkit.HostBuilder
{
    public static class AddStoresHostBuilderExtensions
    {
        public static IHostBuilder AddStores(this IHostBuilder host)
        {
            host.AddConfigurationStores();
            host.AddConfigurationMenu();

            return host;
        }

        /// <summary>
        /// Registers ConfigurationStores
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationStores(this IHostBuilder host)
        {
            host.ConfigureServices((_, services) =>
            {
                services.AddKeyedSingleton<ConfigurationStore>("Animations");
                services.AddKeyedSingleton<ConfigurationStore>("Bluetooth");
                services.AddKeyedSingleton<ConfigurationStore>("XboxServices");
                services.AddKeyedSingleton<ConfigurationStore>("FsoAndGameBar");
                services.AddKeyedSingleton<ConfigurationStore>("LanmanWorkstation");
                services.AddKeyedSingleton<ConfigurationStore>("SearchIndexing");
                services.AddKeyedSingleton<ConfigurationStore>("CpuIdleContextMenu");
                services.AddKeyedSingleton<ConfigurationStore>("PowerPlanShell");
                services.AddKeyedSingleton<ConfigurationStore>("LockScreen");
                services.AddKeyedSingleton<ConfigurationStore>("RunWithPriority");
                services.AddKeyedSingleton<ConfigurationStore>("ShortcutText");
                services.AddKeyedSingleton<ConfigurationStore>("BootLogo");
                services.AddKeyedSingleton<ConfigurationStore>("BootMessages");
                services.AddKeyedSingleton<ConfigurationStore>("NewBootMenu");
                services.AddKeyedSingleton<ConfigurationStore>("SpinningAnimation");
                services.AddKeyedSingleton<ConfigurationStore>("AdvancedBootOptions");
                services.AddKeyedSingleton<ConfigurationStore>("AutomaticRepair");
                services.AddKeyedSingleton<ConfigurationStore>("KernelParameters");
                services.AddKeyedSingleton<ConfigurationStore>("HighestMode");
                services.AddKeyedSingleton<ConfigurationStore>("CompactView");
                services.AddKeyedSingleton<ConfigurationStore>("RemovableDrivesInSidebar");
                services.AddKeyedSingleton<ConfigurationStore>("AutomaticUpdates");
                services.AddKeyedSingleton<ConfigurationStore>("BackgroundApps");
                services.AddKeyedSingleton<ConfigurationStore>("DeliveryOptimisation");
                services.AddKeyedSingleton<ConfigurationStore>("Hibernation");
                services.AddKeyedSingleton<ConfigurationStore>("Location");
                services.AddKeyedSingleton<ConfigurationStore>("Sleep");
                services.AddKeyedSingleton<ConfigurationStore>("AppStoreArchiving");
                services.AddKeyedSingleton<ConfigurationStore>("UpdateNotifications");
                services.AddKeyedSingleton<ConfigurationStore>("Widgets");
                services.AddKeyedSingleton<ConfigurationStore>("ExtractContextMenu");
                services.AddKeyedSingleton<ConfigurationStore>("OldContextMenu");
                services.AddKeyedSingleton<ConfigurationStore>("EdgeSwipe");
                services.AddKeyedSingleton<ConfigurationStore>("AppIconsThumbnail");
                services.AddKeyedSingleton<ConfigurationStore>("AutomaticFolderDiscovery");
                services.AddKeyedSingleton<ConfigurationStore>("Gallery");
                services.AddKeyedSingleton<ConfigurationStore>("SnapLayout");
                services.AddKeyedSingleton<ConfigurationStore>("RecentItems");
                services.AddKeyedSingleton<ConfigurationStore>("VerboseStatusMessage");
                services.AddKeyedSingleton<ConfigurationStore>("NvidiaDispayContainer");
                services.AddKeyedSingleton<ConfigurationStore>("AddNvidiaDisplayContainerContextMenu");
                services.AddKeyedSingleton<ConfigurationStore>("HideAppBrowserControl");
                services.AddKeyedSingleton<ConfigurationStore>("SecurityHealthTray");
                services.AddKeyedSingleton<ConfigurationStore>("FaultTolerantHeap");
                services.AddKeyedSingleton<ConfigurationStore>("CpuIdle");
                services.AddKeyedSingleton<ConfigurationStore>("GiveAccessToMenu");
                services.AddKeyedSingleton<ConfigurationStore>("NetworkNavigationPane");
                services.AddKeyedSingleton<ConfigurationStore>("ToggleWindowsUpdates");
                services.AddKeyedSingleton<ConfigurationStore>("MultiPlaneOverlay");
                services.AddKeyedSingleton<ConfigurationStore>("Hags");
                services.AddKeyedSingleton<ConfigurationStore>("WindowedGamesOptimization");
                services.AddKeyedSingleton<ConfigurationStore>("DefenderRealtimeProtection");
                services.AddKeyedSingleton<ConfigurationStore>("UsernameRequirement");
                services.AddKeyedSingleton<ConfigurationStore>("UAC");
                services.AddKeyedSingleton<ConfigurationStore>("WiFi");
                services.AddKeyedSingleton<ConfigurationStore>("Printing");
                services.AddKeyedSingleton<ConfigurationStore>("VbsState");
                services.AddKeyedSingleton<ConfigurationStore>("MemoryCompression");
                services.AddKeyedSingleton<ConfigurationStore>("SuperFetch");
                services.AddKeyedSingleton<ConfigurationStore>("SvcHostSplit");
                services.AddKeyedSingleton<ConfigurationStore>("NtfsOptimization");
                services.AddKeyedSingleton<ConfigurationStore>("DynamicTick");
                services.AddKeyedSingleton<ConfigurationStore>("DmaRemapping");
                services.AddKeyedSingleton<ConfigurationStore>("SleepStudy");
                services.AddKeyedSingleton<ConfigurationStore>("SettingsSync");
                services.AddKeyedSingleton<ConfigurationStore>("EnhancePointerPrecision");
                services.AddKeyedSingleton<ConfigurationStore>("LongPaths");
                services.AddKeyedSingleton<ConfigurationStore>("InstantMenus");
                services.AddKeyedSingleton<ConfigurationStore>("AutoEndTasks");
                services.AddKeyedSingleton<ConfigurationStore>("TransparencyEffects");
                services.AddKeyedSingleton<ConfigurationStore>("TaskViewButton");
                services.AddKeyedSingleton<ConfigurationStore>("SearchBoxTaskbar");
                services.AddKeyedSingleton<ConfigurationStore>("StartRecommended");
                services.AddKeyedSingleton<ConfigurationStore>("ShowFileExtensions");
                services.AddKeyedSingleton<ConfigurationStore>("AeroShake");
                services.AddKeyedSingleton<ConfigurationStore>("WallpaperQuality");
                services.AddKeyedSingleton<ConfigurationStore>("NetworkLatency");
                services.AddKeyedSingleton<ConfigurationStore>("PowerSaving");
                services.AddKeyedSingleton<ConfigurationStore>("TelemetryOptimizations");
                services.AddKeyedSingleton<ConfigurationStore>("DriverUpdates");
                services.AddKeyedSingleton<ConfigurationStore>("AmdDriverTelemetry");
                services.AddKeyedSingleton<ConfigurationStore>("AmdFeatureLatency");
                services.AddKeyedSingleton<ConfigurationStore>("AmdPowerPlay");
                services.AddKeyedSingleton<ConfigurationStore>("AmdUlps");
                services.AddKeyedSingleton<ConfigurationStore>("AmdAspm");
                services.AddKeyedSingleton<ConfigurationStore>("AmdClockGating");
                services.AddKeyedSingleton<ConfigurationStore>("AmdPowerGating");
                services.AddKeyedSingleton<ConfigurationStore>("AmdDisplayPower");
                services.AddKeyedSingleton<ConfigurationStore>("AmdSpreadSpectrum");
                services.AddKeyedSingleton<ConfigurationStore>("AmdCrashDefender");
                services.AddKeyedSingleton<MultiOptionConfigurationStore>("ContextMenuTerminals");
                services.AddKeyedSingleton<MultiOptionConfigurationStore>("ShortcutIcon");
                services.AddKeyedSingleton<MultiOptionConfigurationStore>("Mitigations");
                services.AddKeyedSingleton<MultiOptionConfigurationStore>("SafeMode");
                services.AddKeyedSingleton<MultiOptionConfigurationStore>("ProgramPriority");
                services.AddKeyedSingleton<MultiOptionConfigurationStore>("MmcssOptimization");
            });
            App.logger.Info($"[STORE] Added stores to host");
            return host;
        }

        /// <summary>
        /// Registers sub-menu ConfigurationStore 
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationMenu(this IHostBuilder host)
        {
            host.ConfigureServices((_, services) =>
            {
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("ContextMenuSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("ServicesSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("BootConfigurationSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("FileExplorerSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("StartMenuSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("BootConfigAppearance");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("BootConfigBehavior");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("DriverConfigurationSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("NvidiaDisplayContainerSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("CoreIsolationSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("DefenderSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("MitigationsSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("TroubleshootingNetwork");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("FileSharingSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("WindowsUpdate");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("TweaksPerformanceSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("TweaksNetworkPowerSubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("TweaksPrivacySubMenu");
                services.AddKeyedSingleton<ConfigurationStoreSubMenu>("TweaksInterfaceSubMenu");
            });
            App.logger.Info($"[STORE] Added submenu stores to host");

            return host;
        }
    }
}
