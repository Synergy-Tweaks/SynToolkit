namespace Roomy.Core;

public static class CacheFinder
{
    /// <summary>
    /// Chromium cache folders inside an app's folder: "Cache", "Code Cache" and "GPUCache" that sit
    /// next to each other (or a "Cache" holding "Cache_Data"). Matching that layout means only browser
    /// caches are picked, never an app's own data that happens to be called "cache".
    /// </summary>
    public static List<string> ChromiumCachesUnder(string root)
    {
        var found = new List<string>();
        if (!Directory.Exists(root)) return found;
        string[] names = ["Cache", "Code Cache", "GPUCache"];
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var dir in Directory.EnumerateDirectories(root, "*", options))
            {
                if (!names.Contains(Path.GetFileName(dir))) continue;
                if (found.Any(f => dir.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                var parent = Path.GetDirectoryName(dir)!;
                bool chromium = Directory.Exists(Path.Combine(parent, "Code Cache")) || Directory.Exists(Path.Combine(parent, "GPUCache")) ||
                                Directory.Exists(Path.Combine(parent, "Cache", "Cache_Data"));
                if (chromium) found.Add(dir);
            }
        }
        catch { }
        return found;
    }
}
