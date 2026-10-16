using System.Text.Json;

namespace SynToolkit.SystemInformationTests;

internal static class PowerPlanManifestTests
{
    public static void ManifestMatchesDisk()
    {
        string plansDirectory = FindPowerPlansDirectory();
        string manifestPath = Path.Combine(plansDirectory, "manifest.json");
        True(File.Exists(manifestPath), "manifest.json must exist beside the bundled .pow files.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        True(document.RootElement.TryGetProperty("plans", out JsonElement plans), "manifest.json must contain a plans array.");
        True(plans.ValueKind == JsonValueKind.Array, "plans must be an array.");

        var fileNames = new List<string>();
        var displayNames = new List<string>();
        foreach (JsonElement plan in plans.EnumerateArray())
        {
            string fileName = RequiredString(plan, "fileName");
            string displayName = RequiredString(plan, "displayName");
            RequiredString(plan, "description");
            fileNames.Add(fileName);
            displayNames.Add(displayName);

            string planPath = Path.Combine(plansDirectory, fileName);
            True(File.Exists(planPath), $"Manifest fileName '{fileName}' must exist on disk.");
        }

        True(NoCaseInsensitiveDuplicates(fileNames), "fileName values must be unique, ignoring case.");
        True(NoCaseInsensitiveDuplicates(displayNames), "displayName values must be unique, ignoring case.");
        True(
            !fileNames.Any(name => name.Equals("SOS.pow", StringComparison.OrdinalIgnoreCase)),
            "SOS.pow must not be listed in the manifest.");

        foreach (string powPath in Directory.EnumerateFiles(plansDirectory, "*.pow", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(powPath);
            if (fileName.Equals("SOS.pow", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            True(
                fileNames.Any(name => name.Equals(fileName, StringComparison.OrdinalIgnoreCase)),
                $"'{fileName}' is on disk and must have a manifest entry.");
        }
    }

    private static string FindPowerPlansDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "SynToolkit", "Assets", "PowerPlans");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate SynToolkit/Assets/PowerPlans/manifest.json.");
    }

    private static string RequiredString(JsonElement plan, string propertyName)
    {
        True(plan.TryGetProperty(propertyName, out JsonElement property), $"Each plan must include {propertyName}.");
        True(property.ValueKind == JsonValueKind.String, $"{propertyName} must be a string.");
        string? value = property.GetString();
        True(!string.IsNullOrWhiteSpace(value), $"{propertyName} must be non-empty.");
        return value!;
    }

    private static bool NoCaseInsensitiveDuplicates(IReadOnlyList<string> values)
    {
        return values
            .Select(value => value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count() == values.Count;
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
