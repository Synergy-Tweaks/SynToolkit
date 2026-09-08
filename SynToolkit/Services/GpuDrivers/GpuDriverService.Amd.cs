#nullable enable

using System.Net.Http;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

public static partial class GpuDriverService
{
    private static readonly string[] AmdManifestFiles =
    [
        @"Bin64\cccmanifest_64.json",
        @"Config\InstallManifest.json"
    ];

    private static readonly Dictionary<string, string> AmdDisplayFriendlyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["amdfendr"] = "Crash Defender",
            ["amdfendrmgr"] = "Crash Defender Manager",
            ["amdfdans"] = "Dynamic Audio Noise Suppression",
            ["amdinstallmanager"] = "Install Manager",
            ["amdocl"] = "OpenCL User Mode Driver",
            ["amdogl"] = "OpenGL User Mode Driver",
            ["amdpcibridge"] = "PCI Bridge Device Extension",
            ["amdvlk"] = "Vulkan User Mode Driver",
            ["amdwin"] = "AMD Windows Support Components",
            ["amdxe"] = "AMD Controller Emulation",
            ["amdkmdag"] = "AMD Kernel Mode Driver",
            ["amdsound"] = "AMD HDMI Audio Driver",
            ["atihdwt6"] = "AMD HDMI Audio Driver"
        };

    private sealed record AmdSeriesMapping(string Pattern, string SeriesSlug);

    private static readonly AmdSeriesMapping[] AmdSeriesMappings =
    [
        new(@"\bRX\s*9[0-9]{3}\b", "radeon-rx-9000-series"),
        new(@"\bRX\s*7[0-9]{3}\b", "radeon-rx-7000-series"),
        new(@"\bRX\s*6[0-9]{3}\b", "radeon-rx-6000-series"),
        new(@"\bRX\s*5[0-9]{3}\b", "radeon-rx-5000-series"),
        new(@"\bRX\s*VEGA\b", "radeon-rx-vega-series"),
        new(@"\bVEGA\b", "radeon-rx-vega-series"),
        new(@"\bRX\s*5[0-9]{3}M\b", "radeon-rx-5000m-series"),
        new(@"\bRX\s*6[0-9]{3}M\b", "radeon-rx-6000m-series"),
        new(@"\bRX\s*7[0-9]{3}M\b", "radeon-rx-7000m-series"),
        new(@"\bPRO\s*W[0-9]", "radeon-pro-w-series"),
        new(@"\bRADEON\s*PRO\b", "radeon-pro-w-series")
    ];

    public static async Task<GpuDriverLookupResult> GetAmdDriversAsync(GpuDeviceInfo device)
    {
        if (!device.IsAmd)
        {
            return new GpuDriverLookupResult
            {
                Device = device,
                Vendor = GpuDriverCatalogVendor.Amd,
                Message = "AMD driver lookup only supports AMD PCI devices."
            };
        }

        var architecture = ResolveAmdArchitectureLabel(device);
        var catalogTask = LoadAmdDriverCatalogAsync();
        var scrapeTask = ScrapeAmdProductDriversAsync(device);
        await Task.WhenAll(catalogTask, scrapeTask);

        var catalogDrivers = await BuildAmdDriversFromCatalogAsync(device, await catalogTask);
        var scrapedDrivers = await scrapeTask;
        var drivers = MergeAmdDriverLists(scrapedDrivers, catalogDrivers);
        await EnrichMissingAmdDownloadUrlsAsync(drivers.Take(12).ToList());

        return new GpuDriverLookupResult
        {
            Device = device,
            Vendor = GpuDriverCatalogVendor.Amd,
            Drivers = drivers,
            ProductName = device.DisplayName,
            SeriesName = architecture,
            Message = drivers.Count == 0
                ? "AMD returned no Adrenalin driver releases for this GPU."
                : $"Loaded {drivers.Count} AMD drivers for {device.DisplayName} ({architecture})."
        };
    }

    public static async Task<GpuPreparedPackage> PrepareAmdPackageAsync(
        GpuDriverOption driver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!driver.HasDownloadUrl && !string.IsNullOrWhiteSpace(driver.DetailUrl))
        {
            var resolvedUrl = await ResolveAmdReleaseNotesDownloadUrlAsync(driver.DetailUrl);
            if (!string.IsNullOrWhiteSpace(resolvedUrl))
                driver.DownloadUrl = resolvedUrl;
        }

        var installerPath = await DownloadDriverInstallerAsync(driver, progress, cancellationToken);
        var extractedPath = GetAmdExtractedPackagePath(driver);

        await EnsureFreshAmdExtractAsync(installerPath, extractedPath, cancellationToken);

        return new GpuPreparedPackage
        {
            Vendor = GpuDriverCatalogVendor.Amd,
            InstallerPath = installerPath,
            ExtractedPath = extractedPath,
            Components = GetAmdPackageComponents(extractedPath, selectPreset: true)
        };
    }

    public static IReadOnlyList<GpuPackageComponent> GetAmdPackageComponents(string extractedPath, bool selectPreset)
    {
        var components = new List<GpuPackageComponent>();
        components.AddRange(ReadAmdManifestPackages(extractedPath, selectPreset));
        components.AddRange(ReadAmdDisplayComponents(extractedPath, selectPreset));
        components.AddRange(ReadAmdScheduledTasks(extractedPath, selectPreset));

        return components
            .OrderBy(component => component.Kind)
            .ThenByDescending(component => component.IsRequired)
            .ThenBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<GpuPackageComponent> ResolveAmdInstallComponents(
        string extractedPath,
        GpuDebloatMode mode,
        IReadOnlyList<GpuPackageComponent>? customSelections = null)
    {
        var components = mode switch
        {
            GpuDebloatMode.Stock => [],
            GpuDebloatMode.Custom when customSelections is { Count: > 0 } => customSelections.ToList(),
            GpuDebloatMode.Custom => GetAmdPackageComponents(extractedPath, selectPreset: false),
            _ => GetAmdPackageComponents(extractedPath, selectPreset: true)
        };

        if (mode == GpuDebloatMode.Stripped)
            ValidateAmdStrippedPresetSelection(components);

        return components;
    }

    public static void ApplyAmdDebloat(string extractedPath, IEnumerable<GpuPackageComponent> components)
    {
        var selected = components.ToList();
        RemoveUnselectedAmdPackages(extractedPath, selected);
        DisableUnselectedAmdScheduledTasks(extractedPath, selected);
        RemoveUnselectedAmdDisplayComponents(extractedPath, selected);
        ValidateAmdDebloatOutput(extractedPath, selected);
    }

    public static async Task EnsureFreshAmdExtractAsync(
        string installerPath,
        string extractedPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            throw new FileNotFoundException("The cached AMD installer could not be found.", installerPath);

        if (Directory.Exists(extractedPath))
            Directory.Delete(extractedPath, recursive: true);

        Directory.CreateDirectory(extractedPath);
        await ExtractAmdDriverPackageAsync(installerPath, extractedPath, cancellationToken);
    }

    public static void LaunchAmdExtractedSetup(string extractedPath)
    {
        if (!Directory.Exists(extractedPath))
            throw new DirectoryNotFoundException($"The extracted AMD folder was not found: {extractedPath}");

        var setupPath = Path.Combine(extractedPath, "Setup.exe");
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("The extracted AMD Setup.exe could not be found.", setupPath);

        Process.Start(new ProcessStartInfo(setupPath)
        {
            UseShellExecute = true,
            WorkingDirectory = extractedPath
        });
    }

    public static async Task<GpuPreparedPackage> PreparePackageAsync(
        GpuDriverOption driver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return driver.Vendor switch
        {
            GpuDriverCatalogVendor.Amd => await PrepareAmdPackageAsync(driver, progress, cancellationToken),
            _ => await PrepareNvidiaPackageAsync(driver, progress, cancellationToken)
        };
    }

    public static IReadOnlyList<GpuPackageComponent> GetPackageComponents(
        GpuDriverCatalogVendor vendor,
        string extractedPath,
        bool selectPreset)
    {
        return vendor switch
        {
            GpuDriverCatalogVendor.Amd => GetAmdPackageComponents(extractedPath, selectPreset),
            _ => GetNvidiaPackageComponents(extractedPath, selectPreset)
        };
    }

    public static void ApplyDebloat(
        GpuDriverCatalogVendor vendor,
        string extractedPath,
        IEnumerable<GpuPackageComponent> components)
    {
        switch (vendor)
        {
            case GpuDriverCatalogVendor.Amd:
                ApplyAmdDebloat(extractedPath, components);
                break;
            default:
                ApplyNvidiaDebloat(extractedPath, components);
                break;
        }
    }

    public static IReadOnlyList<GpuPackageComponent> ResolveInstallComponents(
        GpuPreparedPackage package,
        GpuDebloatMode mode)
    {
        if (mode == GpuDebloatMode.Stock)
            return [];

        if (package.Vendor == GpuDriverCatalogVendor.Amd)
            return ResolveAmdInstallComponents(package.ExtractedPath, mode, package.Components);

        return mode switch
        {
            GpuDebloatMode.Custom => package.Components.ToList(),
            _ => GetNvidiaPackageComponents(package.ExtractedPath, selectPreset: true)
        };
    }

    public static void LaunchExtractedSetup(GpuDriverCatalogVendor vendor, string extractedPath)
    {
        switch (vendor)
        {
            case GpuDriverCatalogVendor.Amd:
                LaunchAmdExtractedSetup(extractedPath);
                break;
            default:
                LaunchNvidiaExtractedSetup(extractedPath);
                break;
        }
    }

    public static async Task<GpuDriverLookupResult> GetDriversAsync(GpuDeviceInfo device)
    {
        if (device.IsNvidia)
            return await GetNvidiaDriversAsync(device);

        if (device.IsAmd)
            return await GetAmdDriversAsync(device);

        return new GpuDriverLookupResult
        {
            Device = device,
            Message = "Driver download is only supported for NVIDIA and AMD GPUs."
        };
    }

    private const string AmdGlobalDriversUrl =
        "https://www.amd.com/en/support/download/drivers.html";

    private static string ResolveAmdProductPageUrl(GpuDeviceInfo device)
    {
        var slug = BuildAmdProductSlug(device);
        var series = ResolveAmdSeriesSlug(device);
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(series))
            return AmdGlobalDriversUrl;

        return $"https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/{series}/{slug}.html";
    }

    private static string? ResolveAmdSeriesSlug(GpuDeviceInfo device)
    {
        var normalized = NormalizeName($"{device.Name} {device.DisplayName} {device.PciName}");
        foreach (var mapping in AmdSeriesMappings)
        {
            if (Regex.IsMatch(normalized, mapping.Pattern, RegexOptions.IgnoreCase))
                return mapping.SeriesSlug;
        }

        return null;
    }

    private static string BuildAmdProductSlug(GpuDeviceInfo device)
    {
        // Name is already the best WMI/PCI name selected during device discovery.
        // Combining it with DisplayName duplicates the model on most systems (for
        // example, "RX 6950 XT RX 6950 XT") and produces an invalid AMD URL.
        var rawSource = device.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawSource) ||
            rawSource.Contains("Microsoft Basic Display Adapter", StringComparison.OrdinalIgnoreCase))
        {
            rawSource = device.DisplayName?.Trim() ?? string.Empty;
        }

        // A PCI database name can describe several products sharing one device ID,
        // such as "RX 6700/6700 XT/6750 XT / 6800M". It is not an exact AMD
        // product page and must fall back to branch-filtered catalog data.
        if (rawSource.Contains('/') || rawSource.Contains('[') || rawSource.Contains(']'))
            return string.Empty;

        var source = NormalizeName(rawSource);
        source = Regex.Replace(source, @"\bAMD\b", string.Empty, RegexOptions.IgnoreCase).Trim();
        source = Regex.Replace(source, @"\bRADEON\b", "radeon", RegexOptions.IgnoreCase).Trim();
        source = Regex.Replace(source, @"\bGRAPHICS\b", string.Empty, RegexOptions.IgnoreCase).Trim();
        source = Regex.Replace(source, @"\([^)]*\)", string.Empty).Trim();
        source = Regex.Replace(source, @"\s+", " ").Trim().ToLowerInvariant();
        source = source.Replace(' ', '-');
        source = Regex.Replace(source, @"[^a-z0-9\-]", string.Empty);

        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        if (!source.StartsWith("amd-", StringComparison.Ordinal))
            source = "amd-" + source;

        return source;
    }

    private static async Task<string> FetchAmdPageAsync(string url)
    {
        var result = await FetchAmdPageWithFinalUriAsync(url);
        return result.Html;
    }

    private static async Task<(string Html, Uri FinalUri)> FetchAmdPageWithFinalUriAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", AmdHttpUserAgent);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri
                       ?? throw new InvalidOperationException("AMD returned a response without a final address.");
        return (await response.Content.ReadAsStringAsync(), finalUri);
    }

    private static List<GpuDriverOption> ParseAmdDriverLinks(string html, string sourceUrl)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AmdDriverLinkRegex().Matches(html))
            candidates.Add(NormalizeAmdDownloadUrl(match.Value));

        foreach (Match match in AmdHrefDriverLinkRegex().Matches(html))
        {
            var href = match.Groups[1].Value;
            if (href.Contains("drivers.amd.com", StringComparison.OrdinalIgnoreCase))
                candidates.Add(NormalizeAmdDownloadUrl(href));
        }

        var drivers = new List<GpuDriverOption>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawUrl in candidates)
        {
            if (rawUrl.Contains("minimalsetup", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.Contains("/installer/comp/", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.Contains("rgb_led", StringComparison.OrdinalIgnoreCase) ||
                rawUrl.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rawUrl.Contains("adrenalin-edition", StringComparison.OrdinalIgnoreCase) &&
                !rawUrl.Contains("amd-software", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloadUrl = rawUrl;
            if (!seenUrls.Add(downloadUrl))
                continue;

            var version = ExtractAmdVersionFromUrl(downloadUrl);
            if (string.IsNullOrWhiteSpace(version))
                continue;

            var isOptional = !downloadUrl.Contains("whql", StringComparison.OrdinalIgnoreCase);

            drivers.Add(new GpuDriverOption
            {
                Vendor = GpuDriverCatalogVendor.Amd,
                Name = isOptional ? "AMD Adrenalin Optional" : "AMD Adrenalin WHQL",
                Version = version,
                ReleaseDate = string.Empty,
                DownloadUrl = downloadUrl,
                DetailUrl = sourceUrl,
                OperatingSystem = downloadUrl.Contains("win11", StringComparison.OrdinalIgnoreCase)
                    ? "Windows 11"
                    : "Windows 10/11",
                Type = isOptional ? "Optional" : "WHQL"
            });
        }

        return drivers
            .OrderByDescending(driver => Version.TryParse(driver.Version, out var parsed) ? parsed : new Version())
            .ThenByDescending(driver => driver.Type.Equals("WHQL", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string NormalizeAmdDownloadUrl(string url)
    {
        var normalized = WebUtility.HtmlDecode(url.Trim());
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            normalized = "https:" + normalized;
        else if (!normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            normalized = "https://" + normalized.TrimStart('/');

        return normalized;
    }

    private static string ExtractAmdVersionFromUrl(string url)
    {
        var match = AmdVersionRegex().Match(url);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static async Task ExtractAmdDriverPackageAsync(
        string installerPath,
        string extractedPath,
        CancellationToken cancellationToken)
    {
        var sevenZipPath = FindSevenZipExecutable();
        if (sevenZipPath is null)
        {
            throw new InvalidOperationException(
                "7-Zip is required to safely extract AMD drivers without running the AMD installer. Install 7-Zip and try again.");
        }

        var exitCode = await RunProcessAsync(
            sevenZipPath,
            $"x -y -o\"{extractedPath}\" \"{installerPath}\"",
            cancellationToken);

        if (exitCode != 0)
            throw new InvalidOperationException($"7-Zip extraction failed with exit code {exitCode}.");

        if (!File.Exists(Path.Combine(extractedPath, "Setup.exe")))
            throw new InvalidOperationException("The AMD package was extracted, but Setup.exe was not found.");
    }

    private static string GetAmdExtractedPackagePath(GpuDriverOption driver)
    {
        var identitySource = string.IsNullOrWhiteSpace(driver.DownloadUrl)
            ? driver.Version
            : driver.DownloadUrl;
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identitySource)))[..12]
            .ToLowerInvariant();
        var folderName = $"amd-driver-{SanitizePathPart(driver.Version)}-{identity}";
        return Path.Combine(Path.GetTempPath(), "SynToolkit", "GpuDrivers", "Extracted", "Amd", folderName);
    }

    private static IEnumerable<GpuPackageComponent> ReadAmdManifestPackages(string extractedPath, bool selectPreset)
    {
        var seenProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in AmdManifestFiles)
        {
            var manifestPath = Path.Combine(extractedPath, relativePath);
            if (!File.Exists(manifestPath))
                continue;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(manifestPath));
            }
            catch (JsonException)
            {
                continue;
            }

            if (root?["Packages"]?["Package"] is not JsonArray packages)
                continue;

            foreach (var packageNode in packages)
            {
                var info = packageNode?["Info"];
                if (info is null)
                    continue;

                var productName = info["productName"]?.GetValue<string>() ?? "Unknown package";
                if (!seenProducts.Add(productName))
                    continue;

                var description = info["Description"]?.GetValue<string>() ?? productName;
                var required = AmdStrippedPresetPolicy.IsCoreDisplayPackage(productName, description);
                var selected = required || (selectPreset
                    ? AmdStrippedPresetPolicy.ShouldKeepPackage(productName, description)
                    : true);

                yield return new GpuPackageComponent
                {
                    Name = productName,
                    Description = description,
                    FullPath = $"package|{productName}",
                    Kind = GpuPackageComponentKind.Package,
                    IsDirectory = false,
                    IsRequired = required,
                    IsSelected = selected
                };
            }
        }
    }

    private static IEnumerable<GpuPackageComponent> ReadAmdDisplayComponents(string extractedPath, bool selectPreset)
    {
        var displayRoot = FindAmdDisplayInfRoot(extractedPath);
        if (displayRoot is null)
            yield break;

        foreach (var directory in Directory.EnumerateDirectories(displayRoot))
        {
            var infFiles = Directory.GetFiles(directory, "*.inf", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (infFiles.Length == 0)
                continue;

            var folderName = Path.GetFileName(directory);
            var infFileName = Path.GetFileName(infFiles[0]);
            var infDescription = ReadAmdInfDescription(infFiles[0]);
            var friendlyName = ResolveAmdDisplayFriendlyName(folderName, infDescription);
            const bool required = false;
            var selected = selectPreset
                ? AmdStrippedPresetPolicy.ShouldKeepDisplayComponent(folderName, infFileName, infDescription)
                : true;

            yield return new GpuPackageComponent
            {
                Name = friendlyName,
                Description = infDescription,
                FullPath = directory,
                Kind = GpuPackageComponentKind.DisplayDriver,
                IsDirectory = true,
                IsRequired = required,
                IsSelected = selected
            };
        }
    }

    private static IEnumerable<GpuPackageComponent> ReadAmdScheduledTasks(string extractedPath, bool selectPreset)
    {
        var configDir = Path.Combine(extractedPath, "Config");
        if (!Directory.Exists(configDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(configDir, "*.xml", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(file).StartsWith("Monet", StringComparison.OrdinalIgnoreCase))
                continue;

            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch
            {
                continue;
            }

            if (document.Root is null ||
                !document.Root.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
                continue;

            var ns = document.Root.GetDefaultNamespace();
            var description = document.Root.Element(ns + "RegistrationInfo")?.Element(ns + "Description")?.Value
                              ?? Path.GetFileNameWithoutExtension(file);
            var selected = selectPreset
                ? AmdStrippedPresetPolicy.ShouldKeepScheduledTask(description, Path.GetFileName(file))
                : true;

            yield return new GpuPackageComponent
            {
                Name = description,
                Description = Path.GetFileName(file),
                FullPath = file,
                Kind = GpuPackageComponentKind.ScheduledTask,
                IsDirectory = false,
                IsRequired = false,
                IsSelected = selected
            };
        }
    }

    private static void RemoveUnselectedAmdPackages(string extractedPath, IReadOnlyList<GpuPackageComponent> components)
    {
        var namesToRemove = components
            .Where(component => component.Kind == GpuPackageComponentKind.Package &&
                                !component.IsRequired &&
                                !component.IsSelected)
            .Select(component => component.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in AmdManifestFiles)
        {
            var manifestPath = Path.Combine(extractedPath, relativePath);
            if (!File.Exists(manifestPath))
                continue;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(manifestPath));
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"AMD manifest '{relativePath}' could not be read.", ex);
            }

            if (root?["Packages"]?["Package"] is not JsonArray packages)
                throw new InvalidOperationException($"AMD manifest '{relativePath}' has an unsupported package layout.");

            for (var index = packages.Count - 1; index >= 0; index--)
            {
                var productName = packages[index]?["Info"]?["productName"]?.GetValue<string>();
                if (productName is not null && namesToRemove.Contains(productName))
                    packages.RemoveAt(index);
            }

            File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void DisableUnselectedAmdScheduledTasks(string extractedPath, IReadOnlyList<GpuPackageComponent> components)
    {
        foreach (var component in components.Where(component =>
                     component.Kind == GpuPackageComponentKind.ScheduledTask &&
                     !component.IsSelected))
        {
            if (!File.Exists(component.FullPath))
                continue;

            XDocument document;
            try
            {
                document = XDocument.Load(component.FullPath);
            }
            catch
            {
                continue;
            }

            if (document.Root is null)
                continue;

            var ns = document.Root.GetDefaultNamespace();
            var settings = document.Root.Element(ns + "Settings");
            if (settings is null)
            {
                settings = new XElement(ns + "Settings");
                var actions = document.Root.Element(ns + "Actions");
                if (actions is not null)
                    actions.AddBeforeSelf(settings);
                else
                    document.Root.Add(settings);
            }

            var enabled = settings.Element(ns + "Enabled");
            if (enabled is null)
                settings.Add(new XElement(ns + "Enabled", false));
            else
                enabled.SetValue(false);

            var hidden = settings.Element(ns + "Hidden");
            if (hidden is null)
                settings.Add(new XElement(ns + "Hidden", false));
            else
                hidden.SetValue(false);

            document.Save(component.FullPath);
        }
    }

    private static void RemoveUnselectedAmdDisplayComponents(string extractedPath, IReadOnlyList<GpuPackageComponent> components)
    {
        foreach (var component in components.Where(component =>
                     component.Kind == GpuPackageComponentKind.DisplayDriver &&
                     !string.IsNullOrWhiteSpace(component.FullPath) &&
                     Directory.Exists(component.FullPath) &&
                     !component.IsRequired &&
                     !component.IsSelected))
        {
            Directory.Delete(component.FullPath, recursive: true);
        }
    }

    private static void ValidateAmdStrippedPresetSelection(IReadOnlyList<GpuPackageComponent> components)
    {
        var selectedPackages = components
            .Where(component => component.Kind == GpuPackageComponentKind.Package &&
                                (component.IsRequired || component.IsSelected))
            .ToList();

        if (!selectedPackages.Any(component =>
                AmdStrippedPresetPolicy.IsCoreDisplayPackage(component.Name, component.Description)))
        {
            throw new InvalidOperationException(
                "The AMD package does not expose its Display Driver entry, so the stripped preset cannot be applied safely.");
        }

        if (!selectedPackages.Any(component =>
                AmdStrippedPresetPolicy.IsSettingsPackage(component.Name, component.Description)))
        {
            throw new InvalidOperationException(
                "The AMD package does not expose its Settings entry, so the approved stripped preset cannot be applied safely.");
        }
    }

    private static void ValidateAmdDebloatOutput(
        string extractedPath,
        IReadOnlyList<GpuPackageComponent> components)
    {
        if (!File.Exists(Path.Combine(extractedPath, "Setup.exe")))
            throw new InvalidOperationException("AMD Setup.exe is missing after package preparation.");

        var remainingPackages = ReadAmdManifestProductNames(extractedPath);
        foreach (var component in components.Where(component =>
                     component.Kind == GpuPackageComponentKind.Package))
        {
            var shouldRemain = component.IsRequired || component.IsSelected;
            var remains = remainingPackages.Contains(component.Name);
            if (shouldRemain && !remains)
                throw new InvalidOperationException($"Required AMD package '{component.Name}' was removed unexpectedly.");
            if (!shouldRemain && remains)
                throw new InvalidOperationException($"AMD package '{component.Name}' is still enabled after stripping.");
        }

        foreach (var component in components.Where(component =>
                     component.Kind == GpuPackageComponentKind.DisplayDriver))
        {
            var shouldRemain = component.IsRequired || component.IsSelected;
            var remains = Directory.Exists(component.FullPath);
            if (shouldRemain && !remains)
                throw new InvalidOperationException($"Required AMD display component '{component.Name}' is missing.");
            if (!shouldRemain && remains)
                throw new InvalidOperationException($"AMD display component '{component.Name}' was not removed.");
        }

        foreach (var component in components.Where(component =>
                     component.Kind == GpuPackageComponentKind.ScheduledTask &&
                     !component.IsSelected))
        {
            if (!IsAmdTaskDisabled(component.FullPath))
                throw new InvalidOperationException($"AMD scheduled task '{component.Name}' is still enabled.");
        }
    }

    private static HashSet<string> ReadAmdManifestProductNames(string extractedPath)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in AmdManifestFiles)
        {
            var manifestPath = Path.Combine(extractedPath, relativePath);
            if (!File.Exists(manifestPath))
                continue;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(manifestPath));
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"AMD manifest '{relativePath}' could not be verified.", ex);
            }

            if (root?["Packages"]?["Package"] is not JsonArray packages)
                throw new InvalidOperationException($"AMD manifest '{relativePath}' has an unsupported package layout.");

            foreach (var package in packages)
            {
                var productName = package?["Info"]?["productName"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(productName))
                    names.Add(productName);
            }
        }

        return names;
    }

    private static bool IsAmdTaskDisabled(string taskPath)
    {
        if (!File.Exists(taskPath))
            return false;

        try
        {
            var document = XDocument.Load(taskPath);
            if (document.Root is null)
                return false;

            var ns = document.Root.GetDefaultNamespace();
            return bool.TryParse(
                       document.Root.Element(ns + "Settings")?.Element(ns + "Enabled")?.Value,
                       out var enabled) &&
                   !enabled;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindAmdDisplayInfRoot(string extractedPath)
    {
        var displayRoot = Path.Combine(extractedPath, "Packages", "Drivers", "Display");
        if (!Directory.Exists(displayRoot))
            return null;

        foreach (var directory in Directory.EnumerateDirectories(displayRoot, "*_INF", SearchOption.TopDirectoryOnly))
            return directory;

        return Directory.EnumerateDirectories(displayRoot).FirstOrDefault();
    }

    private static string ResolveAmdDisplayFriendlyName(string folderName, string infDescription)
    {
        foreach (var entry in AmdDisplayFriendlyNames)
        {
            if (folderName.Contains(entry.Key, StringComparison.OrdinalIgnoreCase) ||
                infDescription.Contains(entry.Key, StringComparison.OrdinalIgnoreCase) ||
                infDescription.Contains(entry.Value, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(infDescription) &&
            !infDescription.Equals(folderName, StringComparison.OrdinalIgnoreCase))
        {
            return infDescription.Length > 48 ? infDescription[..48] : infDescription;
        }

        return folderName;
    }

    private static string ReadAmdInfDescription(string infPath)
    {
        var inStrings = false;
        foreach (var line in File.ReadLines(infPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[Strings]", StringComparison.OrdinalIgnoreCase))
            {
                inStrings = true;
                continue;
            }

            if (inStrings && trimmed.StartsWith('['))
                break;

            if (!inStrings)
                continue;

            if ((trimmed.Contains("desc", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("AMDOCLName", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("AMDWINName", StringComparison.OrdinalIgnoreCase)) &&
                trimmed.Contains("\""))
            {
                var start = trimmed.IndexOf('"');
                var end = trimmed.LastIndexOf('"');
                if (end > start)
                    return trimmed.Substring(start + 1, end - start - 1);
            }
        }

        return Path.GetFileNameWithoutExtension(infPath);
    }

    private const string AmdHttpUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    [GeneratedRegex(@"drivers\.amd\.com/drivers/(?:whql-)?amd(?:-software)?(?:-software)?-adrenalin(?:-edition)?-[\d\.]+-win(?:10|11)?(?:-win11)?[^\s""<>]+\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex AmdDriverLinkRegex();

    [GeneratedRegex(@"href=[""']([^""']*drivers\.amd\.com/drivers/[^""']+\.exe)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AmdHrefDriverLinkRegex();

    [GeneratedRegex(@"adrenalin(?:-edition)?-([\d\.]+)-win", RegexOptions.IgnoreCase)]
    private static partial Regex AmdVersionRegex();
}
