using System.IO.Enumeration;
using Microsoft.Win32;

namespace Roomy.Core;

/// <summary>
/// Finds folders in AppData that most likely belong to apps you've uninstalled.
/// It is deliberately cautious: a folder is only suggested when its name matches nothing that is
/// installed, it isn't a known Windows or tool folder, nothing in it changed for months, and it's big
/// enough to matter. Results are always "your call" and never ticked by default.
/// </summary>
public static class LeftoverFinder
{
    public sealed record Leftover(string Path, long Size, DateTime LastChange);

    public static readonly TimeSpan MinIdle = TimeSpan.FromDays(90);
    public const long MinSize = 50L * 1024 * 1024;

    /// <summary>Folder names that are part of Windows, drivers or tools, or that Roomy cleans elsewhere.</summary>
    static readonly HashSet<string> Never = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Packages", "Temp", "Programs", "Publishers", "Comms", "ConnectedDevicesPlatform",
        "D3DSCache", "CrashDumps", "IsolatedStorage", "VirtualStore", "PlaceholderTileLogoFolder", "Package Cache",
        "Microsoft Help", "Microsoft_Corporation", "MicrosoftEdge", "History", "Application Data",
        "NVIDIA", "NVIDIA Corporation", "AMD", "ATI", "Intel", "Realtek",
        "npm", "npm-cache", "pip", "Yarn", "NuGet", "pnpm", "pnpm-cache", "node-gyp", "Python", "Java", "Sun", "Oracle",
        "Adobe", "Google", "Mozilla", "BraveSoftware", "Opera Software", "Vivaldi", "Chromium",
        "Steam", "Discord", "Spotify", "Slack", "Code", "Battle.net", "EpicGamesLauncher", "Electronic Arts", "Ubisoft Game Launcher",
        "Riot Games", "GOG.com", "Roomy", "Framecalm", "SquirrelTemp", "Downloaded Installations", "ElevatedDiagnostics",
    };

    static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Everything Windows knows is installed, as normalized names to match folders against.</summary>
    public static HashSet<string> InstalledNames()
    {
        var names = new HashSet<string>();
        void AddName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var n = Norm(s);
            if (n.Length >= 3) names.Add(n);
            foreach (var word in s.Split([' ', '.', '_', '-', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries))
                if (Norm(word) is { Length: >= 4 } w) names.Add(w);
        }

        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (root is null) continue;
                foreach (var keyName in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(keyName);
                    AddName(keyName);
                    AddName(k?.GetValue("DisplayName") as string);
                    AddName(k?.GetValue("Publisher") as string);
                    if (k?.GetValue("InstallLocation") is string loc && loc.Length > 3) AddName(Path.GetFileName(loc.TrimEnd('\\')));
                }
            }
            catch { }
        }

        // Store apps: their package folders are named like Publisher.AppName_hash.
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Env.L("Packages")))
                AddName(Path.GetFileName(dir).Split('_')[0]);
        }
        catch { }

        // Anything installed straight into Program Files counts as installed too.
        foreach (var pf in new[] { Env.ProgramFiles, Env.ProgramFilesX86, Env.L("Programs") })
        {
            try { foreach (var dir in Directory.EnumerateDirectories(pf)) AddName(Path.GetFileName(dir)); }
            catch { }
        }
        return names;
    }

    /// <summary>True when the folder name plausibly belongs to something installed.</summary>
    public static bool BelongsToInstalled(string folderName, IReadOnlySet<string> installed)
    {
        var n = Norm(folderName);
        if (n.Length < 4) return true; // too short to judge, so never suggest it
        foreach (var name in installed)
            if ((name.Length >= 4 && n.Contains(name)) || name.Contains(n)) return true;
        return false;
    }

    /// <summary>Total size and newest change time of a folder, without following links.</summary>
    static (long Size, DateTime Newest) Measure(string dir, CancellationToken ct)
    {
        long size = 0;
        var newest = Directory.GetLastWriteTimeUtc(dir);
        var e = new FileSystemEnumerable<(long, DateTime)>(dir, (ref FileSystemEntry x) => (x.IsDirectory ? 0 : x.Length, x.LastWriteTimeUtc.UtcDateTime),
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint });
        foreach (var (len, mod) in e)
        {
            ct.ThrowIfCancellationRequested();
            size += len;
            if (mod > newest) newest = mod;
        }
        return (size, newest);
    }

    public static List<Leftover> Find(IEnumerable<string> roots, IReadOnlySet<string> installed, DateTime nowUtc, CancellationToken ct = default)
    {
        var found = new List<Leftover>();
        foreach (var root in roots)
        {
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(root).ToList(); }
            catch { continue; }
            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                if (name.StartsWith('.') || name.StartsWith('{') || Never.Contains(name)) continue;
                try
                {
                    if ((File.GetAttributes(dir) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                    if (BelongsToInstalled(name, installed)) continue;
                    var (size, newest) = Measure(dir, ct);
                    if (size < MinSize || nowUtc - newest < MinIdle) continue;
                    found.Add(new(dir, size, newest.ToLocalTime()));
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
        }
        return found.OrderByDescending(l => l.Size).ToList();
    }

    public static IEnumerable<string> DefaultRoots()
    {
        yield return Env.Local;
        yield return Env.Roaming;
        yield return Path.Combine(Path.GetDirectoryName(Env.Local) ?? Env.Local, "LocalLow");
    }
}
