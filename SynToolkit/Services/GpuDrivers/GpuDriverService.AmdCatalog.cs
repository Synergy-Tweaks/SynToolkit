#nullable enable

using System.Net.Http;
using System.IO;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

public static partial class GpuDriverService
{
    private const string AmdVersionsCatalogUrl =
        "https://raw.githubusercontent.com/GPUOpen-Drivers/amd-vulkan-versions/master/amdversions.xml";

    private static readonly SemaphoreSlim AmdCatalogLock = new(1, 1);
    private static IReadOnlyList<AmdCatalogEntry>? _cachedCatalog;
    private static DateTime _catalogLoadedUtc;
    private static readonly Dictionary<string, string> ReleaseNotesDownloadCache =
        new(StringComparer.OrdinalIgnoreCase);

    private enum AmdGpuBranch
    {
        Rdna12,
        Rdna34,
        Legacy,
        Unknown
    }

    private sealed record AmdCatalogEntry(
        string Version,
        string Whql,
        string ReleaseNotesUrl,
        string InternalVersion,
        string WindowsVersion,
        string ReleaseDate,
        bool IsRdna12,
        bool IsRdna34Only,
        bool IsLegacy);

    public static string ResolveAmdArchitectureLabel(GpuDeviceInfo device)
    {
        if (!device.IsAmd)
            return string.Empty;

        var normalized = NormalizeName($"{device.Name} {device.DisplayName} {device.PciName}");
        if (Regex.IsMatch(normalized, @"\bRX\s*9[0-9]{3}\b", RegexOptions.IgnoreCase))
            return "RDNA 4";
        if (Regex.IsMatch(normalized, @"\bRX\s*7[0-9]{3}\b", RegexOptions.IgnoreCase))
            return "RDNA 3";
        if (Regex.IsMatch(normalized, @"\bRX\s*6[0-9]{3}\b", RegexOptions.IgnoreCase))
            return "RDNA 2";
        if (Regex.IsMatch(normalized, @"\bRX\s*5[0-9]{3}\b", RegexOptions.IgnoreCase))
            return "RDNA";
        if (Regex.IsMatch(normalized, @"\bVEGA\b", RegexOptions.IgnoreCase))
            return "Vega";
        if (Regex.IsMatch(normalized, @"\bPOLARIS\b|\bRX\s*4[0-9]{2}\b|\bRX\s*5[0-9]{2}\b", RegexOptions.IgnoreCase))
            return "Polaris / Legacy";

        return ResolveAmdGpuBranch(device) switch
        {
            AmdGpuBranch.Legacy => "Polaris / Vega",
            AmdGpuBranch.Rdna12 => "RDNA 1 / RDNA 2",
            AmdGpuBranch.Rdna34 => "RDNA 3 / RDNA 4",
            _ => "Radeon"
        };
    }

    private static async Task<IReadOnlyList<AmdCatalogEntry>> LoadAmdDriverCatalogAsync()
    {
        if (_cachedCatalog is not null && DateTime.UtcNow - _catalogLoadedUtc < TimeSpan.FromHours(6))
            return _cachedCatalog;

        await AmdCatalogLock.WaitAsync();
        try
        {
            if (_cachedCatalog is not null && DateTime.UtcNow - _catalogLoadedUtc < TimeSpan.FromHours(6))
                return _cachedCatalog;

            using var request = new HttpRequestMessage(HttpMethod.Get, AmdVersionsCatalogUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", AmdHttpUserAgent);
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync();
            _cachedCatalog = ParseAmdVersionsXml(xml);
            _catalogLoadedUtc = DateTime.UtcNow;
            return _cachedCatalog;
        }
        finally
        {
            AmdCatalogLock.Release();
        }
    }

    private static List<AmdCatalogEntry> ParseAmdVersionsXml(string xml)
    {
        var document = XDocument.Parse(xml);
        var entries = new List<AmdCatalogEntry>();

        foreach (var element in document.Descendants("driver"))
        {
            var os = element.Attribute("operating-system")?.Value ?? string.Empty;
            if (!os.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                continue;

            var version = element.Attribute("version")?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(version))
                continue;

            var releaseNotesUrl = element.Element("download-url")?.Value?.Trim() ?? string.Empty;
            var windowsVersion = element.Element("windows-version")?.Value?.Trim() ?? string.Empty;
            var internalVersion = element.Element("internal-version")?.Value?.Trim() ?? string.Empty;
            var whql = element.Element("whql")?.Value?.Trim() ?? string.Empty;
            var releaseDate = element.Element("release-date")?.Value?.Trim() ?? string.Empty;

            var isRdna12 = version.Contains("RDNA1 and RDNA2", StringComparison.OrdinalIgnoreCase) ||
                           version.Contains("RDNA 1 and RDNA 2", StringComparison.OrdinalIgnoreCase);
            var isRdna34Only = internalVersion.StartsWith("26.", StringComparison.Ordinal) ||
                               windowsVersion.StartsWith("32.0.3", StringComparison.Ordinal);
            var isLegacy = version.Contains("Polaris", StringComparison.OrdinalIgnoreCase) ||
                           version.Contains("Vega", StringComparison.OrdinalIgnoreCase) ||
                           releaseNotesUrl.Contains("polaris-vega", StringComparison.OrdinalIgnoreCase) ||
                           windowsVersion.StartsWith("31.", StringComparison.Ordinal) ||
                           internalVersion.StartsWith("23.", StringComparison.Ordinal);

            entries.Add(new AmdCatalogEntry(
                version,
                whql,
                releaseNotesUrl,
                internalVersion,
                windowsVersion,
                releaseDate,
                isRdna12,
                isRdna34Only,
                isLegacy));
        }

        return entries;
    }

    private static AmdGpuBranch ResolveAmdGpuBranch(GpuDeviceInfo device)
    {
        var normalized = NormalizeName($"{device.Name} {device.DisplayName} {device.PciName}");
        if (Regex.IsMatch(normalized,
                @"\bRX\s*[56][0-9]{3}\b|\bNAVI\s*(?:1[024]|2[1234])\b",
                RegexOptions.IgnoreCase))
            return AmdGpuBranch.Rdna12;

        if (Regex.IsMatch(normalized,
                @"\bRX\s*[79][0-9]{3}\b|\bNAVI\s*(?:3[123]|4[48])\b",
                RegexOptions.IgnoreCase))
            return AmdGpuBranch.Rdna34;

        if (Regex.IsMatch(normalized, @"\bRX\s*[45][0-9]{2}\b|\bVEGA\b|\bPOLARIS\b", RegexOptions.IgnoreCase))
            return AmdGpuBranch.Legacy;

        if (int.TryParse(device.DeviceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var deviceId))
        {
            if ((deviceId >= 0x7310 && deviceId <= 0x736F) ||
                (deviceId >= 0x73A0 && deviceId <= 0x743F && deviceId != 0x73F0))
            {
                return AmdGpuBranch.Rdna12;
            }

            if (deviceId == 0x73F0 || deviceId is >= 0x7440 and <= 0x759F)
                return AmdGpuBranch.Rdna34;
        }

        return AmdGpuBranch.Unknown;
    }

    private static IEnumerable<AmdCatalogEntry> FilterCatalogForDevice(
        IEnumerable<AmdCatalogEntry> catalog,
        GpuDeviceInfo device)
    {
        var entries = catalog.ToList();
        var splitRdna12Versions = entries
            .Where(entry => entry.IsRdna12)
            .Select(entry => NormalizeAmdCatalogVersion(entry.Version))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<AmdCatalogEntry> filtered = ResolveAmdGpuBranch(device) switch
        {
            AmdGpuBranch.Legacy => entries.Where(entry => entry.IsLegacy),
            AmdGpuBranch.Rdna12 => entries.Where(entry =>
                !entry.IsLegacy &&
                (entry.IsRdna12 ||
                 (!entry.IsRdna34Only &&
                  !splitRdna12Versions.Contains(NormalizeAmdCatalogVersion(entry.Version))))),
            AmdGpuBranch.Rdna34 => entries.Where(entry => !entry.IsLegacy && !entry.IsRdna12),
            _ => entries.Where(entry =>
                !entry.IsLegacy &&
                !entry.IsRdna12 &&
                !entry.IsRdna34Only &&
                !splitRdna12Versions.Contains(NormalizeAmdCatalogVersion(entry.Version)))
        };

        return filtered
            .GroupBy(entry => NormalizeAmdCatalogVersion(entry.Version), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.ReleaseDate).First())
            .OrderByDescending(entry => ParseAmdCatalogVersion(entry.Version))
            .ThenByDescending(entry => entry.ReleaseDate);
    }

    private static async Task<List<GpuDriverOption>> BuildAmdDriversFromCatalogAsync(
        GpuDeviceInfo device,
        IReadOnlyList<AmdCatalogEntry> catalog)
    {
        var filtered = FilterCatalogForDevice(catalog, device).Take(24).ToList();
        var results = new List<GpuDriverOption>();
        var seenVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in filtered.Chunk(4))
        {
            var resolved = batch.Select(BuildCatalogDriverOption).ToList();
            foreach (var driver in resolved)
            {
                if (driver is null || !seenVersions.Add(driver.Version))
                    continue;

                results.Add(driver);
            }

            await Task.Yield();
        }

        return results;
    }

    private static GpuDriverOption? BuildCatalogDriverOption(AmdCatalogEntry entry)
    {
        var version = NormalizeAmdCatalogVersion(entry.Version);
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var isOptional = entry.Whql.Contains("Optional", StringComparison.OrdinalIgnoreCase) ||
                         entry.Version.Contains("Hotfix", StringComparison.OrdinalIgnoreCase);

        return new GpuDriverOption
        {
            Vendor = GpuDriverCatalogVendor.Amd,
            Name = isOptional ? "AMD Adrenalin Optional" : "AMD Adrenalin WHQL",
            Version = version,
            ReleaseDate = FormatAmdReleaseDate(entry.ReleaseDate),
            DownloadUrl = string.Empty,
            DetailUrl = entry.ReleaseNotesUrl,
            OperatingSystem = "Windows 10/11",
            Type = isOptional ? "Optional" : "WHQL"
        };
    }

    private static async Task<string?> ResolveAmdReleaseNotesDownloadUrlAsync(string releaseNotesUrl)
    {
        if (string.IsNullOrWhiteSpace(releaseNotesUrl))
            return null;

        if (ReleaseNotesDownloadCache.TryGetValue(releaseNotesUrl, out var cached))
            return cached;

        if (releaseNotesUrl.Contains("drivers.amd.com", StringComparison.OrdinalIgnoreCase) &&
            releaseNotesUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ReleaseNotesDownloadCache[releaseNotesUrl] = NormalizeAmdDownloadUrl(releaseNotesUrl);
            return ReleaseNotesDownloadCache[releaseNotesUrl];
        }

        try
        {
            var html = await FetchAmdPageAsync(releaseNotesUrl);
            var links = ParseAmdDriverLinks(html, releaseNotesUrl)
                .Where(driver => !string.IsNullOrWhiteSpace(driver.DownloadUrl))
                .OrderByDescending(driver => driver.Type.Equals("WHQL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var url = links.FirstOrDefault()?.DownloadUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                ReleaseNotesDownloadCache[releaseNotesUrl] = url;
                return url;
            }
        }
        catch
        {
        }

        return null;
    }

    private static List<GpuDriverOption> MergeAmdDriverLists(
        IEnumerable<GpuDriverOption> primary,
        IEnumerable<GpuDriverOption> secondary)
    {
        var merged = new Dictionary<string, GpuDriverOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in primary.Concat(secondary))
        {
            if (string.IsNullOrWhiteSpace(driver.Version))
                continue;

            if (!merged.TryGetValue(driver.Version, out var existing))
            {
                merged[driver.Version] = driver;
                continue;
            }

            merged[driver.Version] = new GpuDriverOption
            {
                Vendor = GpuDriverCatalogVendor.Amd,
                Name = !string.IsNullOrWhiteSpace(driver.Name) ? driver.Name : existing.Name,
                Version = driver.Version,
                ReleaseDate = !string.IsNullOrWhiteSpace(driver.ReleaseDate) ? driver.ReleaseDate : existing.ReleaseDate,
                DownloadUrl = !string.IsNullOrWhiteSpace(driver.DownloadUrl) ? driver.DownloadUrl : existing.DownloadUrl,
                DetailUrl = !string.IsNullOrWhiteSpace(driver.DetailUrl) ? driver.DetailUrl : existing.DetailUrl,
                OperatingSystem = driver.OperatingSystem,
                Type = !string.IsNullOrWhiteSpace(driver.Type) ? driver.Type : existing.Type
            };
        }

        return merged.Values
            .OrderByDescending(driver => Version.TryParse(driver.Version, out var parsed) ? parsed : new Version())
            .ThenByDescending(driver => driver.Type.Equals("WHQL", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task<List<GpuDriverOption>> ScrapeAmdProductDriversAsync(GpuDeviceInfo device)
    {
        var url = ResolveAmdProductPageUrl(device);
        if (url.Equals(AmdGlobalDriversUrl, StringComparison.OrdinalIgnoreCase))
            return [];

        try
        {
            var page = await FetchAmdPageWithFinalUriAsync(url);
            var expectedFile = Path.GetFileName(new Uri(url).AbsolutePath);
            var actualFile = Path.GetFileName(page.FinalUri.AbsolutePath);
            if (!actualFile.Equals(expectedFile, StringComparison.OrdinalIgnoreCase))
                return [];

            return ParseAmdDriverLinks(page.Html, url);
        }
        catch
        {
            // The version catalog remains available as the safe fallback. Never
            // substitute a different GPU product page: AMD can publish distinct
            // installers for the same Adrenalin version and architecture branch.
            return [];
        }
    }

    private static string NormalizeAmdCatalogVersion(string version)
    {
        var match = Regex.Match(version, @"(\d+\.\d+\.\d+)");
        return match.Success ? match.Groups[1].Value : version.Trim();
    }

    private static Version ParseAmdCatalogVersion(string version)
    {
        return Version.TryParse(NormalizeAmdCatalogVersion(version), out var parsed)
            ? parsed
            : new Version();
    }

    private static string FormatAmdReleaseDate(string isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate))
            return string.Empty;

        if (DateTime.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToString("yyyy-MM-dd");

        return isoDate;
    }

    private static async Task EnrichMissingAmdDownloadUrlsAsync(IReadOnlyList<GpuDriverOption> drivers)
    {
        foreach (var driver in drivers)
        {
            if (driver.HasDownloadUrl || string.IsNullOrWhiteSpace(driver.DetailUrl))
                continue;

            var url = await ResolveAmdReleaseNotesDownloadUrlAsync(driver.DetailUrl);
            if (!string.IsNullOrWhiteSpace(url))
                driver.DownloadUrl = url;
        }
    }
}
