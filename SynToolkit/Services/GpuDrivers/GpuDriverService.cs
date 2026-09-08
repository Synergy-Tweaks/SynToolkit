#nullable enable

using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

public static partial class GpuDriverService
{
    private sealed record DriverInstallInfo(string Version, string Provider, string InfName);

    private static readonly HashSet<string> RequiredComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display.Driver",
        "NVI2",
        "setup.exe",
        "setup.cfg"
    };

    private static readonly HashSet<string> PresetComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display.Driver",
        "NVI2",
        "MSVCRT",
        "EULA.txt",
        "ListDevices.txt",
        "setup.cfg",
        "setup.exe"
    };

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly HttpClient DriverDownloadHttp = CreateHttpClient(Timeout.InfiniteTimeSpan);

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return client;
    }

    public static Task<IReadOnlyList<GpuDeviceInfo>> GetGpuDevicesAsync()
    {
        return Task.Run<IReadOnlyList<GpuDeviceInfo>>(() =>
        {
            var pciNames = LoadPciDeviceNames();
            var installedDrivers = LoadInstalledDisplayDrivers();
            var results = new List<GpuDeviceInfo>();

            AddGpuDevicesFromWmi(pciNames, installedDrivers, results);
            AddGpuDevicesFromPnpEntities(pciNames, installedDrivers, results);

            return results
                .OrderByDescending(device => device.IsNvidia)
                .ThenByDescending(device => device.IsAmd)
                .ThenByDescending(device => device.HasInstalledDriver)
                .ThenBy(device => device.DisplayName)
                .ToList();
        });
    }

    public static GpuDeviceInfo? SelectPrimaryGpuDevice(IReadOnlyList<GpuDeviceInfo> devices)
    {
        return devices
            .OrderByDescending(device => device.IsNvidia)
            .ThenByDescending(device => device.IsAmd)
            .ThenByDescending(device => device.HasInstalledDriver)
            .ThenBy(device => device.IsMicrosoftBasicDisplayAdapter)
            .ThenBy(device => device.DisplayName)
            .FirstOrDefault();
    }

    private static void AddGpuDevicesFromWmi(
        IReadOnlyDictionary<(string VendorId, string DeviceId), string> pciNames,
        IReadOnlyDictionary<string, DriverInstallInfo> installedDrivers,
        List<GpuDeviceInfo> results)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Caption, AdapterCompatibility, PNPDeviceID, DriverVersion FROM Win32_VideoController");

            foreach (ManagementObject item in searcher.Get())
            {
                var pnpId = item["PNPDeviceID"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pnpId))
                    continue;

                var ids = ParsePciIds(pnpId);
                if (string.IsNullOrWhiteSpace(ids.VendorId) || string.IsNullOrWhiteSpace(ids.DeviceId))
                    continue;

                var rawName = (item["Name"] ?? item["Caption"])?.ToString()?.Trim() ?? string.Empty;
                var pciName = ResolvePciName(pciNames, ids.VendorId, ids.DeviceId, rawName);
                var name = GetBestGpuName(rawName, pciName);
                installedDrivers.TryGetValue(pnpId, out var installedDriver);

                AddGpuDevice(
                    results,
                    name,
                    pciName,
                    ids,
                    GetBestDriverVersion(
                        item["DriverVersion"]?.ToString()?.Trim(),
                        installedDriver),
                    installedDriver,
                    pnpId);
            }
        }
        catch (ManagementException)
        {
        }
    }

    private static void AddGpuDevicesFromPnpEntities(
        IReadOnlyDictionary<(string VendorId, string DeviceId), string> pciNames,
        IReadOnlyDictionary<string, DriverInstallInfo> installedDrivers,
        List<GpuDeviceInfo> results)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID, PNPClass, Service FROM Win32_PnPEntity");

            foreach (ManagementObject item in searcher.Get())
            {
                var pnpId = item["PNPDeviceID"]?.ToString()?.Trim() ?? string.Empty;
                if (!pnpId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                var ids = ParsePciIds(pnpId);
                if (string.IsNullOrWhiteSpace(ids.VendorId) || string.IsNullOrWhiteSpace(ids.DeviceId))
                    continue;

                var rawName = item["Name"]?.ToString()?.Trim() ?? string.Empty;
                var pnpClass = item["PNPClass"]?.ToString()?.Trim() ?? string.Empty;
                var service = item["Service"]?.ToString()?.Trim() ?? string.Empty;
                if (!IsDisplayPciDevice(pnpId, ids.VendorId, rawName, pnpClass, service))
                    continue;

                var pciName = ResolvePciName(pciNames, ids.VendorId, ids.DeviceId, rawName);
                var name = GetBestGpuName(rawName, pciName);
                installedDrivers.TryGetValue(pnpId, out var installedDriver);

                AddGpuDevice(
                    results,
                    name,
                    pciName,
                    ids,
                    GetBestDriverVersion(null, installedDriver),
                    installedDriver,
                    pnpId);
            }
        }
        catch (ManagementException)
        {
        }
    }

    private static void AddGpuDevice(
        List<GpuDeviceInfo> results,
        string name,
        string pciName,
        (string VendorId, string DeviceId, string SubsystemVendorId, string SubsystemDeviceId) ids,
        string driverVersion,
        DriverInstallInfo? installedDriver,
        string pnpId)
    {
        if (results.Any(device => string.Equals(device.PnpDeviceId, pnpId, StringComparison.OrdinalIgnoreCase)))
            return;

        results.Add(new GpuDeviceInfo
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Unknown GPU" : name,
            PciName = string.IsNullOrWhiteSpace(pciName) ? "Unknown PCI device" : pciName,
            VendorId = ids.VendorId,
            DeviceId = ids.DeviceId,
            SubsystemVendorId = ids.SubsystemVendorId,
            SubsystemDeviceId = ids.SubsystemDeviceId,
            DriverVersion = string.IsNullOrWhiteSpace(driverVersion) ? "Unknown" : driverVersion,
            DriverProvider = installedDriver?.Provider ?? string.Empty,
            DriverInfName = installedDriver?.InfName ?? string.Empty,
            PnpDeviceId = pnpId
        });
    }

    private static Dictionary<string, DriverInstallInfo> LoadInstalledDisplayDrivers()
    {
        var results = new Dictionary<string, DriverInstallInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, DriverVersion, DriverProviderName, InfName FROM Win32_PnPSignedDriver WHERE DeviceClass = 'DISPLAY'");

            foreach (ManagementObject item in searcher.Get())
            {
                var deviceId = item["DeviceID"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(deviceId))
                    continue;

                results[deviceId] = new DriverInstallInfo(
                    item["DriverVersion"]?.ToString()?.Trim() ?? string.Empty,
                    item["DriverProviderName"]?.ToString()?.Trim() ?? string.Empty,
                    item["InfName"]?.ToString()?.Trim() ?? string.Empty);
            }
        }
        catch (ManagementException)
        {
        }

        return results;
    }

    private static string GetBestDriverVersion(string? wmiDriverVersion, DriverInstallInfo? installedDriver)
    {
        if (!string.IsNullOrWhiteSpace(installedDriver?.Version))
            return installedDriver.Version;

        return string.IsNullOrWhiteSpace(wmiDriverVersion)
            ? "Not installed"
            : wmiDriverVersion;
    }

    public static async Task<GpuDriverLookupResult> GetNvidiaDriversAsync(GpuDeviceInfo device)
    {
        if (!device.IsNvidia)
        {
            return new GpuDriverLookupResult
            {
                Device = device,
                Vendor = GpuDriverCatalogVendor.Nvidia,
                Message = "NVIDIA driver lookup only supports NVIDIA PCI devices."
            };
        }

        try
        {
            var productTypes = await GetLookupValuesAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=1");
            var geforceType = productTypes.FirstOrDefault(value =>
                value.Name.Equals("GeForce", StringComparison.OrdinalIgnoreCase));
            if (geforceType is null)
            {
                return new GpuDriverLookupResult
                {
                    Device = device,
                    Vendor = GpuDriverCatalogVendor.Nvidia,
                    Message = "NVIDIA product type lookup did not return GeForce."
                };
            }

            var seriesValues = await GetLookupValuesAsync("https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2");
            var geforceSeries = seriesValues
                .Where(value => string.Equals(value.ParentId, geforceType.Value, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(value => value.Name.Length)
                .ToList();

            var gpuName = $"{device.Name} {device.DisplayName}";
            var preferredPlatform = NvidiaDriverPackagePolicy.Classify(gpuName, IsPortableComputer());
            var match = await FindMatchingNvidiaProductAsync(gpuName, geforceSeries, preferredPlatform);
            if (match is null)
            {
                return new GpuDriverLookupResult
                {
                    Device = device,
                    Vendor = GpuDriverCatalogVendor.Nvidia,
                    Message = $"Could not map {device.DisplayName} to NVIDIA's driver search product list."
                };
            }

            var drivers = await GetNvidiaDriverRowsAsync(match);

            return new GpuDriverLookupResult
            {
                Device = device,
                Vendor = GpuDriverCatalogVendor.Nvidia,
                Drivers = drivers,
                ProductName = match.Product.Name,
                SeriesName = match.Series.Name,
                NvidiaPlatform = match.Platform,
                Message = drivers.Count == 0
                    ? "NVIDIA returned no Game Ready drivers for this product. Try again in a moment."
                    : $"Loaded {drivers.Count} NVIDIA {match.Platform.ToString().ToLowerInvariant()} drivers for {match.Product.Name}."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new GpuDriverLookupResult
            {
                Device = device,
                Vendor = GpuDriverCatalogVendor.Nvidia,
                Message = $"NVIDIA driver catalog is unreachable right now. {ex.Message}"
            };
        }
    }

    private static async Task<List<GpuDriverOption>> GetNvidiaDriverRowsAsync(NvidiaProductMatch match)
    {
        var allDrivers = new List<GpuDriverOption>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var osIds = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? new[] { "135", "57" }
            : new[] { "57", "135" };

        // Prefer the combinations that usually return Game Ready rows first.
        // The old 2x2x3x2 = 24 sequential calls failed the whole lookup if any
        // single request timed out or was rate-limited by NVIDIA.
        var queries = new List<(string OsId, string Whql, string DriverType, bool IncludeProduct)>();
        foreach (var osId in osIds)
        {
            queries.Add((osId, "1", "1", true));
            queries.Add((osId, "1", "0", true));
            queries.Add((osId, "0", "1", true));
            queries.Add((osId, "1", "1", false));
        }

        foreach (var (osId, whql, driverType, includeProduct) in queries)
        {
            if (allDrivers.Count >= 12)
                break;

            var productPart = includeProduct
                ? $"&pfid={Uri.EscapeDataString(match.Product.Value)}"
                : string.Empty;
            var query =
                $"https://www.nvidia.com/Download/processFind.aspx?psid={Uri.EscapeDataString(match.Series.Value)}{productPart}&osid={osId}&lid=1&whql={whql}&dtcid={driverType}";

            string html;
            try
            {
                html = await Http.GetStringAsync(query);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                continue;
            }

            foreach (var driver in ParseDriverRows(html, match.Platform)
                         .Where(entry => entry.Name.Contains("Game Ready", StringComparison.OrdinalIgnoreCase) ||
                                         entry.Name.Contains("Studio Driver", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(driver.Version) || !seenVersions.Add(driver.Version))
                    continue;

                allDrivers.Add(driver);
            }
        }

        return allDrivers
            .OrderByDescending(driver =>
                driver.Name.Contains("Game Ready", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(driver => Version.TryParse(driver.Version, out var version) ? version : new Version())
            .ToList();
    }

    public static async Task<string> DownloadDriverInstallerAsync(
        GpuDriverOption driver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driver.DownloadUrl))
            throw new InvalidOperationException("The selected driver does not have a direct installer URL.");

        var downloadUri = new Uri(driver.DownloadUrl, UriKind.Absolute);
        if (driver.Vendor == GpuDriverCatalogVendor.Amd)
            EnsureTrustedAmdDownloadUri(downloadUri);
        else if (driver.Vendor == GpuDriverCatalogVendor.Nvidia)
            EnsureTrustedNvidiaDownloadUri(downloadUri, driver.NvidiaPlatform);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "SynToolkit", "GpuDrivers");
        Directory.CreateDirectory(tempDirectory);

        var fileName = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            fileName = $"{driver.Vendor.ToString().ToLowerInvariant()}-driver-{driver.Version}.exe";

        var installerPath = Path.Combine(tempDirectory, fileName);
        var partialPath = installerPath + ".partial";
        if (File.Exists(installerPath) && new FileInfo(installerPath).Length > 50 * 1024 * 1024)
        {
            if (driver.Vendor == GpuDriverCatalogVendor.Amd)
            {
                try
                {
                    EnsureTrustedAmdInstaller(installerPath);
                }
                catch
                {
                    File.Delete(installerPath);
                }
            }

            if (File.Exists(installerPath))
            {
                progress?.Report(1);
                return installerPath;
            }
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteFile(partialPath);
            progress?.Report(0);

            try
            {
                await DownloadDriverInstallerAttemptAsync(
                    driver,
                    downloadUri,
                    partialPath,
                    progress,
                    cancellationToken);

                File.Move(partialPath, installerPath, overwrite: true);
                progress?.Report(1);
                return installerPath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDeleteFile(partialPath);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or
                                       TaskCanceledException or InvalidOperationException)
            {
                lastError = ex;
                TryDeleteFile(partialPath);
                App.logger.Warn(ex, "[GPU] Driver download attempt {0}/3 failed for {1}.", attempt, fileName);
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Driver download failed after 3 attempts: {lastError?.Message ?? "unknown download error"}",
            lastError);
    }

    private static async Task DownloadDriverInstallerAttemptAsync(
        GpuDriverOption driver,
        Uri downloadUri,
        string partialPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        if (driver.Vendor == GpuDriverCatalogVendor.Amd)
            request.Headers.TryAddWithoutValidation("Referer", "https://www.amd.com/");

        using var response = await DriverDownloadHttp.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (driver.Vendor == GpuDriverCatalogVendor.Amd)
            EnsureTrustedAmdDownloadUri(response.RequestMessage?.RequestUri);

        var expectedBytes = response.Content.Headers.ContentLength;
        long downloadedBytes = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(
                         partialPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 1024 * 128,
                         useAsync: true))
        {
            var buffer = new byte[1024 * 128];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                if (expectedBytes is > 0)
                    progress?.Report(downloadedBytes / (double)expectedBytes.Value);
            }

            await destination.FlushAsync(cancellationToken);
        }

        if (expectedBytes is > 0 && downloadedBytes != expectedBytes.Value)
        {
            throw new IOException(
                $"The driver download ended early ({downloadedBytes:N0} of {expectedBytes.Value:N0} bytes). ");
        }

        var downloadedSize = new FileInfo(partialPath).Length;
        if (driver.Vendor == GpuDriverCatalogVendor.Amd && downloadedSize < 50 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "The AMD driver download looks incomplete. Check your connection and try again.");
        }

        if (driver.Vendor == GpuDriverCatalogVendor.Amd)
        {
            try
            {
                EnsureTrustedAmdInstaller(partialPath);
            }
            catch
            {
                throw;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The original operation reports the actionable failure.
        }
    }

    public static void LaunchInstaller(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException("The NVIDIA installer could not be found.", installerPath);

        Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true
        });
    }

    public static async Task<GpuPreparedPackage> PrepareNvidiaPackageAsync(
        GpuDriverOption driver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installerPath = await DownloadDriverInstallerAsync(driver, progress, cancellationToken);
        var extractedPath = GetNvidiaExtractedPackagePath(driver);

        await EnsureFreshNvidiaExtractAsync(installerPath, extractedPath, cancellationToken);

        return new GpuPreparedPackage
        {
            Vendor = GpuDriverCatalogVendor.Nvidia,
            InstallerPath = installerPath,
            ExtractedPath = extractedPath,
            Components = GetNvidiaPackageComponents(extractedPath, selectPreset: true)
        };
    }

    public static IReadOnlyList<GpuPackageComponent> GetNvidiaPackageComponents(string extractedPath, bool selectPreset)
    {
        if (!Directory.Exists(extractedPath))
            return [];

        return Directory.EnumerateFileSystemEntries(extractedPath)
            .Where(Directory.Exists)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var required = RequiredComponents.Contains(name);
                return new GpuPackageComponent
                {
                    Name = name,
                    FullPath = path,
                    Kind = GpuPackageComponentKind.Folder,
                    IsDirectory = Directory.Exists(path),
                    IsRequired = required,
                    IsSelected = required || (selectPreset ? PresetComponents.Contains(name) : true)
                };
            })
            .Where(component => !component.IsRequired || component.Name.Equals("Display.Driver", StringComparison.OrdinalIgnoreCase) || component.Name.Equals("NVI2", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(component => component.IsRequired)
            .ThenBy(component => component.Name)
            .ToList();
    }

    public static void ApplyNvidiaDebloat(string extractedPath, IEnumerable<GpuPackageComponent> components)
    {
        var selectedFolders = components
            .Where(component => component.IsDirectory && (component.IsRequired || component.IsSelected))
            .Select(component => component.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        selectedFolders.Add("Display.Driver");
        selectedFolders.Add("NVI2");

        foreach (var path in Directory.EnumerateDirectories(extractedPath).ToList())
        {
            var name = Path.GetFileName(path);
            if (selectedFolders.Contains(name))
                continue;

            Directory.Delete(path, recursive: true);
        }

        PatchSetupConfig(extractedPath);
    }

    private static void PatchSetupConfig(string extractedPath)
    {
        var setupConfigPath = Path.Combine(extractedPath, "setup.cfg");
        if (!File.Exists(setupConfigPath))
            return;

        var variableNamesToRemove = new[]
        {
            "EulaHtmlFile",
            "FunctionalConsentFile",
            "PrivacyPolicyFile"
        };

        var lines = File.ReadAllLines(setupConfigPath);
        var patched = lines
            .Where(line => !ShouldRemoveSetupConfigFileLine(line, variableNamesToRemove))
            .ToArray();

        if (patched.Length != lines.Length)
            File.WriteAllLines(setupConfigPath, patched);
    }

    private static bool ShouldRemoveSetupConfigFileLine(string line, IEnumerable<string> variableNames)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("<file ", StringComparison.OrdinalIgnoreCase) &&
               variableNames.Any(name => trimmed.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    public static void LaunchNvidiaExtractedSetup(string extractedPath)
    {
        if (!Directory.Exists(extractedPath))
            throw new DirectoryNotFoundException($"The extracted NVIDIA folder was not found: {extractedPath}");

        var setupPath = Path.Combine(extractedPath, "setup.exe");
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("The extracted NVIDIA setup.exe could not be found.", setupPath);

        Process.Start(new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            WorkingDirectory = extractedPath
        });
    }

    /// <summary>
    /// Re-extracts the cached NVIDIA installer so Stock/Stripped/Custom always start from a complete package tree.
    /// </summary>
    public static async Task EnsureFreshNvidiaExtractAsync(
        string installerPath,
        string extractedPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException("The NVIDIA installer could not be found.", installerPath);

        if (Directory.Exists(extractedPath))
            Directory.Delete(extractedPath, recursive: true);

        Directory.CreateDirectory(extractedPath);
        await ExtractNvidiaDriverPackageAsync(installerPath, extractedPath, cancellationToken);
    }

    private static async Task ExtractNvidiaDriverPackageAsync(
        string installerPath,
        string extractedPath,
        CancellationToken cancellationToken)
    {
        var sevenZipPath = FindSevenZipExecutable();
        if (sevenZipPath is null)
        {
            throw new InvalidOperationException(
                "7-Zip is required to safely extract NVIDIA drivers without running the NVIDIA installer. Install 7-Zip and try again.");
        }

        var exitCode = await RunProcessAsync(
            sevenZipPath,
            $"x -y -o\"{extractedPath}\" \"{installerPath}\"",
            cancellationToken);

        if (exitCode != 0)
            throw new InvalidOperationException($"7-Zip extraction failed with exit code {exitCode}.");

        if (!File.Exists(Path.Combine(extractedPath, "setup.exe")))
            throw new InvalidOperationException("The NVIDIA package was extracted, but setup.exe was not found.");
    }

    private static string? FindSevenZipExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "7-Zip", "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "7z.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tools", "7z.exe"),
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        process.Start();
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string GetNvidiaExtractedPackagePath(GpuDriverOption driver)
    {
        var folderName = $"nvidia-driver-{SanitizePathPart(driver.Version)}";
        return Path.Combine(Path.GetTempPath(), "SynToolkit", "GpuDrivers", "Extracted", "Nvidia", folderName);
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "driver" : sanitized;
    }

    private static async Task<NvidiaProductMatch?> FindMatchingNvidiaProductAsync(
        string gpuName,
        IReadOnlyList<NvidiaLookupValue> seriesValues,
        NvidiaDriverPlatform preferredPlatform)
    {
        var normalizedGpu = NormalizeName(gpuName);
        var preferredSeries = seriesValues
            .OrderByDescending(series => GetSeriesScore(normalizedGpu, series.Name, preferredPlatform))
            .ThenByDescending(series => series.Value)
            .ToList();

        foreach (var series in preferredSeries)
        {
            if (GetSeriesScore(normalizedGpu, series.Name, preferredPlatform) <= 0)
                continue;

            var products = await GetLookupValuesAsync(
                $"https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID={Uri.EscapeDataString(series.Value)}");

            var product = products
                .OrderByDescending(value => GetProductScore(normalizedGpu, value.Name))
                .FirstOrDefault(value => GetProductScore(normalizedGpu, value.Name) > 0);

            if (product is not null)
                return new NvidiaProductMatch(
                    series,
                    product,
                    series.Name.Contains("NOTEBOOK", StringComparison.OrdinalIgnoreCase)
                        ? NvidiaDriverPlatform.Notebook
                        : NvidiaDriverPlatform.Desktop);
        }

        return null;
    }

    private static int GetSeriesScore(
        string normalizedGpu,
        string seriesName,
        NvidiaDriverPlatform preferredPlatform)
    {
        var series = NormalizeName(seriesName);
        var score = 0;
        var generationMatch = RtxGtxGenerationRegex().Match(normalizedGpu);
        if (generationMatch.Success && series.Contains(generationMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            score += 100;
        else
        {
            var mxMatch = NvidiaMxRegex().Match(normalizedGpu);
            if (mxMatch.Success && series.Contains($"MX{mxMatch.Groups[1].Value}00", StringComparison.OrdinalIgnoreCase))
                score += 100;

            var legacyMobileMatch = LegacyNvidiaMobileRegex().Match(normalizedGpu);
            if (legacyMobileMatch.Success && series.Contains($"{legacyMobileMatch.Groups[1].Value}00M", StringComparison.OrdinalIgnoreCase))
                score += 100;
        }

        if (score == 0)
            return 0;

        if (normalizedGpu.Contains("RTX", StringComparison.OrdinalIgnoreCase) && series.Contains("RTX", StringComparison.OrdinalIgnoreCase))
            score += 20;
        if (normalizedGpu.Contains("GTX", StringComparison.OrdinalIgnoreCase) && series.Contains("GTX", StringComparison.OrdinalIgnoreCase))
            score += 20;

        var notebookSeries = series.Contains("NOTEBOOK", StringComparison.OrdinalIgnoreCase);
        score += preferredPlatform switch
        {
            NvidiaDriverPlatform.Notebook when notebookSeries => 80,
            NvidiaDriverPlatform.Notebook => -80,
            NvidiaDriverPlatform.Desktop when !notebookSeries => 30,
            NvidiaDriverPlatform.Desktop => -30,
            _ => 0
        };

        return score;
    }

    private static int GetProductScore(string normalizedGpu, string productName)
    {
        var product = NormalizeName(productName);
        var productTokens = product.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (productTokens.Length == 0)
            return 0;

        var score = 0;
        foreach (var token in productTokens)
        {
            if (token is "NVIDIA" or "GEFORCE")
                continue;

            if (normalizedGpu.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += token.All(char.IsDigit) ? 40 : 14;
        }

        if (product.Contains(" TI", StringComparison.OrdinalIgnoreCase) &&
            !normalizedGpu.Contains(" TI", StringComparison.OrdinalIgnoreCase))
        {
            score -= 35;
        }

        if (product.Contains(" SUPER", StringComparison.OrdinalIgnoreCase) &&
            !normalizedGpu.Contains(" SUPER", StringComparison.OrdinalIgnoreCase))
        {
            score -= 35;
        }

        return score;
    }

    private static IEnumerable<GpuDriverOption> ParseDriverRows(
        string html,
        NvidiaDriverPlatform platform)
    {
        foreach (Match row in DriverRowRegex().Matches(html))
        {
            var rowHtml = row.Value;
            var links = DriverLinkRegex().Matches(rowHtml);
            var detailUrl = links.Count > 0 ? NormalizeNvidiaUrl(WebUtility.HtmlDecode(links[0].Groups[1].Value)) : string.Empty;
            var cells = DriverCellRegex()
                .Matches(rowHtml)
                .Select(match => StripHtml(match.Groups[1].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (cells.Count < 3)
                continue;

            var downloadUrl = links.Count > 1
                ? NormalizeNvidiaUrl(WebUtility.HtmlDecode(links[^1].Groups[1].Value))
                : detailUrl;
            if (!IsDirectNvidiaInstallerUrl(downloadUrl, platform))
                downloadUrl = NvidiaDriverPackagePolicy.BuildInstallerUrl(cells.ElementAtOrDefault(1) ?? string.Empty, platform);

            yield return new GpuDriverOption
            {
                Vendor = GpuDriverCatalogVendor.Nvidia,
                Name = cells.ElementAtOrDefault(0) ?? "NVIDIA Driver",
                Version = cells.ElementAtOrDefault(1) ?? string.Empty,
                ReleaseDate = cells.ElementAtOrDefault(2) ?? string.Empty,
                DetailUrl = detailUrl,
                DownloadUrl = downloadUrl,
                OperatingSystem = "Windows",
                Type = cells.ElementAtOrDefault(3) ?? string.Empty,
                NvidiaPlatform = platform
            };
        }
    }

    private static bool IsPortableComputer()
    {
        try
        {
            using var computerSearcher = new ManagementObjectSearcher("SELECT PCSystemType FROM Win32_ComputerSystem");
            foreach (ManagementObject computer in computerSearcher.Get())
            {
                if (Convert.ToInt32(computer["PCSystemType"], CultureInfo.InvariantCulture) is 2 or 8)
                    return true;
            }

            using var enclosureSearcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject enclosure in enclosureSearcher.Get())
            {
                if (enclosure["ChassisTypes"] is ushort[] chassisTypes &&
                    chassisTypes.Any(type => type is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 30 or 31 or 32))
                {
                    return true;
                }
            }
        }
        catch (ManagementException)
        {
        }

        return false;
    }

    private static bool IsDirectNvidiaInstallerUrl(string url, NvidiaDriverPlatform platform)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(uri.AbsolutePath);
        return platform != NvidiaDriverPlatform.Notebook ||
               !fileName.Contains("-desktop-", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureTrustedNvidiaDownloadUri(Uri uri, NvidiaDriverPlatform platform)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The NVIDIA installer link is not an official HTTPS executable URL.");
        }

        var fileName = Path.GetFileName(uri.AbsolutePath);
        if (platform == NvidiaDriverPlatform.Notebook &&
            fileName.Contains("-desktop-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("NVIDIA returned a desktop package for a notebook GPU.");
        }
    }

    private static async Task<List<NvidiaLookupValue>> GetLookupValuesAsync(string url)
    {
        var xml = await Http.GetStringAsync(url);
        if (string.IsNullOrWhiteSpace(xml) || !xml.Contains("<LookupValue", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("NVIDIA lookup API returned an unexpected response.");

        try
        {
            var document = XDocument.Parse(xml);
            return document.Descendants("LookupValue")
                .Select(element => new NvidiaLookupValue(
                    element.Element("Name")?.Value.Trim() ?? string.Empty,
                    element.Element("Value")?.Value.Trim() ?? string.Empty,
                    element.Attribute("ParentID")?.Value.Trim() ?? string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value.Name) && !string.IsNullOrWhiteSpace(value.Value))
                .ToList();
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException("NVIDIA lookup API returned invalid XML.", ex);
        }
    }

    // pci.ids is shipped with the app and never changes at runtime, so the parsed
    // lookup (tens of thousands of entries) is built once and reused.
    private static readonly Lazy<Dictionary<(string VendorId, string DeviceId), string>> CachedPciNames =
        new(ParsePciDeviceNames, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dictionary<(string VendorId, string DeviceId), string> LoadPciDeviceNames()
        => CachedPciNames.Value;

    private static Dictionary<(string VendorId, string DeviceId), string> ParsePciDeviceNames()
    {
        var names = new Dictionary<(string VendorId, string DeviceId), string>();
        var path = Path.Combine(AppContext.BaseDirectory, "pci.ids");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "..", "pci.ids");
        if (!File.Exists(path))
            return names;

        var currentVendor = string.Empty;
        foreach (var rawLine in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith('#'))
                continue;

            if (!rawLine.StartsWith('\t'))
            {
                var parts = rawLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                currentVendor = parts.Length > 0 ? parts[0].ToUpperInvariant() : string.Empty;
                continue;
            }

            if (rawLine.StartsWith("\t\t") || string.IsNullOrWhiteSpace(currentVendor))
                continue;

            var trimmed = rawLine.Trim();
            var deviceParts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (deviceParts.Length == 2)
                names[(currentVendor, deviceParts[0].ToUpperInvariant())] = NormalizePciDeviceName(deviceParts[1]);
        }

        return names;
    }

    private static string NormalizePciDeviceName(string name)
    {
        var trimmed = name.Trim();
        var bracketMatch = Regex.Match(trimmed, @"\[(?<name>[^\]]+)\]");
        return bracketMatch.Success ? bracketMatch.Groups["name"].Value.Trim() : trimmed;
    }

    private static (string VendorId, string DeviceId, string SubsystemVendorId, string SubsystemDeviceId) ParsePciIds(string pnpDeviceId)
    {
        var vendor = GetIdPart(pnpDeviceId, "VEN_");
        var device = GetIdPart(pnpDeviceId, "DEV_");
        var subsystem = GetIdPart(pnpDeviceId, "SUBSYS_");
        return (
            vendor,
            device,
            subsystem.Length >= 8 ? subsystem[4..8] : string.Empty,
            subsystem.Length >= 8 ? subsystem[..4] : string.Empty);
    }

    private static string ResolvePciName(
        IReadOnlyDictionary<(string VendorId, string DeviceId), string> pciNames,
        string vendorId,
        string deviceId,
        string fallbackName)
    {
        return pciNames.TryGetValue((vendorId, deviceId), out var resolvedName) && !string.IsNullOrWhiteSpace(resolvedName)
            ? resolvedName
            : fallbackName;
    }

    private static string GetBestGpuName(string rawName, string pciName)
    {
        if (!IsGenericGpuName(rawName))
            return rawName;

        if (!string.IsNullOrWhiteSpace(pciName) &&
            !pciName.Equals("Unknown PCI device", StringComparison.OrdinalIgnoreCase))
        {
            return pciName;
        }

        return string.IsNullOrWhiteSpace(rawName) ? "Unknown GPU" : rawName;
    }

    private static bool IsGenericGpuName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var genericNames = new[]
        {
            "Microsoft Basic Display",
            "Basic Display",
            "Video Controller",
            "3D Video Controller",
            "VGA Compatible Controller",
            "Unknown"
        };

        return genericNames.Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDisplayPciDevice(
        string pnpId,
        string vendorId,
        string name,
        string pnpClass,
        string service)
    {
        if (pnpId.Contains("CC_03", StringComparison.OrdinalIgnoreCase))
            return true;

        if (pnpClass.Equals("Display", StringComparison.OrdinalIgnoreCase))
            return true;

        if (service.Contains("display", StringComparison.OrdinalIgnoreCase) ||
            service.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!vendorId.Equals("10DE", StringComparison.OrdinalIgnoreCase) &&
            !vendorId.Equals("1002", StringComparison.OrdinalIgnoreCase))
            return false;

        if (vendorId.Equals("1002", StringComparison.OrdinalIgnoreCase))
        {
            return name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                   IsGenericGpuName(name);
        }

        return name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
               IsGenericGpuName(name);
    }

    private static string GetIdPart(string text, string prefix)
    {
        var index = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return string.Empty;

        var start = index + prefix.Length;
        var length = 0;
        while (start + length < text.Length && Uri.IsHexDigit(text[start + length]) && length < 8)
            length++;

        return text.Substring(start, length).ToUpperInvariant();
    }

    private static string NormalizeName(string value)
    {
        return Regex.Replace(value.ToUpperInvariant(), @"[^\w]+", " ").Trim();
    }

    private static string NormalizeNvidiaUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;
        if (url.StartsWith("//", StringComparison.Ordinal))
            return "https:" + url;
        if (url.StartsWith("/", StringComparison.Ordinal))
            return "https://www.nvidia.com" + url;
        return url;
    }

    private static string StripHtml(string html)
    {
        var withoutTags = Regex.Replace(html, "<.*?>", " ");
        return WebUtility.HtmlDecode(Regex.Replace(withoutTags, @"\s+", " ").Trim());
    }

    [GeneratedRegex(@"<tr[^>]*id=[""']driverList[""'][\s\S]*?</tr>", RegexOptions.IgnoreCase)]
    private static partial Regex DriverRowRegex();

    [GeneratedRegex(@"<td[^>]*class=[""']gridItem[^""']*[""'][^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase)]
    private static partial Regex DriverCellRegex();

    [GeneratedRegex(@"href=['""]([^'""]+)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex DriverLinkRegex();

    [GeneratedRegex(@"\b(?:RTX|GTX)\s*(\d{2})\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex RtxGtxGenerationRegex();

    [GeneratedRegex(@"\bMX\s*(\d)\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaMxRegex();

    [GeneratedRegex(@"\b(?:GTX|GEFORCE)\s*(\d)\d{2}M\b", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyNvidiaMobileRegex();

    private sealed record NvidiaLookupValue(string Name, string Value, string ParentId);
    private sealed record NvidiaProductMatch(
        NvidiaLookupValue Series,
        NvidiaLookupValue Product,
        NvidiaDriverPlatform Platform);
}
