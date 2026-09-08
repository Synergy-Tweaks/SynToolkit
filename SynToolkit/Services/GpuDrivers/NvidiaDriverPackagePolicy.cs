using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

public static class NvidiaDriverPackagePolicy
{
    public static NvidiaDriverPlatform Classify(string gpuName, bool isPortableComputer)
    {
        var normalized = NormalizeName(gpuName);
        if (normalized.Contains("LAPTOP", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("NOTEBOOK", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("MOBILE", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\bMX\s*\d{3}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\b(?:GTX|GEFORCE)\s*\d{3}M\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return NvidiaDriverPlatform.Notebook;
        }

        return isPortableComputer ? NvidiaDriverPlatform.Notebook : NvidiaDriverPlatform.Desktop;
    }

    public static string BuildInstallerUrl(string version, NvidiaDriverPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        var normalizedVersion = version.Trim();
        var packageType = platform == NvidiaDriverPlatform.Notebook ? "notebook" : "desktop";
        var fileName = $"{normalizedVersion}-{packageType}-win10-win11-64bit-international-dch-whql.exe";
        return $"https://us.download.nvidia.com/Windows/{Uri.EscapeDataString(normalizedVersion)}/{Uri.EscapeDataString(fileName)}";
    }

    private static string NormalizeName(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value.ToUpperInvariant(), @"[^\w]+", " ").Trim();
}
