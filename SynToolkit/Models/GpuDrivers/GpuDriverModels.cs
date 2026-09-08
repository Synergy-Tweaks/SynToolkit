#nullable enable

namespace SynToolkit.Models.GpuDrivers;

public sealed class GpuDeviceInfo
{
    public string Name { get; init; } = "Unknown GPU";
    public string PciName { get; init; } = "Unknown PCI device";
    public string VendorId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string SubsystemVendorId { get; init; } = string.Empty;
    public string SubsystemDeviceId { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = "Unknown";
    public string DriverProvider { get; init; } = string.Empty;
    public string DriverInfName { get; init; } = string.Empty;
    public string PnpDeviceId { get; init; } = string.Empty;
    public bool IsNvidia => VendorId.Equals("10DE", StringComparison.OrdinalIgnoreCase);
    public bool IsAmd => VendorId.Equals("1002", StringComparison.OrdinalIgnoreCase);
    public bool IsTunable => IsNvidia || IsAmd;
    public bool IsMicrosoftBasicDisplayAdapter =>
        Name.Contains("Microsoft Basic Display Adapter", StringComparison.OrdinalIgnoreCase) ||
        DisplayName.Contains("Microsoft Basic Display Adapter", StringComparison.OrdinalIgnoreCase) ||
        DriverInfName.Contains("basicdisplay", StringComparison.OrdinalIgnoreCase);
    public bool HasInstalledDriver =>
        !IsMicrosoftBasicDisplayAdapter &&
        !string.IsNullOrWhiteSpace(DriverVersion) &&
        !DriverVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
        !DriverVersion.Equals("Not installed", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => string.IsNullOrWhiteSpace(PciName) || PciName == "Unknown PCI device"
        ? Name
        : PciName;
    public string PciIdText => string.IsNullOrWhiteSpace(VendorId) || string.IsNullOrWhiteSpace(DeviceId)
        ? "PCI ID unavailable"
        : $"VEN_{VendorId}&DEV_{DeviceId}";

    public override string ToString() => DisplayName;
}

public enum GpuDriverCatalogVendor
{
    Nvidia,
    Amd
}

public enum NvidiaDriverPlatform
{
    Unknown,
    Desktop,
    Notebook
}

public sealed class GpuDriverOption
{
    public GpuDriverCatalogVendor Vendor { get; init; }
    public string Name { get; init; } = "GPU Driver";
    public string Version { get; init; } = string.Empty;
    public string ReleaseDate { get; init; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string DetailUrl { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = "Windows";
    public string Type { get; init; } = string.Empty;
    public NvidiaDriverPlatform NvidiaPlatform { get; init; }

    public string PlatformText => NvidiaPlatform switch
    {
        NvidiaDriverPlatform.Notebook => "Notebook",
        NvidiaDriverPlatform.Desktop => "Desktop",
        _ => string.Empty
    };

    public string DisplayText => string.IsNullOrWhiteSpace(ReleaseDate)
        ? $"{Version} - {Name}"
        : $"{Version} - {ReleaseDate} - {Name}";

    public string DriverListSubtitle => string.Join(" - ", new[] { ReleaseDate, Type, PlatformText }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public bool HasDownloadUrl => !string.IsNullOrWhiteSpace(DownloadUrl);

    public override string ToString() => DisplayText;
}

public sealed class GpuDriverLookupResult
{
    public GpuDeviceInfo? Device { get; init; }
    public GpuDriverCatalogVendor Vendor { get; init; }
    public IReadOnlyList<GpuDriverOption> Drivers { get; init; } = [];
    public string Message { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string SeriesName { get; init; } = string.Empty;
    public NvidiaDriverPlatform NvidiaPlatform { get; init; }
    public bool Success => Drivers.Count > 0;
}

public enum GpuPackageComponentKind
{
    Package,
    DisplayDriver,
    ScheduledTask,
    Folder
}

public sealed class GpuPackageComponent : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public GpuPackageComponentKind Kind { get; init; } = GpuPackageComponentKind.Package;
    public bool IsDirectory { get; init; }
    public bool IsRequired { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    private bool _isSelectionLocked;
    public bool IsSelectionLocked
    {
        get => _isSelectionLocked;
        set
        {
            if (_isSelectionLocked == value)
                return;

            _isSelectionLocked = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelectionLocked)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CanToggle)));
        }
    }

    public bool CanToggle => !IsRequired && !IsSelectionLocked;

    public string ToggleColumnHeader => Kind switch
    {
        GpuPackageComponentKind.ScheduledTask => "Enabled",
        _ => "Keep"
    };

    public string ActionHint => Kind switch
    {
        GpuPackageComponentKind.ScheduledTask => "Uncheck to install the task in a disabled state.",
        GpuPackageComponentKind.DisplayDriver => "Uncheck to remove this display driver component from the installer.",
        GpuPackageComponentKind.Folder => "Uncheck to delete this folder before installation.",
        _ => "Uncheck to prevent this package from installing."
    };

    public string DetailText => Kind switch
    {
        GpuPackageComponentKind.DisplayDriver => string.IsNullOrWhiteSpace(Description) ? "Display driver component" : Description,
        GpuPackageComponentKind.ScheduledTask => Description,
        GpuPackageComponentKind.Folder => "Installer folder",
        _ => string.IsNullOrWhiteSpace(Description) ? "Installer package" : Description
    };

    public string TypeText => Kind switch
    {
        GpuPackageComponentKind.DisplayDriver => "Display",
        GpuPackageComponentKind.ScheduledTask => "Task",
        GpuPackageComponentKind.Folder => "Folder",
        _ => "Package"
    };

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public enum GpuDebloatMode
{
    Stripped,
    Stock,
    Custom
}

public sealed class GpuPreparedPackage
{
    public GpuDriverCatalogVendor Vendor { get; init; }
    public string InstallerPath { get; init; } = string.Empty;
    public string ExtractedPath { get; init; } = string.Empty;
    public IReadOnlyList<GpuPackageComponent> Components { get; init; } = [];
}
