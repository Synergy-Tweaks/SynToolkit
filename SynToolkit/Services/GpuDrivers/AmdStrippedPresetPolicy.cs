#nullable enable

using System.IO;
namespace SynToolkit.Services.GpuDrivers;

internal static class AmdStrippedPresetPolicy
{
    private static readonly HashSet<string> PackageKeepIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "amddisplaydriver",
        "displaydriver",
        "amdsettings",
        "radeonsettings"
    };

    private static readonly string[] DisplayComponentKeepPrefixes =
    [
        "amdocl",
        "amdogl",
        "amdpcibridge",
        "amdvlk",
        "amdwin",
        "amdxe"
    ];

    public static bool IsCoreDisplayPackage(string productName, string description)
    {
        var productId = Normalize(productName);
        if (productId is "amddisplaydriver" or "displaydriver")
            return true;

        var descriptionId = Normalize(description);
        return descriptionId.StartsWith("displaydriverforwindows", StringComparison.OrdinalIgnoreCase) ||
               descriptionId == "amddisplaydriver";
    }

    public static bool IsSettingsPackage(string productName, string description)
    {
        var productId = Normalize(productName);
        if (productId is "amdsettings" or "radeonsettings")
            return true;

        var descriptionId = Normalize(description);
        return descriptionId is "amdcatalystsettings" or "amdsettings" or "radeonsettings";
    }

    public static bool ShouldKeepPackage(string productName, string description)
    {
        var productId = Normalize(productName);
        return PackageKeepIds.Contains(productId) ||
               IsCoreDisplayPackage(productName, description) ||
               IsSettingsPackage(productName, description);
    }

    public static bool ShouldKeepDisplayComponent(
        string directoryName,
        string infFileName,
        string description)
    {
        var identifiers = GetDisplayComponentIdentifiers(directoryName, infFileName, description);

        return identifiers.Any(identifier =>
            DisplayComponentKeepPrefixes.Any(prefix =>
                identifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool ShouldKeepScheduledTask(string description, string fileName) => false;

    private static string[] GetDisplayComponentIdentifiers(
        string directoryName,
        string infFileName,
        string description) =>
    [
        Normalize(directoryName),
        Normalize(Path.GetFileNameWithoutExtension(infFileName)),
        Normalize(description)
    ];

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
