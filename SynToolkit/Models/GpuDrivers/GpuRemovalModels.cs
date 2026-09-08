#nullable enable

namespace SynToolkit.Models.GpuDrivers;

public enum GpuDriverVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel
}

public enum GpuRemovalLogLevel
{
    Info,
    Step,
    Success,
    Warning,
    Error
}

public sealed class GpuRemovalLogEntry
{
    public GpuRemovalLogLevel Level { get; init; } = GpuRemovalLogLevel.Info;
    public string Message { get; init; } = string.Empty;
}

public sealed class GpuRemovalOptions
{
    public GpuDriverVendor Vendor { get; init; } = GpuDriverVendor.Unknown;

    /// <summary>Run the vendor's own silent uninstaller(s) before purging.</summary>
    public bool RunVendorUninstaller { get; init; } = true;

    /// <summary>Remove published display-class driver packages from the driver store via pnputil.</summary>
    public bool RemoveDriverStorePackages { get; init; } = true;

    /// <summary>Stop and delete vendor background services.</summary>
    public bool RemoveServices { get; init; } = true;

    /// <summary>Delete leftover program/data folders.</summary>
    public bool RemoveLeftoverFiles { get; init; } = true;

    /// <summary>Delete leftover registry keys.</summary>
    public bool RemoveRegistryEntries { get; init; } = true;

    /// <summary>Prevent Windows Update from automatically reinstalling the driver.</summary>
    public bool BlockAutomaticReinstall { get; init; }
}

public sealed class GpuRemovalResult
{
    public bool Completed { get; init; }
    public int StepsSucceeded { get; init; }
    public int StepsFailed { get; init; }
    public string Summary { get; init; } = string.Empty;
}
