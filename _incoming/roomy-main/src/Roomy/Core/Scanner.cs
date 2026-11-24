using System.Diagnostics;
using Roomy.Localization;

namespace Roomy.Core;

public sealed class ScanContext
{
    public bool IsAdmin { get; init; }
    public CancellationToken Token { get; init; }

    readonly Lazy<List<FileInfo>> _downloads = new(LoadDownloads);
    public List<FileInfo> DownloadFiles => _downloads.Value;

    static List<FileInfo> LoadDownloads()
    {
        try
        {
            var d = new DirectoryInfo(Env.Downloads);
            if (!d.Exists) return [];
            return d.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            }).ToList();
        }
        catch { return []; }
    }
}

public sealed record ScanStep(string Id, string Title, Func<ScanContext, CleanupCategory?> Run);

/// <summary>Each step looks in a few well-known places and reports what it found. Nothing here deletes.</summary>
public static class Scanner
{
    static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    static readonly HashSet<string> InstallerExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle",
    };

    public static readonly IReadOnlyList<ScanStep> Steps =
    [
        new("recycle", S.CatRecycle, RecycleBin),
        new("temp", S.CatTemp, Temp),
        new("browser", S.CatBrowser, Browsers),
        new("apps", S.CatApps, Apps),
        new("shaders", S.StepShaders, Shaders),
        new("crash", S.CatCrash, Crashes),
        new("windows", S.StepWindows, WindowsLeftovers),
        new("installers", S.CatInstallers, Installers),
        new("downloads", S.StepDownloads, BigDownloads),
        new("games", S.StepGames, Games),
        new("winold", S.StepWinOld, WindowsOld),
        new("hibernation", S.StepHibernation, HibernationFile),
        new("leftovers", S.StepLeftovers, Leftovers),
    ];

    // ---------- helpers ----------

    static CleanupItem? Item(string name, TimeSpan? minAge, CancellationToken ct, IEnumerable<string> paths)
    {
        var existing = paths.Where(FileOps.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (existing.Count == 0) return null;
        long size = 0;
        int files = 0;
        foreach (var p in existing)
        {
            var (b, f) = FileOps.Measure(p, minAge, ct);
            size += b;
            files += f;
        }
        if (size <= 0) return null;
        return new CleanupItem
        {
            Name = name, Paths = existing, Size = size, FileCount = files, MinAge = minAge,
            Detail = $"{files:N0} {Loc.Files(files)}",
        };
    }

    static void Add(CleanupCategory c, CleanupItem? i)
    {
        if (i != null) c.Items.Add(i);
    }

    static CleanupCategory Sorted(CleanupCategory c)
    {
        c.Items.Sort((a, b) => b.Size.CompareTo(a.Size));
        return c;
    }

    static string[] Subdirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : []; }
        catch { return []; }
    }

    static List<string> Running(params (string Proc, string Name)[] apps)
    {
        var list = new List<string>();
        foreach (var (proc, name) in apps)
        {
            try
            {
                var ps = Process.GetProcessesByName(proc);
                if (ps.Length > 0 && !list.Contains(name)) list.Add(name);
                foreach (var p in ps) p.Dispose();
            }
            catch { }
        }
        return list;
    }

    static string? RunningNote(List<string> names)
    {
        if (names.Count == 0) return null;
        var who = names.Count == 1
            ? names[0]
            : string.Join(", ", names.Take(names.Count - 1)) + " " + S.AndWord + " " + names[^1];
        return Loc.Format("RunningNote", who);
    }

    static DateTime Touched(FileInfo f) => f.CreationTime > f.LastWriteTime ? f.CreationTime : f.LastWriteTime;

    // ---------- steps ----------

    static CleanupCategory RecycleBin(ScanContext c)
    {
        var cat = Catalog.Create("recycle");
        var (size, count) = Native.QueryRecycleBin();
        if (size > 0)
            cat.Items.Add(new CleanupItem
            {
                Name = S.CatRecycle, Paths = ["shell:RecycleBinFolder"], Size = size,
                Detail = $"{count:N0} {Loc.Items(count)}",
            });
        return cat;
    }

    static CleanupCategory Temp(ScanContext c)
    {
        var cat = Catalog.Create("temp");
        Add(cat, Item(S.ItemTempFolder, OneDay, c.Token, [Env.Temp]));
        return cat;
    }

    static readonly (string Name, string UserData)[] Chromium =
    [
        ("Google Chrome", Env.L("Google", "Chrome", "User Data")),
        ("Microsoft Edge", Env.L("Microsoft", "Edge", "User Data")),
        ("Brave", Env.L("BraveSoftware", "Brave-Browser", "User Data")),
        ("Vivaldi", Env.L("Vivaldi", "User Data")),
        ("Chromium", Env.L("Chromium", "User Data")),
    ];

    static IEnumerable<string> ChromiumCaches(string userData)
    {
        if (!Directory.Exists(userData)) yield break;
        yield return Path.Combine(userData, "ShaderCache");
        yield return Path.Combine(userData, "GrShaderCache");
        yield return Path.Combine(userData, "GraphiteDawnCache");
        foreach (var dir in Subdirs(userData))
        {
            var n = Path.GetFileName(dir);
            if (n == "Default" || n == "Guest Profile" || n.StartsWith("Profile ", StringComparison.Ordinal))
            {
                yield return Path.Combine(dir, "Cache");
                yield return Path.Combine(dir, "Code Cache");
                yield return Path.Combine(dir, "GPUCache");
            }
        }
    }

    static CleanupCategory Browsers(ScanContext c)
    {
        var cat = Catalog.Create("browser");
        foreach (var (name, ud) in Chromium) Add(cat, Item(name, null, c.Token, ChromiumCaches(ud)));
        Add(cat, Item(S.ItemFirefox, null, c.Token,
            Subdirs(Env.L("Mozilla", "Firefox", "Profiles")).Select(p => Path.Combine(p, "cache2"))));
        Add(cat, Item(S.ItemOpera, null, c.Token,
            [Env.L("Opera Software", "Opera Stable", "Cache"), Env.L("Opera Software", "Opera GX Stable", "Cache")]));
        if (cat.Items.Count > 0)
            cat.Note = RunningNote(Running(("chrome", "Chrome"), ("msedge", "Edge"), ("firefox", S.ItemFirefox),
                ("brave", "Brave"), ("opera", S.ItemOpera), ("vivaldi", "Vivaldi")));
        return Sorted(cat);
    }

    static CleanupCategory Apps(ScanContext c)
    {
        var cat = Catalog.Create("apps");
        string[] bits = ["Cache", "Code Cache", "GPUCache"];
        IEnumerable<string> Bits(string root) => bits.Select(b => Path.Combine(root, b));

        Add(cat, Item(S.ItemDiscord, null, c.Token,
            new[] { "discord", "discordptb", "discordcanary" }.SelectMany(d => Bits(Env.R(d)))));
        Add(cat, Item(S.ItemSlack, null, c.Token, Bits(Env.R("Slack"))));
        Add(cat, Item(S.ItemTeamsClassic, null, c.Token, Bits(Env.R("Microsoft", "Teams"))));
        // New Teams keeps its web cache in WebView2 profiles; settings and sign-in live elsewhere.
        Add(cat, Item("Microsoft Teams", null, c.Token,
            Subdirs(Env.L("Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView"))
                .Where(p => Path.GetFileName(p) is var n && (n == "Default" || n.StartsWith("WV2Profile", StringComparison.Ordinal)))
                .SelectMany(Bits)));
        // Spotify's streaming cache (Data) and its built-in browser cache. Offline downloads (Storage) are left alone.
        Add(cat, Item("Spotify", null, c.Token,
            Bits(Env.L("Spotify", "Browser"))
                .Append(Env.L("Spotify", "Data"))
                .Append(Env.L("Packages", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "LocalCache", "Spotify", "Data"))));
        Add(cat, Item("Epic Games Launcher", null, c.Token,
            Subdirs(Env.L("EpicGamesLauncher", "Saved"))
                .Where(p => Path.GetFileName(p).StartsWith("webcache", StringComparison.OrdinalIgnoreCase))));
        Add(cat, Item(S.ItemVsCode, null, c.Token,
            Bits(Env.R("Code")).Append(Env.R("Code", "CachedData")).Append(Env.R("Code", "CachedExtensionVSIXs"))));
        Add(cat, Item(S.ItemSteamBrowser, null, c.Token, [Env.L("Steam", "htmlcache")]));
        // Game launchers embed a Chromium browser; only its cache folders are cleaned, never logins or game data.
        Add(cat, Item("Battle.net", null, c.Token, CacheFinder.ChromiumCachesUnder(Env.L("Battle.net"))));
        Add(cat, Item("EA app", null, c.Token, CacheFinder.ChromiumCachesUnder(Env.L("Electronic Arts", "EA Desktop"))));
        Add(cat, Item("Ubisoft Connect", null, c.Token, CacheFinder.ChromiumCachesUnder(Env.L("Ubisoft Game Launcher"))));
        Add(cat, Item("Riot Client", null, c.Token, CacheFinder.ChromiumCachesUnder(Env.L("Riot Games", "Riot Client"))));
        Add(cat, Item("GOG Galaxy", null, c.Token, CacheFinder.ChromiumCachesUnder(Env.L("GOG.com", "Galaxy"))));
        Add(cat, Item(S.ItemNpm, null, c.Token, [Env.L("npm-cache", "_cacache")]));
        Add(cat, Item(S.ItemPip, null, c.Token, [Env.L("pip", "Cache")]));
        Add(cat, Item(S.ItemYarn, null, c.Token, [Env.L("Yarn", "Cache")]));
        Add(cat, Item(S.ItemNuget, null, c.Token, [Env.L("NuGet", "v3-cache")]));
        if (cat.Items.Count > 0)
            cat.Note = RunningNote(Running(("Discord", S.ItemDiscord), ("slack", S.ItemSlack), ("Code", S.ItemVsCode), ("Teams", "Teams"),
                ("ms-teams", "Teams"), ("Spotify", "Spotify"), ("EpicGamesLauncher", "Epic Games Launcher"),
                ("Battle.net", "Battle.net"), ("EADesktop", "EA app"), ("upc", "Ubisoft Connect"),
                ("RiotClientServices", "Riot Client"), ("GalaxyClient", "GOG Galaxy")));
        return Sorted(cat);
    }

    static CleanupCategory Shaders(ScanContext c)
    {
        var cat = Catalog.Create("shaders");
        Add(cat, Item(S.ItemDirectX, null, c.Token, [Env.L("D3DSCache")]));
        Add(cat, Item(S.ItemNvidia, null, c.Token,
            [Env.L("NVIDIA", "DXCache"), Env.L("NVIDIA", "GLCache"), Env.L("NVIDIA Corporation", "NV_Cache"), Env.PD("NVIDIA Corporation", "NV_Cache")]));
        Add(cat, Item("AMD", null, c.Token,
            [Env.L("AMD", "DxCache"), Env.L("AMD", "DxcCache"), Env.L("AMD", "GLCache"), Env.L("AMD", "VkCache")]));
        Add(cat, Item("Intel", null, c.Token, [Env.L("Intel", "ShaderCache")]));
        Add(cat, Item(S.ItemSteamGames, null, c.Token,
            Steam.Libraries().Select(l => Path.Combine(l, "steamapps", "shadercache"))));
        return Sorted(cat);
    }

    static CleanupCategory Crashes(ScanContext c)
    {
        var cat = Catalog.Create("crash");
        Add(cat, Item(S.ItemAppCrashDumps, null, c.Token, [Env.L("CrashDumps")]));
        Add(cat, Item(S.ItemWindowsErrorReports, null, c.Token, [Env.L("Microsoft", "Windows", "WER")]));
        return Sorted(cat);
    }

    static CleanupCategory WindowsLeftovers(ScanContext c)
    {
        var cat = Catalog.Create("windows");
        if (!c.IsAdmin)
        {
            cat.Blocked = true;
            cat.Note = S.WindowsLocksNote;
            return cat;
        }
        Add(cat, Item(S.ItemDownloadedUpdates, null, c.Token, [Env.W("SoftwareDistribution", "Download")]));
        Add(cat, Item(S.ItemDeliveryOptimization, null, c.Token,
            [Env.W("ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")]));
        Add(cat, Item(S.ItemWindowsTemp, OneDay, c.Token, [Env.W("Temp")]));
        Add(cat, Item(S.ItemSystemCrashDumps, null, c.Token,
            [Env.W("Minidump"), Env.W("MEMORY.DMP"), Env.PD("Microsoft", "Windows", "WER", "ReportArchive"), Env.PD("Microsoft", "Windows", "WER", "ReportQueue")]));
        return Sorted(cat);
    }

    static CleanupCategory Installers(ScanContext c)
    {
        var cat = Catalog.Create("installers");
        var cutoff = DateTime.Now.AddDays(-7);
        foreach (var f in c.DownloadFiles)
        {
            try
            {
                if (!InstallerExt.Contains(f.Extension)) continue;
                // Only loose files in Downloads itself. .exe files inside extracted folders are usually
                // portable apps, not installers.
                if (!string.Equals(f.DirectoryName?.TrimEnd('\\'), Env.Downloads.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                var t = Touched(f);
                if (t > cutoff) continue;
                cat.Items.Add(new CleanupItem
                {
                    Name = f.Name, Paths = [f.FullName], Size = f.Length, FileCount = 1, IsFile = true,
                    Detail = Loc.Format("DownloadedAgo", Format.Ago(t)),
                });
            }
            catch { }
        }
        return Sorted(cat);
    }

    static CleanupCategory BigDownloads(ScanContext c)
    {
        var cat = Catalog.Create("downloads");
        const long minSize = 250L * 1024 * 1024;
        var cutoff = DateTime.Now.AddDays(-30);
        foreach (var f in c.DownloadFiles)
        {
            try
            {
                if (f.Length < minSize || InstallerExt.Contains(f.Extension)) continue;
                var t = Touched(f);
                if (f.LastAccessTime > t) t = f.LastAccessTime;
                if (t > cutoff) continue;
                cat.Items.Add(new CleanupItem
                {
                    Name = f.Name, Paths = [f.FullName], Size = f.Length, FileCount = 1, IsFile = true,
                    Detail = Loc.Format("UntouchedFor", Format.Ago(t)),
                });
            }
            catch { }
        }
        return Sorted(cat);
    }

    static CleanupCategory Games(ScanContext c)
    {
        var cat = Catalog.Create("games");
        var cutoff = DateTime.Now.AddDays(-60);
        foreach (var g in Steam.Games())
        {
            if (g.Size < 1L << 30) continue;
            if (g.LastPlayed is { } lp && lp > cutoff) continue;
            cat.Items.Add(new CleanupItem
            {
                Name = g.Name, Paths = [g.InstallPath], Size = g.Size, AppId = g.AppId,
                Detail = g.LastPlayed is { } p ? Loc.Format("LastPlayed", Format.Ago(p)) : S.NeverPlayed,
            });
        }
        return Sorted(cat);
    }

    static CleanupCategory? Leftovers(ScanContext c)
    {
        var found = LeftoverFinder.Find(LeftoverFinder.DefaultRoots(), LeftoverFinder.InstalledNames(), DateTime.UtcNow, c.Token);
        if (found.Count == 0) return null;
        var cat = Catalog.Create("leftovers");
        foreach (var l in found)
            cat.Items.Add(new CleanupItem
            {
                Name = Path.GetFileName(l.Path), Paths = [l.Path], Size = l.Size, WholeFolder = true,
                Detail = Loc.Format("LeftoverDetail", Path.GetFileName(Path.GetDirectoryName(l.Path)!), Format.Ago(l.LastChange)),
            });
        return cat;
    }

    static CleanupCategory? HibernationFile(ScanContext c)
    {
        var size = Hibernation.FileSize();
        if (size <= 0) return null;
        var cat = Catalog.Create("hibernation");
        cat.Items.Add(new CleanupItem { Name = "hiberfil.sys", Paths = [Hibernation.FilePath], Size = size });
        return cat;
    }

    static CleanupCategory? WindowsOld(ScanContext c)
    {
        var p = Path.Combine(Env.SystemRoot, "Windows.old");
        if (!Directory.Exists(p)) return null;
        var (b, _) = FileOps.Measure(p, null, c.Token);
        var cat = Catalog.Create("winold");
        cat.Items.Add(new CleanupItem { Name = "Windows.old", Paths = [p], Size = b });
        return cat;
    }
}
