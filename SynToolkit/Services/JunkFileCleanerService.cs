#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SynToolkit.Models;
using SynToolkit.Utils;

namespace SynToolkit.Services
{
    /// <summary>
    /// Scans and clears well-known junk locations (temp files, browser caches, Windows Update's
    /// download cache, the Recycle Bin, old log files, the Explorer thumbnail cache). Never
    /// touches arbitrary user files or other users' profiles — only paths Windows and browsers
    /// themselves treat as disposable caches.
    /// </summary>
    public static class JunkFileCleanerService
    {
        // MinAge keeps a sweep away from files something is still writing: installers unpack
        // into %TEMP%, and SynToolkit's own interactive-user helper stages its .cmd and .log
        // there while a queue item runs. Deleting those mid-run fails the install.
        private sealed record JunkLocation(
            string Directory,
            string SearchPattern,
            SearchOption SearchOption,
            TimeSpan MinAge = default);

        public static List<JunkCategoryScanResult> Scan()
        {
            List<JunkCategoryScanResult> results = new();

            foreach (JunkCategory category in Enum.GetValues<JunkCategory>())
            {
                (ulong size, int count) = category == JunkCategory.RecycleBin
                    ? ScanRecycleBin()
                    : SumLocations(GetLocationsFor(category));

                results.Add(new JunkCategoryScanResult(category, GetDisplayName(category), size, count));
            }

            return results;
        }

        public static ulong Clean(IEnumerable<JunkCategory> categories)
        {
            ulong totalFreed = 0;

            foreach (JunkCategory category in categories)
            {
                totalFreed += category == JunkCategory.RecycleBin
                    ? CleanRecycleBin()
                    : CleanLocations(GetLocationsFor(category));
            }

            return totalFreed;
        }

        private static string GetDisplayName(JunkCategory category) => category switch
        {
            JunkCategory.TempFiles => "Temporary files",
            JunkCategory.BrowserCaches => "Browser caches",
            JunkCategory.WindowsUpdateCache => "Windows Update cache",
            JunkCategory.RecycleBin => "Recycle Bin",
            JunkCategory.LogFiles => "Old log files",
            JunkCategory.ThumbnailCache => "Thumbnail cache",
            _ => category.ToString(),
        };

        private static IReadOnlyList<JunkLocation> GetLocationsFor(JunkCategory category)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            return category switch
            {
                // Ten minutes is longer than any single installer step waits on a temp file and
                // shorter than what a user would call "leftover junk".
                JunkCategory.TempFiles => new[]
                {
                    new JunkLocation(Path.GetTempPath(), "*", SearchOption.AllDirectories, TimeSpan.FromMinutes(10)),
                    new JunkLocation(Path.Combine(windowsDirectory, "Temp"), "*", SearchOption.AllDirectories, TimeSpan.FromMinutes(10)),
                },
                JunkCategory.BrowserCaches => new[]
                {
                    new JunkLocation(Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"), "*", SearchOption.AllDirectories),
                    new JunkLocation(Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache"), "*", SearchOption.AllDirectories),
                    new JunkLocation(Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"), "*", SearchOption.AllDirectories),
                    new JunkLocation(Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache"), "*", SearchOption.AllDirectories),
                }.Concat(GetFirefoxCacheLocations()).ToList(),
                JunkCategory.WindowsUpdateCache => new[]
                {
                    new JunkLocation(Path.Combine(windowsDirectory, @"SoftwareDistribution\Download"), "*", SearchOption.AllDirectories),
                },
                JunkCategory.LogFiles => new[]
                {
                    new JunkLocation(Path.Combine(windowsDirectory, "Logs"), "*", SearchOption.AllDirectories),
                    new JunkLocation(Path.Combine(localAppData, @"Microsoft\Windows\WER"), "*", SearchOption.AllDirectories),
                },
                // Deliberately narrow: the Explorer folder also holds real config (e.g.
                // RecommendationsFilterList.json) and startup ETL logs alongside the caches —
                // confirmed by inspecting a real profile's folder contents, not assumed. Only
                // the actual thumbcache_*/iconcache_* database files match "*cache_*.db", and
                // TopDirectoryOnly avoids reaching into the sibling NotifyIcon folder.
                JunkCategory.ThumbnailCache => new[]
                {
                    new JunkLocation(Path.Combine(localAppData, @"Microsoft\Windows\Explorer"), "*cache_*.db", SearchOption.TopDirectoryOnly),
                },
                _ => Array.Empty<JunkLocation>(),
            };
        }

        // Firefox splits profile storage into two roots: %APPDATA%\Mozilla\Firefox\Profiles
        // (bookmarks, saved logins, history — never touched here) and, separately,
        // %LOCALAPPDATA%\Mozilla\Firefox\Profiles (the "local" mirror Firefox itself treats as
        // disposable, holding cache2/startupCache/shader-cache). Only descending into folders
        // literally named "cache2" — never sweeping the profile root itself — keeps this safe
        // even if a future Firefox version stores something unexpected alongside it.
        private static IEnumerable<JunkLocation> GetFirefoxCacheLocations()
        {
            string localFirefoxProfiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Mozilla\Firefox\Profiles");

            if (!Directory.Exists(localFirefoxProfiles))
            {
                yield break;
            }

            IEnumerable<string> cacheDirectories;
            try
            {
                cacheDirectories = Directory.EnumerateDirectories(localFirefoxProfiles, "cache2", SearchOption.AllDirectories);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (string cacheDirectory in cacheDirectories)
            {
                yield return new JunkLocation(cacheDirectory, "*", SearchOption.AllDirectories);
            }
        }

        private static (ulong Size, int Count) SumLocations(IReadOnlyList<JunkLocation> locations)
        {
            ulong totalSize = 0;
            int totalCount = 0;

            foreach (JunkLocation location in locations)
            {
                if (!Directory.Exists(location.Directory))
                {
                    continue;
                }

                DateTime cutoffUtc = DateTime.UtcNow - location.MinAge;

                foreach (string file in EnumerateFilesSafely(location))
                {
                    try
                    {
                        FileInfo info = new(file);
                        if (info.LastWriteTimeUtc > cutoffUtc)
                        {
                            continue;
                        }

                        totalSize += (ulong)info.Length;
                        totalCount++;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Locked or inaccessible file — skip it, it will simply not be counted.
                    }
                }
            }

            return (totalSize, totalCount);
        }

        private static ulong CleanLocations(IReadOnlyList<JunkLocation> locations)
        {
            ulong freedBytes = 0;

            foreach (JunkLocation location in locations)
            {
                if (!Directory.Exists(location.Directory))
                {
                    continue;
                }

                DateTime cutoffUtc = DateTime.UtcNow - location.MinAge;

                foreach (string file in EnumerateFilesSafely(location))
                {
                    try
                    {
                        FileInfo info = new(file);
                        if (info.LastWriteTimeUtc > cutoffUtc)
                        {
                            continue;
                        }

                        long length = info.Length;
                        File.Delete(file);
                        freedBytes += (ulong)length;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // In use or protected — leave it and continue with the rest.
                    }
                }

                if (location.SearchOption == SearchOption.AllDirectories)
                {
                    RemoveEmptySubdirectories(location.Directory);
                }
            }

            return freedBytes;
        }

        private static IEnumerable<string> EnumerateFilesSafely(JunkLocation location)
        {
            try
            {
                return Directory.EnumerateFiles(location.Directory, location.SearchPattern, location.SearchOption);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Enumerable.Empty<string>();
            }
        }

        private static void RemoveEmptySubdirectories(string rootDirectory)
        {
            try
            {
                foreach (string subDirectory in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
                    {
                        Directory.Delete(subDirectory);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort tidy-up only — leftover empty folders are harmless.
            }
        }

        private static (ulong Size, int Count) ScanRecycleBin()
        {
            try
            {
                // Shell.Application's per-item Size property returns a corrupt (negative,
                // overflowed) value for some Recycle Bin entries on real machines — confirmed by
                // testing against an actual 207-item Recycle Bin, where 2 items reported a
                // negative Size and summing them naively (even with explicit int64 casts)
                // produced a negative total for the whole bin. Skipping just those bad entries
                // and summing the rest gives an accurate lower-bound size instead of silently
                // reporting the entire bin as empty.
                CommandResult result = CommandPromptHelper.RunProcessResult(
                    "powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-Command",
                        "$items = (New-Object -ComObject Shell.Application).Namespace(10).Items(); " +
                        "$sum = [int64]0; " +
                        "foreach ($item in $items) { $s = [int64]$item.Size; if ($s -ge 0) { $sum += $s } }; " +
                        "'{0}|{1}' -f $sum, $items.Count"],
                    timeoutMilliseconds: 30_000);

                if (result.Succeeded)
                {
                    string[] parts = result.StandardOutput.Trim().Split('|');
                    if (parts.Length == 2
                        && long.TryParse(parts[0], out long sizeBytes)
                        && sizeBytes >= 0
                        && int.TryParse(parts[1], out int count))
                    {
                        return ((ulong)sizeBytes, count);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Cleaner] Unable to read the Recycle Bin size.");
            }

            return (0, 0);
        }

        private static ulong CleanRecycleBin()
        {
            (ulong sizeBeforeClean, _) = ScanRecycleBin();

            CommandResult result = CommandPromptHelper.RunProcessResult(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", "Clear-RecycleBin -Force -ErrorAction SilentlyContinue"],
                timeoutMilliseconds: 60_000);

            if (!result.Succeeded)
            {
                App.logger.Warn("[Cleaner] Clear-RecycleBin reported a non-zero exit code: {Output}", result.CombinedOutput);
            }

            return sizeBeforeClean;
        }
    }
}
