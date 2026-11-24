using SynToolkit.Drives.Core;

namespace SynToolkit.SystemInformationTests;

internal static class DrivesCoreTests
{
    public static void CacheFinderDetectsChromiumLayout()
    {
        string root = Path.Combine(Path.GetTempPath(), "SynToolkit-DriveTests-Cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string profile = Path.Combine(root, "Default");
            Directory.CreateDirectory(Path.Combine(profile, "Cache"));
            Directory.CreateDirectory(Path.Combine(profile, "Code Cache"));
            Directory.CreateDirectory(Path.Combine(profile, "GPUCache"));

            List<string> found = CacheFinder.ChromiumCachesUnder(root);
            bool hasCache = found.Any(path => path.EndsWith(Path.Combine("Default", "Cache"), StringComparison.OrdinalIgnoreCase));
            bool hasCodeCache = found.Any(path => path.EndsWith(Path.Combine("Default", "Code Cache"), StringComparison.OrdinalIgnoreCase));
            bool hasGpuCache = found.Any(path => path.EndsWith(Path.Combine("Default", "GPUCache"), StringComparison.OrdinalIgnoreCase));

            if (!hasCache || !hasCodeCache || !hasGpuCache)
            {
                throw new InvalidOperationException("CacheFinder must identify Chromium cache folders.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static void LeftoverFinderSkipsRecentOrSmallFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "SynToolkit-DriveTests-Leftovers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string oldLarge = Path.Combine(root, "SomeOldApp");
            Directory.CreateDirectory(oldLarge);
            string oldLargeFile = Path.Combine(oldLarge, "blob.bin");
            File.WriteAllBytes(oldLargeFile, new byte[60 * 1024 * 1024]);
            File.SetLastWriteTimeUtc(oldLargeFile, DateTime.UtcNow.AddDays(-200));
            Directory.SetLastWriteTimeUtc(oldLarge, DateTime.UtcNow.AddDays(-200));

            string recentLarge = Path.Combine(root, "RecentApp");
            Directory.CreateDirectory(recentLarge);
            File.WriteAllBytes(Path.Combine(recentLarge, "blob.bin"), new byte[60 * 1024 * 1024]);

            string oldSmall = Path.Combine(root, "TinyOldApp");
            Directory.CreateDirectory(oldSmall);
            string oldSmallFile = Path.Combine(oldSmall, "blob.bin");
            File.WriteAllBytes(oldSmallFile, new byte[5 * 1024 * 1024]);
            File.SetLastWriteTimeUtc(oldSmallFile, DateTime.UtcNow.AddDays(-200));
            Directory.SetLastWriteTimeUtc(oldSmall, DateTime.UtcNow.AddDays(-200));

            List<LeftoverFinder.Leftover> leftovers = LeftoverFinder.Find(
                [root],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                DateTime.UtcNow);

            if (!leftovers.Any(item => item.Path.Equals(oldLarge, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Old large folder should be detected as leftover.");
            }

            if (leftovers.Any(item => item.Path.Equals(recentLarge, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Recent folders must be excluded.");
            }

            if (leftovers.Any(item => item.Path.Equals(oldSmall, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Small folders must be excluded.");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
