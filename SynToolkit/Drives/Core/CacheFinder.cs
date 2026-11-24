// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.IO;

#nullable enable

namespace SynToolkit.Drives.Core;

public static class CacheFinder
{
    public static List<string> ChromiumCachesUnder(string root)
    {
        List<string> found = [];
        if (!Directory.Exists(root))
        {
            return found;
        }

        string[] names = ["Cache", "Code Cache", "GPUCache"];
        try
        {
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 6,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            foreach (string dir in Directory.EnumerateDirectories(root, "*", options))
            {
                if (!names.Contains(Path.GetFileName(dir)))
                {
                    continue;
                }

                if (found.Any(existing => dir.StartsWith(existing + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string parent = Path.GetDirectoryName(dir)!;
                bool chromiumLayout =
                    Directory.Exists(Path.Combine(parent, "Code Cache")) ||
                    Directory.Exists(Path.Combine(parent, "GPUCache")) ||
                    Directory.Exists(Path.Combine(parent, "Cache", "Cache_Data"));

                if (chromiumLayout)
                {
                    found.Add(dir);
                }
            }
        }
        catch
        {
        }

        return found;
    }
}
