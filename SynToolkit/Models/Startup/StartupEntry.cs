#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;

namespace SynToolkit.Models.Startup
{
    public enum StartupCategory
    {
        Registry,
        StartupFolder,
        ScheduledTask,
        Service
    }

    public enum RiskLevel
    {
        Safe,
        Low,
        Medium,
        High,
        Unknown
    }

    public partial class StartupEntry : ObservableObject
    {
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string FilePath { get; set; } = "";
        public StartupCategory Category { get; set; }
        public RiskLevel Risk { get; set; }
        public string Description { get; set; } = "";
        public string StartupDelay { get; set; } = "None";
        public bool RequiresAdministrator { get; set; }

        [ObservableProperty]
        public partial string Command { get; set; } = "";

        [ObservableProperty]
        public partial string Location { get; set; } = "";

        [ObservableProperty]
        public partial bool IsEnabled { get; set; }

        public string CategoryLabel => Category switch
        {
            StartupCategory.Registry => App.GetValueFromItemList("Startup_Registry"),
            StartupCategory.StartupFolder => App.GetValueFromItemList("Startup_StartupFolder"),
            StartupCategory.ScheduledTask => App.GetValueFromItemList("Startup_ScheduledTask"),
            StartupCategory.Service => App.GetValueFromItemList("Startup_Service"),
            _ => App.GetValueFromItemList("Startup_UnknownPublisher")
        };

        public string RiskLabel => Risk switch
        {
            RiskLevel.Safe => App.GetValueFromItemList("Startup_RiskSafe"),
            RiskLevel.Low => App.GetValueFromItemList("Startup_RiskLow"),
            RiskLevel.Medium => App.GetValueFromItemList("Startup_RiskMedium"),
            RiskLevel.High => App.GetValueFromItemList("Startup_RiskHigh"),
            _ => App.GetValueFromItemList("Startup_RiskUnknown")
        };

        public string PublisherText => Publisher == "Unknown"
            ? App.GetValueFromItemList("Startup_UnknownPublisher")
            : Publisher;

        public string DelayText => StartupDelay == "On Boot"
            ? App.GetValueFromItemList("Startup_OnBoot")
            : App.GetValueFromItemList("Startup_None");

        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

        public bool IsSensitive =>
            Category == StartupCategory.Service ||
            Publisher.Contains("Microsoft", System.StringComparison.OrdinalIgnoreCase) ||
            Publisher.Contains("Windows", System.StringComparison.OrdinalIgnoreCase) ||
            FilePath.Contains(@"\Windows\System32", System.StringComparison.OrdinalIgnoreCase) ||
            FilePath.Contains(@"\Windows\SysWOW64", System.StringComparison.OrdinalIgnoreCase);
    }
}
