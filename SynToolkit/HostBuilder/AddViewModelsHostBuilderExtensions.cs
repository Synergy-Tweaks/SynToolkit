using SynToolkit.Services.ConfigurationServices;
using SynToolkit.Models;
using SynToolkit.Stores;
using SynToolkit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SynToolkit.Enums;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Graphics.Canvas.Text;
using System.Linq;
using System.Threading.Tasks;
using SynToolkit.Commands;
using System.Windows.Input;
using Windows.Security.Cryptography.Core;
using Windows.Devices.WiFi;
using SynToolkit.Commands.ConfigurationButtonsCommand;
using SynToolkit.Services;
using SynToolkit.Utils;
using SynToolkit.Models.ProfileModels;
using Newtonsoft.Json;

namespace SynToolkit.HostBuilder
{
    public static class AddViewModelsHostBuilderExtensions
    {
        private static List<Object> subMenuOnlyItems = new List<Object>();
        private static Dictionary<string, string> list = new Dictionary<string, string>();
        public static IHostBuilder AddViewModels(this IHostBuilder host)
        {
            host.ConfigureServices((_, services) =>
            {
                services.AddTransient(CreateConfigPageViewModel);
                services.AddTransient(CreateHomePageViewModel);
                services.AddTransient(CreateAppFetchPageViewModel);
                services.AddTransient<GpuPageViewModel>();
                services.AddTransient<SpecsPageViewModel>();
                services.AddTransient<CleanerPageViewModel>();
            });

            host.AddConfigurationButtonItemViewModels();
            host.AddLinksItemViewModels();
            host.AddMultiOptionConfigurationViewModels();
            host.AddConfigurationItemViewModels();
            host.AddConfigurationSubMenu();
            host.AddProfiles();

            App.logger.Info($"[VMHostBuilder] Successfully loaded host");
            return host;
        }


        //private static string App.App.GetValueFromItemList(string key, bool desc = false)
        //{
        //    if (!desc) return list.Where(item => item.Key == key).Select(item => item.Value).FirstOrDefault();
        //    else return list.Where(item => item.Key == key + "Description").Select(item => item.Value).FirstOrDefault();
        //}

        /// <summary>
        /// Regsiters profiles from the profile folder
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddProfiles(this IHostBuilder host)
        {
            // This is a copy-only, one-time migration that runs once at startup so
            // migrated profiles are visible to every later disk scan. It never
            // resolves or applies any configuration service. The profile list itself
            // is no longer captured here: CreateHomePageViewModel re-scans the
            // Profiles folder on every HomePageViewModel construction instead, so
            // profiles added/removed during a session don't disappear when the user
            // navigates away from Home and back (HomePageViewModel is Transient).
            LegacyProfileMigrationService.TryMigrateAtStartup();
            return host;
        }

        /// <summary>
        /// Registers links
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddLinksItemViewModels(this IHostBuilder host)
        {
            Dictionary<string, Links> configurationDictionary = new()
            {
                ["ExplorerPatcher"] = new("https://github.com/valinet/ExplorerPatcher", "ExplorerPatcher", ConfigurationType.StartMenuSubMenu),
                ["StartAllBack"] = new("https://www.startallback.com/", "StartAllBack", ConfigurationType.StartMenuSubMenu),
                ["OpenShellDocumentation"] = new(@"https://github.com/Open-Shell/Open-Shell-Menu", App.GetValueFromItemList("OpenShellDocumentation"), ConfigurationType.StartMenuSubMenu),

                ["ActivationPage"] = new(@"ms-settings:activation", App.GetValueFromItemList("ActivationPage"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Windows.png"),
                ["ColorsPage"] = new(@"ms-settings:personalization-colors", App.GetValueFromItemList("ColorsPage"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Color.png"),
                ["DateAndTime"] = new(@"ms-settings:dateandtime", App.GetValueFromItemList("DateAndTime"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Windows.png"),
                ["DefaultApps"] = new(@"ms-settings:defaultapps", App.GetValueFromItemList("DefaultApps"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Windows.png"),
                ["DefaultGraphicsSettings"] = new(@"ms-settings:display-advancedgraphics-default", App.GetValueFromItemList("DefaultGraphicsSettings"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Gpu.png"),
                ["RegionLanguage"] = new(@"ms-settings:regionlanguage", App.GetValueFromItemList("RegionLanguage"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Windows.png"),
                ["Privacy"] = new(@"ms-settings:privacy", App.GetValueFromItemList("Privacy"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Security.png"),
                ["RegionProperties"] = new(@"ms-settings:regionProperties", App.GetValueFromItemList("RegionProperties"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Windows.png"),
                ["Taskbar"] = new(@"ms-settings:taskbar", App.GetValueFromItemList("Taskbar"), ConfigurationType.Windows, "ms-appx:///assets/Icons/Windows.png"),
                ["CoreIsolation"] = new(@"windowsdefender://coreisolation/", App.GetValueFromItemList("CoreIsolation"), ConfigurationType.CoreIsolationSubMenu, "ms-appx:///assets/Icons/Security.png"),

                ["AutoGpuAffinity"] = new(@"https://github.com/valleyofdoom/AutoGpuAffinity", "AutoGpuAffinity", ConfigurationType.DriverConfigurationSubMenu),
                ["GoInterruptPolicy"] = new(@"https://github.com/spddl/GoInterruptPolicy", "GoInterruptPolicy", ConfigurationType.DriverConfigurationSubMenu),
                ["InterrupAffinityTool"] = new(@"https://www.techpowerup.com/download/microsoft-interrupt-affinity-tool", App.GetValueFromItemList("InterrupAffinityTool"), ConfigurationType.DriverConfigurationSubMenu),
                ["MSIUtilityV3"] = new(@"https://forums.guru3d.com/threads/windows-line-based-vs-message-signaled-based-interrupts-msi-tool.378044", "MSI Utility V3", ConfigurationType.DriverConfigurationSubMenu),
            };

            host.ConfigureServices((_, services) =>
            {
                services.AddSingleton<IEnumerable<LinksViewModel>>(provider =>
                {
                    List<LinksViewModel> viewModels = new();

                    foreach (KeyValuePair<string, Links> item in configurationDictionary)
                    {
                        viewModels.Add(CreateLinksViewModel(item.Value));
                    }
                    App.logger.Info($"[VMHostBuilder] Successfully loaded {viewModels.Count} link entries");
                    return viewModels;
                });
            });
            return host;
        }

        /// <summary>
        /// Registers configuration buttons
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationButtonItemViewModels(this IHostBuilder host)
        {
            ICommand buttonCommand;
            Dictionary<string, ConfigurationButton> configurationDictionary = new()
            {
                ["RestartExplorerButton"] = new(buttonCommand = new RestartExplorerCommand(), App.GetValueFromItemList("RestartExplorerButton"), App.GetValueFromItemList("RestartExplorerButton", true), ConfigurationType.Interface, "ms-appx:///assets/Icons/Windows.png"),
                ["ViewCurrentSettingsBootConfig"] = new(buttonCommand = new ViewCurrentValuesCommand(), App.GetValueFromItemList("ViewCurrentSettingsBootConfig"), App.GetValueFromItemList("ViewCurrentSettingsBootConfig", true), ConfigurationType.BootConfigurationSubMenu, "ms-appx:///assets/Icons/Bios.png"),
                ["RestartToBios"] = new(buttonCommand = new RestartToBiosCommand(), App.GetValueFromItemList("RestartToBios"), App.GetValueFromItemList("RestartToBiosDesc"), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Bios.png"),
                ["HWiNFO"] = new(buttonCommand = new LaunchPortableToolCommand("HWiNFO64.exe"), App.GetValueFromItemList("HWiNFO"), App.GetValueFromItemList("HWiNFODescription"), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Ram.png"),
                ["CPUZ"] = new(buttonCommand = new LaunchPortableToolCommand("cpuz_x64.exe"), App.GetValueFromItemList("CPUZ"), App.GetValueFromItemList("CPUZDescription"), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Cpu.png"),
                ["GPUZ"] = new(buttonCommand = new LaunchPortableToolCommand("GPU-Z.2.70.0.exe"), App.GetValueFromItemList("GPUZ"), App.GetValueFromItemList("GPUZDescription"), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Gpu.png"),
                ["CrystalDiskMark"] = new(buttonCommand = new LaunchPortableToolCommand(@"CrystalDiskMark\DiskMark64.exe"), App.GetValueFromItemList("CrystalDiskMark"), App.GetValueFromItemList("CrystalDiskMarkDescription"), ConfigurationType.Advanced, "ms-appx:///assets/Icons/SSD.png"),
                ["EmptyStandbyList"] = new(buttonCommand = new LaunchPortableToolCommand("EmptyStandbyList.exe", ["standbylist"], App.GetValueFromItemList("EmptyStandbyList")), App.GetValueFromItemList("EmptyStandbyList"), App.GetValueFromItemList("EmptyStandbyListDescription"), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Ram.png"),
                ["VBSCurrentConfig"] = new(buttonCommand = new CurrentVBSConfigurationCommand(), App.GetValueFromItemList("VBSCurrentConfig"), App.GetValueFromItemList("VBSCurrentConfig", true), ConfigurationType.CoreIsolationSubMenu, "ms-appx:///assets/Icons/Security.png"),
                ["ToggleDefender"] = new(buttonCommand = new ToggleDefenderCommand(), App.GetValueFromItemList("ToggleDefender"), App.GetValueFromItemList("ToggleDefender", true), ConfigurationType.DefenderSubMenu, "ms-appx:///assets/Icons/Defender.png"),
                ["ResetFTH"] = new(buttonCommand = new ResetFTHCommand(), App.GetValueFromItemList("ResetFTH"), App.GetValueFromItemList("ResetFTH", true), ConfigurationType.MitigationsSubMenu, "ms-appx:///assets/Icons/Update.png"),
                ["InstallOpenShell"] = new(buttonCommand = new InstallOpenShellCommand(), App.GetValueFromItemList("InstallOpenShell"), App.GetValueFromItemList("InstallOpenShell", true), ConfigurationType.StartMenuSubMenu, "ms-appx:///assets/Icons/Windows.png"),

                ["DiskCleanup"] = new(buttonCommand = new DiskCleanupCommand(), App.GetValueFromItemList("DiskCleanup"), App.GetValueFromItemList("DiskCleanup", true), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/DiskCleanup.png"),
                ["SystemRestore"] = new(buttonCommand = new OpenSystemProtectionCommand(), App.GetValueFromItemList("SystemRestore"), App.GetValueFromItemList("SystemRestore", true), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/Security.png"),
                ["RepairWindowsInstaller"] = new(buttonCommand = new RepairWindowsInstallerCommand(), App.GetValueFromItemList("FixErrors"), App.GetValueFromItemList("RepairWindowsInstaller"), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/Update.png"),
                ["RepairWinComponent"] = new(buttonCommand = new RepairWindowsComponentsCommand(), App.GetValueFromItemList("FixErrors"), App.GetValueFromItemList("RepairWinComponent"), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/Update.png"),
                ["TelemetryComponents"] = new(buttonCommand = new TelemetryComponentsCommand(), App.GetValueFromItemList("FixErrors"), App.GetValueFromItemList("TelemetryComponents"), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/Update.png"),
                ["WindowsDefault"] = new(buttonCommand = new NetworkWindowsDefaults(), App.GetValueFromItemList("WindowsDefault"), App.GetValueFromItemList("WindowsDefault", true), ConfigurationType.TroubleshootingNetwork, "ms-appx:///assets/Icons/Internet.png"),
                ["SetUpdateDeferral"] = new(buttonCommand = new SetUpdateDeferralConfigurationButton(), App.GetValueFromItemList("Set"), App.GetValueFromItemList("WindowsUpdateDeferral"), ConfigurationType.WindowsUpdate, "ms-appx:///assets/Icons/Update.png"),
                ["ResetUpdateDeferral"] = new(buttonCommand = new ResetWindowsUpdateDeferral(), App.GetValueFromItemList("ResetFTH"), App.GetValueFromItemList("ResetWindowsUpdateDeferral"), ConfigurationType.WindowsUpdate, "ms-appx:///assets/Icons/Update.png"),
            };

            host.ConfigureServices((_, services) =>
            {
                services.AddSingleton<IEnumerable<ConfigurationButtonViewModel>>(provider =>
                {
                    List<ConfigurationButtonViewModel> viewModels = new();

                    foreach (KeyValuePair<string, ConfigurationButton> item in configurationDictionary)
                    {
                        viewModels.Add(CreateButtonViewModel(item.Value));
                    }
                    App.logger.Info($"[VMHostBuilder] Successfully loaded {viewModels.Count} button entries");
                    return viewModels;
                });
            });
            return host;
        }

        /// <summary>
        /// Registers sub-menus
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationSubMenu(this IHostBuilder host)
        {
            Dictionary<string, ConfigurationSubMenu> configurationDictionary = new()
            {
                ["BootConfigAppearance"] = new("BootConfigAppearance", App.GetValueFromItemList("BootConfigAppearance"), App.GetValueFromItemList("BootConfigAppearance", true), ConfigurationType.BootConfigurationSubMenu, "ms-appx:///assets/Icons/Display.png"),
                ["BootConfigBehavior"] = new("BootConfigBehavior", App.GetValueFromItemList("BootConfigBehavior"), App.GetValueFromItemList("BootConfigBehavior", true), ConfigurationType.BootConfigurationSubMenu, "ms-appx:///assets/Icons/Bios.png"),
                ["NvidiaDisplayContainerSubMenu"] = new("NvidiaDisplayContainerSubMenu", App.GetValueFromItemList("NvidiaDisplayContainerSubMenu"), App.GetValueFromItemList("NvidiaDisplayContainerSubMenu", true), ConfigurationType.ServicesSubMenu, "ms-appx:///assets/Icons/Nvidia.png"),

                ["StartMenuSubMenu"] = new("StartMenuSubMenu", App.GetValueFromItemList("StartMenuSubMenu"), App.GetValueFromItemList("StartMenuSubMenu", true), ConfigurationType.Interface, "ms-appx:///assets/Icons/Windows.png"),
                ["ContextMenuSubMenu"] = new("ContextMenuSubMenu", App.GetValueFromItemList("ContextMenuSubMenu"), App.GetValueFromItemList("ContextMenuSubMenu", true), ConfigurationType.Interface, "ms-appx:///assets/Icons/Windows.png"),
                ["ServicesSubMenu"] = new("ServicesSubMenu", App.GetValueFromItemList("ServicesSubMenu"), App.GetValueFromItemList("ServicesSubMenu", true), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/Services.png"),
                ["BootConfigurationSubMenu"] = new("BootConfigurationSubMenu", App.GetValueFromItemList("BootConfigurationSubMenu"), App.GetValueFromItemList("BootConfigurationSubMenu", true), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Bios.png"),
                ["FileExplorerSubMenu"] = new("FileExplorerSubMenu", App.GetValueFromItemList("FileExplorerSubMenu"), App.GetValueFromItemList("FileExplorerSubMenu", true), ConfigurationType.Interface, "ms-appx:///assets/Icons/explorer.png"),
                ["DriverConfigurationSubMenu"] = new("DriverConfigurationSubMenu", App.GetValueFromItemList("DriverConfigurationSubMenu"), App.GetValueFromItemList("DriverConfigurationSubMenu", true), ConfigurationType.Advanced, "ms-appx:///assets/Icons/Devices.png"),
                ["CoreIsolationSubMenu"] = new("CoreIsolationSubMenu", App.GetValueFromItemList("CoreIsolationSubMenu"), App.GetValueFromItemList("CoreIsolationSubMenu", true), ConfigurationType.Security, "ms-appx:///assets/Icons/Security.png"),
                ["DefenderSubMenu"] = new("DefenderSubMenu", App.GetValueFromItemList("DefenderSubMenu"), App.GetValueFromItemList("DefenderSubMenu", true), ConfigurationType.Security, "ms-appx:///assets/Icons/Defender.png"),
                ["MitigationsSubMenu"] = new("MitigationsSubMenu", App.GetValueFromItemList("MitigationsSubMenu"), App.GetValueFromItemList("MitigationsSubMenu", true), ConfigurationType.Security, "ms-appx:///assets/Icons/Security.png"),
                ["TroubleshootingNetwork"] = new("TroubleshootingNetwork", App.GetValueFromItemList("TroubleshootingNetwork"), App.GetValueFromItemList("TroubleshootingNetwork", true), ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/Internet.png"),
                ["FileSharingSubMenu"] = new("FileSharingSubMenu", App.GetValueFromItemList("FileSharingSubMenu"), App.GetValueFromItemList("FileSharingSubMenu", true), ConfigurationType.General, "ms-appx:///assets/Icons/Internet.png"),
                ["WindowsUpdate"] = new("WindowsUpdate", App.GetValueFromItemList("WindowsUpdate"), App.GetValueFromItemList("WindowsUpdate", true), ConfigurationType.General, "ms-appx:///assets/Icons/Update.png"),
            };
            host.ConfigureServices((_, services) =>
            {
                services.AddSingleton<IEnumerable<ConfigurationSubMenuViewModel>>(provider =>
                {
                    List<ConfigurationSubMenuViewModel> viewModels = new();
                    foreach (KeyValuePair<string, ConfigurationSubMenu> subMenu in configurationDictionary)
                    {
                        ObservableCollection<ConfigurationItemViewModel> itemViewModels = new ObservableCollection<ConfigurationItemViewModel>(provider.GetServices<ConfigurationItemViewModel>().Where(item => item.Type.ToString() == subMenu.Key));
                        ObservableCollection<MultiOptionConfigurationItemViewModel> multiOptionItemViewModels = new ObservableCollection<MultiOptionConfigurationItemViewModel>(provider.GetServices<MultiOptionConfigurationItemViewModel>().Where(item => item.Type.ToString() == subMenu.Key));
                        ObservableCollection<LinksViewModel> linksViewModel = new ObservableCollection<LinksViewModel>(provider.GetServices<LinksViewModel>().Where(item => item.Type.ToString() == subMenu.Key));
                        ObservableCollection<ConfigurationSubMenuViewModel> configurationSubMenuViewModels = new ObservableCollection<ConfigurationSubMenuViewModel>(viewModels.Where(item => item.Type.ToString() == subMenu.Key));
                        ObservableCollection<ConfigurationButtonViewModel> configurationButtonViewModels = new ObservableCollection<ConfigurationButtonViewModel>(provider.GetServices<ConfigurationButtonViewModel>().Where(item => item.Type.ToString() == subMenu.Key));

                        ConfigurationSubMenuViewModel viewModel = CreateConfigurationSubMenuViewModel(provider, itemViewModels, multiOptionItemViewModels, linksViewModel, subMenu.Key, subMenu.Value, configurationSubMenuViewModels, configurationButtonViewModels);
                        viewModels.Add(viewModel);
                    }
                    App.logger.Info($"[VMHostBuilder] Successfully loaded {viewModels.Count} submenu entries");
                    return viewModels;
                });
            });

            return host;
        }


        /// <summary>
        /// Registers multioption configuration services
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddMultiOptionConfigurationViewModels(this IHostBuilder host)
        {
            // TODO: Change configuration types
            Dictionary<string, MultiOptionConfiguration> configurationDictionary = new()
            {
                ["ContextMenuTerminals"] = new(App.GetValueFromItemList("ContextMenuTerminals"), "ContextMenuTerminals", ConfigurationType.ContextMenuSubMenu, "ms-appx:///assets/Icons/Windows.png"),
                ["ShortcutIcon"] = new(App.GetValueFromItemList("ShortcutIcon"), "ShortcutIcon", ConfigurationType.Interface, "ms-appx:///assets/Icons/Windows.png"),
                ["Mitigations"] = new(App.GetValueFromItemList("Mitigations"), "Mitigations", ConfigurationType.MitigationsSubMenu, "ms-appx:///assets/Icons/Security.png"),
                ["SafeMode"] = new(App.GetValueFromItemList("SafeMode"), "SafeMode", ConfigurationType.Troubleshooting, "ms-appx:///assets/Icons/SafeMode.png"),
            };

            host.ConfigureServices((_, services) =>
            {
                services.AddSingleton<IEnumerable<MultiOptionConfigurationItemViewModel>>(provider =>
                {
                    List<MultiOptionConfigurationItemViewModel> viewModels = new();

                    foreach (KeyValuePair<string, MultiOptionConfiguration> item in configurationDictionary)
                    {
                        viewModels.Add(CreateMultiOptionConfigurationItemViewModel(provider, item.Key, item.Value));
                    }
                    App.logger.Info($"[VMHostBuilder] Successfully loaded {viewModels.Count} multi-configuration entries");
                    return viewModels;
                });
            });
            return host;
        }

        /// <summary>
        /// Registers configuration items
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static IHostBuilder AddConfigurationItemViewModels(this IHostBuilder host)
        {
            // TODO: Change configuration types`
            Dictionary<string, Configuration> configurationDictionary = new()
            {
                ["Animations"] = new(App.GetValueFromItemList("Animations"), "Animations", ConfigurationType.Interface),
                ["ExtractContextMenu"] = new(App.GetValueFromItemList("ExtractContextMenu"), "ExtractContextMenu", ConfigurationType.ContextMenuSubMenu),
                ["RunWithPriority"] = new(App.GetValueFromItemList("RunWithPriority"), "RunWithPriority", ConfigurationType.ContextMenuSubMenu),
                ["Bluetooth"] = new(
                    App.GetValueFromItemList("Bluetooth"),
                    "Bluetooth",
                    ConfigurationType.Troubleshooting,
                    "ms-appx:///assets/Icons/Bluetooth.png"),
                ["XboxServices"] = new(
                    App.GetValueFromItemList("XboxServices"),
                    "XboxServices",
                    ConfigurationType.Troubleshooting,
                    "ms-appx:///assets/Icons/Games.png"),
                ["WiFi"] = new(
                    App.GetValueFromItemList("WiFi"),
                    "WiFi",
                    ConfigurationType.Troubleshooting,
                    "ms-appx:///assets/Icons/Internet.png"),
                ["Printing"] = new(
                    App.GetValueFromItemList("Printing"),
                    "Printing",
                    ConfigurationType.Troubleshooting,
                    "ms-appx:///assets/Icons/Devices.png"),
                ["VbsState"] = new(
                    App.GetValueFromItemList("VbsState"),
                    "VbsState",
                    ConfigurationType.CoreIsolationSubMenu,
                    "ms-appx:///assets/Icons/Security.png"),
                ["LanmanWorkstation"] = new(App.GetValueFromItemList("LanmanWorkstation"), "LanmanWorkstation", ConfigurationType.ServicesSubMenu),
                ["NvidiaDispayContainer"] = new(App.GetValueFromItemList("NvidiaDispayContainer"), "NvidiaDispayContainer", ConfigurationType.NvidiaDisplayContainerSubMenu),
                ["AddNvidiaDisplayContainerContextMenu"] = new(App.GetValueFromItemList("AddNvidiaDisplayContainerContextMenu"), "AddNvidiaDisplayContainerContextMenu", ConfigurationType.NvidiaDisplayContainerSubMenu),
                ["CpuIdleContextMenu"] = new(App.GetValueFromItemList("CpuIdleContextMenu"), "CpuIdleContextMenu", ConfigurationType.ContextMenuSubMenu),
                ["LockScreen"] = new(App.GetValueFromItemList("LockScreen"), "LockScreen", ConfigurationType.Interface),
                ["ShortcutText"] = new(App.GetValueFromItemList("ShortcutText"), "ShortcutText", ConfigurationType.Interface),
                ["BootLogo"] = new(App.GetValueFromItemList("BootLogo"), "BootLogo", ConfigurationType.BootConfigAppearance),
                ["BootMessages"] = new(App.GetValueFromItemList("BootMessages"), "BootMessages", ConfigurationType.BootConfigAppearance),
                ["NewBootMenu"] = new(App.GetValueFromItemList("NewBootMenu"), "NewBootMenu", ConfigurationType.BootConfigAppearance),
                ["SpinningAnimation"] = new(App.GetValueFromItemList("SpinningAnimation"), "SpinningAnimation", ConfigurationType.BootConfigAppearance),
                ["AdvancedBootOptions"] = new(App.GetValueFromItemList("AdvancedBootOptions"), "AdvancedBootOptions", ConfigurationType.BootConfigBehavior),
                ["AutomaticRepair"] = new(App.GetValueFromItemList("AutomaticRepair"), "AutomaticRepair", ConfigurationType.BootConfigBehavior),
                ["KernelParameters"] = new(App.GetValueFromItemList("KernelParameters"), "KernelParameters", ConfigurationType.BootConfigBehavior),
                ["HighestMode"] = new(App.GetValueFromItemList("HighestMode"), "HighestMode", ConfigurationType.BootConfigBehavior),
                ["CompactView"] = new(App.GetValueFromItemList("CompactView"), "CompactView", ConfigurationType.FileExplorerSubMenu),
                ["RemovableDrivesInSidebar"] = new(App.GetValueFromItemList("RemovableDrivesInSidebar"), "RemovableDrivesInSidebar", ConfigurationType.FileExplorerSubMenu),
                ["BackgroundApps"] = new(App.GetValueFromItemList("BackgroundApps"), "BackgroundApps", ConfigurationType.General),
                ["SearchIndexing"] = new(App.GetValueFromItemList("SearchIndexing"), "SearchIndexing", ConfigurationType.General),
                ["FsoAndGameBar"] = new(App.GetValueFromItemList("FsoAndGameBar"), "FsoAndGameBar", ConfigurationType.General),
                ["AutomaticUpdates"] = new(App.GetValueFromItemList("AutomaticUpdates"), "AutomaticUpdates", ConfigurationType.General),
                ["DeliveryOptimisation"] = new(App.GetValueFromItemList("DeliveryOptimisation"), "DeliveryOptimisation", ConfigurationType.General),
                ["Hibernation"] = new(App.GetValueFromItemList("Hibernation"), "Hibernation", ConfigurationType.General),
                ["Location"] = new(App.GetValueFromItemList("Location"), "Location", ConfigurationType.General),
                ["Sleep"] = new(App.GetValueFromItemList("Sleep"), "Sleep", ConfigurationType.General),
                ["UpdateNotifications"] = new(App.GetValueFromItemList("UpdateNotifications"), "UpdateNotifications", ConfigurationType.General),
                ["Widgets"] = new(App.GetValueFromItemList("Widgets"), "Widgets", ConfigurationType.General),
                ["AppStoreArchiving"] = new(App.GetValueFromItemList("AppStoreArchiving"), "AppStoreArchiving", ConfigurationType.General),
                ["OldContextMenu"] = new(App.GetValueFromItemList("OldContextMenu"), "OldContextMenu", ConfigurationType.ContextMenuSubMenu),
                ["EdgeSwipe"] = new(App.GetValueFromItemList("EdgeSwipe"), "EdgeSwipe", ConfigurationType.Interface),
                ["AppIconsThumbnail"] = new(App.GetValueFromItemList("AppIconsThumbnail"), "AppIconsThumbnail", ConfigurationType.FileExplorerSubMenu),
                ["AutomaticFolderDiscovery"] = new(App.GetValueFromItemList("AutomaticFolderDiscovery"), "AutomaticFolderDiscovery", ConfigurationType.FileExplorerSubMenu),
                ["Gallery"] = new(App.GetValueFromItemList("Gallery"), "Gallery", ConfigurationType.FileExplorerSubMenu),
                ["SnapLayout"] = new(App.GetValueFromItemList("SnapLayout"), "SnapLayout", ConfigurationType.Interface),
                ["RecentItems"] = new(App.GetValueFromItemList("RecentItems"), "RecentItems", ConfigurationType.Interface),
                ["VerboseStatusMessage"] = new(App.GetValueFromItemList("VerboseStatusMessage"), "VerboseStatusMessage", ConfigurationType.Interface),
                ["UAC"] = new(App.GetValueFromItemList("UAC"), "UAC", ConfigurationType.Security, "ms-appx:///assets/Icons/UserAccount.png"),
                ["HideAppBrowserControl"] = new(App.GetValueFromItemList("HideAppBrowserControl"), "HideAppBrowserControl", ConfigurationType.DefenderSubMenu),
                ["SecurityHealthTray"] = new(App.GetValueFromItemList("SecurityHealthTray"), "SecurityHealthTray", ConfigurationType.DefenderSubMenu),
                ["DefenderRealtimeProtection"] = new(App.GetValueFromItemList("DefenderRealtimeProtection"), "DefenderRealtimeProtection", ConfigurationType.DefenderSubMenu),
                ["MultiPlaneOverlay"] = new(App.GetValueFromItemList("MultiPlaneOverlay"), "MultiPlaneOverlay", ConfigurationType.Advanced),
                ["Hags"] = new(App.GetValueFromItemList("Hags"), "Hags", ConfigurationType.General, "ms-appx:///assets/Icons/Gpu.png"),
                ["WindowedGamesOptimization"] = new(App.GetValueFromItemList("WindowedGamesOptimization"), "WindowedGamesOptimization", ConfigurationType.General, "ms-appx:///assets/Icons/Games.png"),
                ["FaultTolerantHeap"] = new(App.GetValueFromItemList("FaultTolerantHeap"), "FaultTolerantHeap", ConfigurationType.MitigationsSubMenu),
                ["CpuIdle"] = new(App.GetValueFromItemList("CpuIdle"), "CpuIdle", ConfigurationType.General),
                ["GiveAccessToMenu"] = new(App.GetValueFromItemList("GiveAccessToMenu"), "GiveAccessToMenu", ConfigurationType.FileSharingSubMenu),
                ["NetworkNavigationPane"] = new(App.GetValueFromItemList("NetworkNavigationPane"), "NetworkNavigationPane", ConfigurationType.FileSharingSubMenu),
                ["ToggleWindowsUpdates"] = new(App.GetValueFromItemList("ToggleWindowsUpdates"), "ToggleWindowsUpdates", ConfigurationType.WindowsUpdate),
            };

            host.ConfigureServices((_, services) =>
            {
                services.AddSingleton<IEnumerable<ConfigurationItemViewModel>>(provider =>
                {
                    List<ConfigurationItemViewModel> viewModels = new();

                    foreach (KeyValuePair<string, Configuration> item in configurationDictionary)
                    {
                        //Could work, but needs to await for everything to be completed before returning viewModels
                        //Task.Run(() => { viewModels.Add(CreateConfigurationItemViewModel(provider, item.Key, item.Value)); });
                        viewModels.Add(CreateConfigurationItemViewModel(provider, item.Key, item.Value));
                    }
                    App.logger.Info($"[VMHostBuilder] Successfully loaded {viewModels.Count} configuration entries");
                    return viewModels;
                });
            });
            return host;
        }



        private static MultiOptionConfigurationItemViewModel CreateMultiOptionConfigurationItemViewModel(
            IServiceProvider serviceProvider, object key, MultiOptionConfiguration configuration)
        {
            MultiOptionConfigurationItemViewModel viewModel = new(
                configuration, serviceProvider.GetRequiredKeyedService<MultiOptionConfigurationStore>(key), serviceProvider.GetRequiredKeyedService<IMultiOptionConfigurationServices>(key));

            return viewModel;
        }

        private static ConfigurationItemViewModel CreateConfigurationItemViewModel(
            IServiceProvider serviceProvider, object key, Configuration configuration)
        {
            ConfigurationItemViewModel viewModel = new(
                configuration, serviceProvider.GetRequiredKeyedService<ConfigurationStore>(key), serviceProvider.GetRequiredKeyedService<IConfigurationService>(key));

            return viewModel;
        }

        #region Create ViewModels
        // Entire region is made to create view models
        private static ConfigurationButtonViewModel CreateButtonViewModel(ConfigurationButton configurationButtonViewModel)
        {
            ConfigurationButtonViewModel viewModel = new(configurationButtonViewModel);

            return viewModel;
        }

        private static LinksViewModel CreateLinksViewModel(Links linksItem)
        {
            LinksViewModel viewModel = new(linksItem);

            return viewModel;
        }

        private static ConfigPageViewModel CreateConfigPageViewModel(IServiceProvider serviceProvider)
        {
            return ConfigPageViewModel.LoadViewModel(
                serviceProvider.GetServices<LinksViewModel>(),
                serviceProvider.GetServices<ConfigurationItemViewModel>(),
                serviceProvider.GetServices<MultiOptionConfigurationItemViewModel>(),
                serviceProvider.GetServices<ConfigurationSubMenuViewModel>(),
                serviceProvider.GetServices<ConfigurationButtonViewModel>());
        }

        private static HomePageViewModel CreateHomePageViewModel(IServiceProvider serviceProvider)
        {
            return HomePageViewModel.LoadViewModel(
                ProfileSerializing.LoadProfilesFromDisk(),
                serviceProvider.GetServices<ConfigurationItemViewModel>(),
                serviceProvider.GetServices<MultiOptionConfigurationItemViewModel>());
        }
        private static AppFetchPageViewModel CreateAppFetchPageViewModel(IServiceProvider serviceProvider)
        {
            return new AppFetchPageViewModel(
                serviceProvider.GetRequiredService<AppFetchService>(),
                serviceProvider.GetRequiredService<WingetInstallerService>(),
                serviceProvider.GetRequiredKeyedService<IConfigurationService>("XboxServices"));
        }
        private static ConfigurationSubMenuViewModel CreateConfigurationSubMenuViewModel(
          IServiceProvider serviceProvider, ObservableCollection<ConfigurationItemViewModel> configurationItemViewModels, ObservableCollection<MultiOptionConfigurationItemViewModel> multiOptionConfigurationItemViewModel, ObservableCollection<LinksViewModel> linksViewModel, object key, ConfigurationSubMenu configuration, ObservableCollection<ConfigurationSubMenuViewModel> configurationSubMenuViewModel, ObservableCollection<ConfigurationButtonViewModel> configurationButtonViewModels)
        {
            ConfigurationStoreSubMenu configurationStoreSubMenu = serviceProvider.GetRequiredKeyedService<ConfigurationStoreSubMenu>(key);

            ConfigurationSubMenuViewModel viewModel = new(
               configuration, configurationStoreSubMenu, configurationItemViewModels, multiOptionConfigurationItemViewModel, linksViewModel, configurationSubMenuViewModel, configurationButtonViewModels);

            return viewModel;
        }
        #endregion Create ViewModels
    }
}
