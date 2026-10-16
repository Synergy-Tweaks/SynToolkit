using System;
using System.ComponentModel;

namespace SynToolkit.Enums
{
    public enum ConfigurationType
    {
        [Description("General Configuration")]
        General = 0,
        [Description("Tweaks")]
        Tweaks = 1,
        [Description("Interface Tweaks")]
        Interface = 2,
        [Description("Windows Settings")]
        Windows = 3,
        [Description("Advanced Configuration")]
        Advanced = 4,
        [Description("Security")]
        Security = 5,
        [Description("Restoration")]
        Troubleshooting = 6,
        [Description("Software")]
        Software = 7,
        ContextMenuSubMenu = 8,
        AiSubMenu = 9,
        ServicesSubMenu = 10,
        CpuIdleSubMenu = 11,
        BootConfigurationSubMenu = 12,
        FileExplorerSubMenu = 13,
        StartMenuSubMenu = 14,
        BootConfigAppearance = 15,
        BootConfigBehavior = 16,
        DriverConfigurationSubMenu = 17,
        NvidiaDisplayContainerSubMenu = 18,
        CoreIsolationSubMenu = 19,
        MitigationsSubMenu = 21,
        TroubleshootingNetwork = 22,
        FileSharingSubMenu = 23,
        WindowsUpdate = 24,
        TweaksPerformanceSubMenu = 25,
        TweaksNetworkPowerSubMenu = 26,
        TweaksPrivacySubMenu = 27,
        TweaksInterfaceSubMenu = 28,
        AmdGpuTweaksSubMenu = 29,
    }

    public static class EnumExtensions
    {
        /// <summary>
        /// Returns the description of an Enum
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetDescription(this Enum value)
        {
            switch (value)
            {
                case ConfigurationType.General:
                    return App.GetValueFromItemList("GeneralConfig");

                case ConfigurationType.Tweaks:
                    return App.GetValueFromItemList("Tweaks");

                case ConfigurationType.Interface:
                    return App.GetValueFromItemList("Interface");

                case ConfigurationType.Windows:
                    return App.GetValueFromItemList("Windows");

                case ConfigurationType.Advanced:
                    return App.GetValueFromItemList("Advanced");

                case ConfigurationType.Security:
                    return App.GetValueFromItemList("Security");

                case ConfigurationType.Troubleshooting:
                    return App.GetValueFromItemList("Troubleshooting");
                default: return null;
            }
        }
    }
}
