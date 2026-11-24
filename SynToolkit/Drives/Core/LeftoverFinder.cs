// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.IO;
using System.IO.Enumeration;
using Microsoft.Win32;

#nullable enable

namespace SynToolkit.Drives.Core;

public static class LeftoverFinder
{
    public sealed record Leftover(string Path, long Size, DateTime LastChange);

    public static readonly TimeSpan MinIdle = TimeSpan.FromDays(90);
    public const long MinSize = 50L * 1024 * 1024;

    private static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Packages", "Temp", "Programs", "Publishers", "Comms", "ConnectedDevicesPlatform",
        "D3DSCache", "CrashDumps", "IsolatedStorage", "VirtualStore", "PlaceholderTileLogoFolder", "Package Cache",
        "Microsoft Help", "Microsoft_Corporation", "MicrosoftEdge", "History", "Application Data",
        "NVIDIA", "NVIDIA Corporation", "AMD", "ATI", "Intel", "Realtek",
        "npm", "npm-cache", "pip", "Yarn", "NuGet", "pnpm", "pnpm-cache", "node-gyp", "Python", "Java", "Sun", "Oracle",
        "Adobe", "Google", "Mozilla", "BraveSoftware", "Opera Software", "Vivaldi", "Chromium",
        "Steam", "Discord", "Spotify", "Slack", "Code", "Battle.net", "EpicGamesLauncher", "Electronic Arts", "Ubisoft Game Launcher",
        "Riot Games", "GOG.com", "Roomy", "Framecalm", "SquirrelTemp", "Downloaded Installations", "ElevatedDiagnostics"
    };

    private static string NormalizeName(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static HashSet<string> InstalledNames()
    {
        HashSet<string> names = [];
        void AddName(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            string normalized = NormalizeName(source);
            if (normalized.Length >= 3)
            {
                names.Add(normalized);
            }

            foreach (string part in source.Split([' ', '.', '_', '-', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                string token = NormalizeName(part);
                if (token.Length >= 4)
                {
                    names.Add(token);
                }
            }
        }

        foreach ((RegistryHive hive, RegistryView view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Default)
                 })
        {
            try
            {
                using RegistryKey? root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root is null)
                {
                    continue;
                }

                foreach (string keyName in root.GetSubKeyNames())
                {
                    using RegistryKey? key = root.OpenSubKey(keyName);
                    AddName(keyName);
                    AddName(key?.GetValue("DisplayName") as string);
                    AddName(key?.GetValue("Publisher") as string);
                    if (key?.GetValue("InstallLocation") is string location && location.Length > 3)
                    {
                        AddName(Path.GetFileName(location.TrimEnd('\\')));
                    }
                }
            }
            catch
            {
            }
        }

        try
        {
            foreach (string dir in Directory.EnumerateDirectories(Env.L("Packages")))
            {
                AddName(Path.GetFileName(dir).Split('_')[0]);
            }
        }
        catch
        {
        }

        foreach (string root in new[] { Env.ProgramFiles, Env.ProgramFilesX86, Env.L("Programs") })
        {
            try
            {
                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    AddName(Path.GetFileName(dir));
                }
            }
            catch
            {
            }
        }

        return names;
    }

    public static bool BelongsToInstalled(string folderName, IReadOnlySet<string> installed)
    {
        string normalized = NormalizeName(folderName);
        if (normalized.Length < 4)
        {
            return true;
        }

        foreach (string name in installed)
        {
            if ((name.Length >= 4 && normalized.Contains(name)) || name.Contains(normalized))
            {
                return true;
            }
        }

        return false;
    }

    private static (long Size, DateTime Newest) Measure(string dir, CancellationToken ct)
    {
        long size = 0;
        DateTime newest = Directory.GetLastWriteTimeUtc(dir);
        FileSystemEnumerable<(long, DateTime)> entries = new(
            dir,
            static (ref FileSystemEntry entry) => (entry.IsDirectory ? 0 : entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });

        foreach ((long len, DateTime modifiedUtc) in entries)
        {
            ct.ThrowIfCancellationRequested();
            size += len;
            if (modifiedUtc > newest)
            {
                newest = modifiedUtc;
            }
        }

        return (size, newest);
    }

    public static List<Leftover> Find(IEnumerable<string> roots, IReadOnlySet<string> installed, DateTime nowUtc, CancellationToken ct = default)
    {
        List<Leftover> found = [];
        foreach (string root in roots)
        {
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root).ToList();
            }
            catch
            {
                continue;
            }

            foreach (string dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name.StartsWith('{') || Never.Contains(name))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(dir) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0)
                    {
                        continue;
                    }

                    if (BelongsToInstalled(name, installed))
                    {
                        continue;
                    }

                    (long size, DateTime newestUtc) = Measure(dir, ct);
                    if (size < MinSize || nowUtc - newestUtc < MinIdle)
                    {
                        continue;
                    }

                    found.Add(new Leftover(dir, size, newestUtc.ToLocalTime()));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }
        }

        return found.OrderByDescending(item => item.Size).ToList();
    }

    public static IEnumerable<string> DefaultRoots()
    {
        yield return Env.Local;
        yield return Env.Roaming;
        yield return Path.Combine(Path.GetDirectoryName(Env.Local) ?? Env.Local, "LocalLow");
    }
}
