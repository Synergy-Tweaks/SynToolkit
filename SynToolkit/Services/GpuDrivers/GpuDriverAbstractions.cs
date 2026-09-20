#nullable enable

using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

public interface IGpuDriverCatalogService
{
    Task<IReadOnlyList<GpuDeviceInfo>> GetGpuDevicesAsync();
    Task<GpuDriverLookupResult> GetDriversAsync(GpuDeviceInfo device);
}

public interface IGpuDriverPackageService
{
    Task<GpuPreparedPackage> PreparePackageAsync(
        GpuDriverOption driver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GpuPreparedPackage> ImportAmdInstallerAsync(
        string installerPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GpuPreparedPackage> ImportAmdExtractedFolderAsync(
        string folderPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    AmdPackageMatchSummary EvaluateAmdPackageMatch(IEnumerable<GpuPackageComponent> components);

    IReadOnlyList<GpuPackageComponent> GetPackageComponents(
        GpuDriverCatalogVendor vendor,
        string extractedPath,
        bool selectPreset);

    IReadOnlyList<GpuPackageComponent> ResolveInstallComponents(
        GpuPreparedPackage package,
        GpuDebloatMode mode);

    Task RefreshPreparedPackageAsync(
        GpuPreparedPackage package,
        CancellationToken cancellationToken = default);

    void ApplyDebloat(
        GpuDriverCatalogVendor vendor,
        string extractedPath,
        IEnumerable<GpuPackageComponent> components);

    void LaunchExtractedSetup(GpuDriverCatalogVendor vendor, string extractedPath);
}

public sealed class GpuDriverCatalogService : IGpuDriverCatalogService
{
    public Task<IReadOnlyList<GpuDeviceInfo>> GetGpuDevicesAsync() => GpuDriverService.GetGpuDevicesAsync();

    public Task<GpuDriverLookupResult> GetDriversAsync(GpuDeviceInfo device) => GpuDriverService.GetDriversAsync(device);
}

public sealed class GpuDriverPackageService : IGpuDriverPackageService
{
    public Task<GpuPreparedPackage> PreparePackageAsync(
        GpuDriverOption driver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        GpuDriverService.PreparePackageAsync(driver, progress, cancellationToken);

    public Task<GpuPreparedPackage> ImportAmdInstallerAsync(
        string installerPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        GpuDriverService.ImportAmdInstallerAsync(installerPath, progress, cancellationToken);

    public Task<GpuPreparedPackage> ImportAmdExtractedFolderAsync(
        string folderPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        GpuDriverService.ImportAmdExtractedFolderAsync(folderPath, progress, cancellationToken);

    public AmdPackageMatchSummary EvaluateAmdPackageMatch(IEnumerable<GpuPackageComponent> components) =>
        GpuDriverService.EvaluateAmdPackageMatch(components);

    public IReadOnlyList<GpuPackageComponent> GetPackageComponents(
        GpuDriverCatalogVendor vendor,
        string extractedPath,
        bool selectPreset) =>
        GpuDriverService.GetPackageComponents(vendor, extractedPath, selectPreset);

    public IReadOnlyList<GpuPackageComponent> ResolveInstallComponents(
        GpuPreparedPackage package,
        GpuDebloatMode mode) =>
        GpuDriverService.ResolveInstallComponents(package, mode);

    public async Task RefreshPreparedPackageAsync(
        GpuPreparedPackage package,
        CancellationToken cancellationToken = default)
    {
        if (package.Vendor == GpuDriverCatalogVendor.Amd)
        {
            await GpuDriverService.RefreshAmdPreparedPackageAsync(package, cancellationToken);
            return;
        }

        await GpuDriverService.EnsureFreshNvidiaExtractAsync(package.InstallerPath, package.ExtractedPath, cancellationToken);
    }

    public void ApplyDebloat(
        GpuDriverCatalogVendor vendor,
        string extractedPath,
        IEnumerable<GpuPackageComponent> components) =>
        GpuDriverService.ApplyDebloat(vendor, extractedPath, components);

    public void LaunchExtractedSetup(GpuDriverCatalogVendor vendor, string extractedPath) =>
        GpuDriverService.LaunchExtractedSetup(vendor, extractedPath);
}
