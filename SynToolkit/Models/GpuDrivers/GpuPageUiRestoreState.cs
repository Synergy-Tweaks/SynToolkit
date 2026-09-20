#nullable enable

namespace SynToolkit.Models.GpuDrivers;

/// <summary>
/// Snapshot of the GPU page workspace used to rebuild the visual tree after a
/// display-driver install tears down WinUI composition surfaces.
/// </summary>
public sealed class GpuPageUiRestoreState
{
    public GpuVendorSelectionSnapshot SelectedVendor { get; init; } = GpuVendorSelectionSnapshot.None;
    public int TabIndex { get; init; }
    public string? SelectedGpuPnpDeviceId { get; init; }
    public string? SelectedGpuDisplayName { get; init; }
    public string? SelectedDriverVersion { get; init; }
    public string? SelectedDriverDownloadUrl { get; init; }
    public string SelectedDebloatModeText { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
    public string PreparedPackageNote { get; init; } = string.Empty;
    public bool HasError { get; init; }
    public GpuPreparedPackageSnapshot? PreparedPackage { get; init; }
    public IReadOnlyList<GpuComponentSelectionSnapshot> DebloatSelections { get; init; } = [];
}

public enum GpuVendorSelectionSnapshot
{
    None,
    AMD,
    NVIDIA,
}

public sealed class GpuPreparedPackageSnapshot
{
    public GpuDriverCatalogVendor Vendor { get; init; }
    public string InstallerPath { get; init; } = string.Empty;
    public string ExtractedPath { get; init; } = string.Empty;
    public string SourceFolderPath { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public bool IsManualImport { get; init; }
}

public sealed class GpuComponentSelectionSnapshot
{
    public GpuPackageComponentKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
}
