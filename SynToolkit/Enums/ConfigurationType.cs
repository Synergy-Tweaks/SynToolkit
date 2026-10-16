using System;
using System.ComponentModel;

namespace SynToolkit.Enums
{
    public enum ConfigurationType
    {
        [Description("General Configuration")]
        General,
        [Description("Tweaks")]
        Tweaks,
        [Description("Interface Tweaks")]
        Interface,
        [Description("Windows Settings")]
        Windows,
        [Description("Advanced Configuration")]
        Advanced,
        [Description("Security")]
        Security,
        [Description("Restoration")]
        Troubleshooting,
        [Description("Software")]
        Software,
        ContextMenuSubMenu,
        AiSubMenu,
        ServicesSubMenu,
        CpuIdleSubMenu,
        BootConfigurationSubMenu,
        FileExplorerSubMenu,
        StartMenuSubMenu,
        BootConfigAppearance,
        BootConfigBehavior,
        DriverConfigurationSubMenu,
        NvidiaDisplayContainerSubMenu,
        CoreIsolationSubMenu,
        DefenderSubMenu,
        MitigationsSubMenu,
        TroubleshootingNetwork,
        FileSharingSubMenu,
        WindowsUpdate,
        TweaksPerformanceSubMenu,
        TweaksNetworkPowerSubMenu,
        TweaksPrivacySubMenu,
        TweaksInterfaceSubMenu,
        AmdGpuTweaksSubMenu,
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
