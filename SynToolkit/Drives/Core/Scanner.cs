// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using System.Diagnostics;
using System.IO;

#nullable enable

namespace SynToolkit.Drives.Core;

public sealed class ScanContext
{
    public bool IsAdmin { get; init; }
    public CancellationToken Token { get; init; }

    private readonly Lazy<List<FileInfo>> _downloads = new(LoadDownloads);
    public List<FileInfo> DownloadFiles => _downloads.Value;

    private static List<FileInfo> LoadDownloads()
    {
        try
        {
            DirectoryInfo downloads = new(Env.Downloads);
            if (!downloads.Exists)
            {
                return [];
            }

            return downloads.EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            }).ToList();
        }
        catch
        {
            return [];
        }
    }
}

public sealed record ScanStep(string Id, string Title, Func<ScanContext, CleanupCategory?> Run);

public static class Scanner
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    private static readonly HashSet<string> InstallerExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"
    };

    public static readonly IReadOnlyList<ScanStep> Steps =
    [
        new("recycle", DrivesText.Get("DrivesCleaner_CatRecycle", "Recycle Bin"), RecycleBin),
        new("temp", DrivesText.Get("DrivesCleaner_CatTemp", "Temporary files"), Temp),
        new("browser", DrivesText.Get("DrivesCleaner_CatBrowser", "Browser cache"), Browsers),
        new("apps", DrivesText.Get("DrivesCleaner_CatApps", "App caches"), Apps),
        new("shaders", DrivesText.Get("DrivesCleaner_StepShaders", "Shader cache"), Shaders),
        new("crash", DrivesText.Get("DrivesCleaner_CatCrash", "Crash reports"), Crashes),
        new("windows", DrivesText.Get("DrivesCleaner_StepWindows", "Windows Update"), WindowsLeftovers),
        new("installers", DrivesText.Get("DrivesCleaner_CatInstallers", "Old installers"), Installers),
        new("downloads", DrivesText.Get("DrivesCleaner_StepDownloads", "Big downloads"), BigDownloads),
        new("games", DrivesText.Get("DrivesCleaner_StepGames", "Unplayed games"), Games),
        new("winold", DrivesText.Get("DrivesCleaner_StepWinOld", "Old Windows"), WindowsOld),
        new("hibernation", DrivesText.Get("DrivesCleaner_StepHibernation", "Hibernation file"), HibernationFile),
        new("leftovers", DrivesText.Get("DrivesCleaner_StepLeftovers", "Uninstalled app leftovers"), Leftovers)
    ];

    private static CleanupItem? Item(string name, TimeSpan? minAge, CancellationToken ct, IEnumerable<string> paths)
    {
        List<string> existing = paths.Where(FileOps.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (existing.Count == 0)
        {
            return null;
        }

        long size = 0;
        int files = 0;
        foreach (string path in existing)
        {
            (long bytes, int count) = FileOps.Measure(path, minAge, ct);
            size += bytes;
            files += count;
        }

        if (size <= 0)
        {
            return null;
        }

        return new CleanupItem
        {
            Name = name,
            Paths = existing,
            Size = size,
            FileCount = files,
            MinAge = minAge,
            Detail = $"{files:N0} files"
        };
    }

    private static void Add(CleanupCategory category, CleanupItem? item)
    {
        if (item is not null)
        {
            category.Items.Add(item);
        }
    }

    private static CleanupCategory Sorted(CleanupCategory category)
    {
        category.Items.Sort((left, right) => right.Size.CompareTo(left.Size));
        return category;
    }

    private static string[] Subdirs(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
        }
        catch
        {
            return [];
        }
    }

    private static List<string> Running(params (string Process, string Name)[] apps)
    {
        List<string> list = [];
        foreach ((string process, string name) in apps)
        {
            try
            {
                Process[] found = Process.GetProcessesByName(process);
                if (found.Length > 0 && !list.Contains(name))
                {
                    list.Add(name);
                }

                foreach (Process proc in found)
                {
                    proc.Dispose();
                }
            }
            catch
            {
            }
        }

        return list;
    }

    private static string? RunningNote(List<string> names)
    {
        if (names.Count == 0)
        {
            return null;
        }

        string people = names.Count == 1
            ? names[0]
            : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

        return $"{people} is running now. Some files are locked - close it to clean everything.";
    }

    private static DateTime Touched(FileInfo file) => file.CreationTime > file.LastWriteTime ? file.CreationTime : file.LastWriteTime;

    private static CleanupCategory RecycleBin(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("recycle");
        (long size, long count) = Native.QueryRecycleBin();
        if (size > 0)
        {
            category.Items.Add(new CleanupItem
            {
                Name = DrivesText.Get("DrivesCleaner_CatRecycle", "Recycle Bin"),
                Paths = ["shell:RecycleBinFolder"],
                Size = size,
                Detail = $"{count:N0} items"
            });
        }

        return category;
    }

    private static CleanupCategory Temp(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("temp");
        Add(category, Item("Your temp folder", OneDay, context.Token, [Env.Temp]));
        return category;
    }

    private static readonly (string Name, string UserData)[] Chromium =
    [
        ("Google Chrome", Env.L("Google", "Chrome", "User Data")),
        ("Microsoft Edge", Env.L("Microsoft", "Edge", "User Data")),
        ("Brave", Env.L("BraveSoftware", "Brave-Browser", "User Data")),
        ("Vivaldi", Env.L("Vivaldi", "User Data")),
        ("Chromium", Env.L("Chromium", "User Data"))
    ];

    private static IEnumerable<string> ChromiumCaches(string userData)
    {
        if (!Directory.Exists(userData))
        {
            yield break;
        }

        yield return Path.Combine(userData, "ShaderCache");
        yield return Path.Combine(userData, "GrShaderCache");
        yield return Path.Combine(userData, "GraphiteDawnCache");
        foreach (string dir in Subdirs(userData))
        {
            string name = Path.GetFileName(dir);
            if (name == "Default" || name == "Guest Profile" || name.StartsWith("Profile ", StringComparison.Ordinal))
            {
                yield return Path.Combine(dir, "Cache");
                yield return Path.Combine(dir, "Code Cache");
                yield return Path.Combine(dir, "GPUCache");
            }
        }
    }

    private static CleanupCategory Browsers(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("browser");
        foreach ((string name, string userData) in Chromium)
        {
            Add(category, Item(name, null, context.Token, ChromiumCaches(userData)));
        }

        Add(category, Item("Firefox", null, context.Token, Subdirs(Env.L("Mozilla", "Firefox", "Profiles")).Select(path => Path.Combine(path, "cache2"))));
        Add(category, Item("Opera", null, context.Token, [Env.L("Opera Software", "Opera Stable", "Cache"), Env.L("Opera Software", "Opera GX Stable", "Cache")]));
        if (category.Items.Count > 0)
        {
            category.Note = RunningNote(Running(("chrome", "Chrome"), ("msedge", "Edge"), ("firefox", "Firefox"), ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi")));
        }

        return Sorted(category);
    }

    private static CleanupCategory Apps(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("apps");
        string[] bits = ["Cache", "Code Cache", "GPUCache"];
        IEnumerable<string> Bits(string root) => bits.Select(bit => Path.Combine(root, bit));

        Add(category, Item("Discord", null, context.Token, new[] { "discord", "discordptb", "discordcanary" }.SelectMany(item => Bits(Env.R(item)))));
        Add(category, Item("Slack", null, context.Token, Bits(Env.R("Slack"))));
        Add(category, Item("Microsoft Teams (classic)", null, context.Token, Bits(Env.R("Microsoft", "Teams"))));
        Add(category, Item("Microsoft Teams", null, context.Token,
            Subdirs(Env.L("Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView"))
                .Where(path => Path.GetFileName(path) is var name && (name == "Default" || name.StartsWith("WV2Profile", StringComparison.Ordinal)))
                .SelectMany(Bits)));
        Add(category, Item("Spotify", null, context.Token,
            Bits(Env.L("Spotify", "Browser"))
                .Append(Env.L("Spotify", "Data"))
                .Append(Env.L("Packages", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "LocalCache", "Spotify", "Data"))));
        Add(category, Item("Epic Games Launcher", null, context.Token,
            Subdirs(Env.L("EpicGamesLauncher", "Saved")).Where(path => Path.GetFileName(path).StartsWith("webcache", StringComparison.OrdinalIgnoreCase))));
        Add(category, Item("VS Code", null, context.Token,
            Bits(Env.R("Code")).Append(Env.R("Code", "CachedData")).Append(Env.R("Code", "CachedExtensionVSIXs"))));
        Add(category, Item("Steam's web browser", null, context.Token, [Env.L("Steam", "htmlcache")]));
        Add(category, Item("Battle.net", null, context.Token, CacheFinder.ChromiumCachesUnder(Env.L("Battle.net"))));
        Add(category, Item("EA app", null, context.Token, CacheFinder.ChromiumCachesUnder(Env.L("Electronic Arts", "EA Desktop"))));
        Add(category, Item("Ubisoft Connect", null, context.Token, CacheFinder.ChromiumCachesUnder(Env.L("Ubisoft Game Launcher"))));
        Add(category, Item("Riot Client", null, context.Token, CacheFinder.ChromiumCachesUnder(Env.L("Riot Games", "Riot Client"))));
        Add(category, Item("GOG Galaxy", null, context.Token, CacheFinder.ChromiumCachesUnder(Env.L("GOG.com", "Galaxy"))));
        Add(category, Item("npm cache", null, context.Token, [Env.L("npm-cache", "_cacache")]));
        Add(category, Item("pip cache", null, context.Token, [Env.L("pip", "Cache")]));
        Add(category, Item("Yarn cache", null, context.Token, [Env.L("Yarn", "Cache")]));
        Add(category, Item("NuGet web cache", null, context.Token, [Env.L("NuGet", "v3-cache")]));

        if (category.Items.Count > 0)
        {
            category.Note = RunningNote(Running(("Discord", "Discord"), ("slack", "Slack"), ("Code", "VS Code"), ("Teams", "Teams"),
                ("ms-teams", "Teams"), ("Spotify", "Spotify"), ("EpicGamesLauncher", "Epic Games Launcher"),
                ("Battle.net", "Battle.net"), ("EADesktop", "EA app"), ("upc", "Ubisoft Connect"),
                ("RiotClientServices", "Riot Client"), ("GalaxyClient", "GOG Galaxy")));
        }

        return Sorted(category);
    }

    private static CleanupCategory Shaders(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("shaders");
        Add(category, Item("DirectX", null, context.Token, [Env.L("D3DSCache")]));
        Add(category, Item("NVIDIA", null, context.Token, [Env.L("NVIDIA", "DXCache"), Env.L("NVIDIA", "GLCache"), Env.L("NVIDIA Corporation", "NV_Cache"), Env.PD("NVIDIA Corporation", "NV_Cache")]));
        Add(category, Item("AMD", null, context.Token, [Env.L("AMD", "DxCache"), Env.L("AMD", "DxcCache"), Env.L("AMD", "GLCache"), Env.L("AMD", "VkCache")]));
        Add(category, Item("Intel", null, context.Token, [Env.L("Intel", "ShaderCache")]));
        Add(category, Item("Steam games", null, context.Token, Steam.Libraries().Select(lib => Path.Combine(lib, "steamapps", "shadercache"))));
        return Sorted(category);
    }

    private static CleanupCategory Crashes(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("crash");
        Add(category, Item("App crash dumps", null, context.Token, [Env.L("CrashDumps")]));
        Add(category, Item("Windows error reports", null, context.Token, [Env.L("Microsoft", "Windows", "WER")]));
        return Sorted(category);
    }

    private static CleanupCategory WindowsLeftovers(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("windows");
        if (!context.IsAdmin)
        {
            category.Blocked = true;
            category.Note = DrivesText.Get("DrivesCleaner_WindowsLocksNote", "Windows locks these. Restart SynToolkit as administrator to check them.");
            return category;
        }

        Add(category, Item("Downloaded updates", null, context.Token, [Env.W("SoftwareDistribution", "Download")]));
        Add(category, Item("Delivery Optimization", null, context.Token, [Env.W("ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")]));
        Add(category, Item("Windows temp folder", OneDay, context.Token, [Env.W("Temp")]));
        Add(category, Item("System crash dumps", null, context.Token, [Env.W("Minidump"), Env.W("MEMORY.DMP"), Env.PD("Microsoft", "Windows", "WER", "ReportArchive"), Env.PD("Microsoft", "Windows", "WER", "ReportQueue")]));
        return Sorted(category);
    }

    private static CleanupCategory Installers(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("installers");
        DateTime cutoff = DateTime.Now.AddDays(-7);
        foreach (FileInfo file in context.DownloadFiles)
        {
            try
            {
                if (!InstallerExt.Contains(file.Extension))
                {
                    continue;
                }

                if (!string.Equals(file.DirectoryName?.TrimEnd('\\'), Env.Downloads.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTime touched = Touched(file);
                if (touched > cutoff)
                {
                    continue;
                }

                category.Items.Add(new CleanupItem
                {
                    Name = file.Name,
                    Paths = [file.FullName],
                    Size = file.Length,
                    FileCount = 1,
                    IsFile = true,
                    Detail = $"Downloaded {Format.Ago(touched)}"
                });
            }
            catch
            {
            }
        }

        return Sorted(category);
    }

    private static CleanupCategory BigDownloads(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("downloads");
        const long minSize = 250L * 1024 * 1024;
        DateTime cutoff = DateTime.Now.AddDays(-30);
        foreach (FileInfo file in context.DownloadFiles)
        {
            try
            {
                if (file.Length < minSize || InstallerExt.Contains(file.Extension))
                {
                    continue;
                }

                DateTime touched = Touched(file);
                if (file.LastAccessTime > touched)
                {
                    touched = file.LastAccessTime;
                }

                if (touched > cutoff)
                {
                    continue;
                }

                category.Items.Add(new CleanupItem
                {
                    Name = file.Name,
                    Paths = [file.FullName],
                    Size = file.Length,
                    FileCount = 1,
                    IsFile = true,
                    Detail = $"Untouched for {Format.Ago(touched)}"
                });
            }
            catch
            {
            }
        }

        return Sorted(category);
    }

    private static CleanupCategory Games(ScanContext context)
    {
        CleanupCategory category = Catalog.Create("games");
        DateTime cutoff = DateTime.Now.AddDays(-60);
        foreach (SteamGame game in Steam.Games())
        {
            if (game.Size < 1L << 30)
            {
                continue;
            }

            if (game.LastPlayed is { } playedAt && playedAt > cutoff)
            {
                continue;
            }

            category.Items.Add(new CleanupItem
            {
                Name = game.Name,
                Paths = [game.InstallPath],
                Size = game.Size,
                AppId = game.AppId,
                Detail = game.LastPlayed is { } time ? $"Last played {Format.Ago(time)}" : "Never played"
            });
        }

        return Sorted(category);
    }

    private static CleanupCategory? Leftovers(ScanContext context)
    {
        List<LeftoverFinder.Leftover> found = LeftoverFinder.Find(LeftoverFinder.DefaultRoots(), LeftoverFinder.InstalledNames(), DateTime.UtcNow, context.Token);
        if (found.Count == 0)
        {
            return null;
        }

        CleanupCategory category = Catalog.Create("leftovers");
        foreach (LeftoverFinder.Leftover leftover in found)
        {
            category.Items.Add(new CleanupItem
            {
                Name = Path.GetFileName(leftover.Path),
                Paths = [leftover.Path],
                Size = leftover.Size,
                WholeFolder = true,
                Detail = $"In {Path.GetFileName(Path.GetDirectoryName(leftover.Path)!)} · untouched for {Format.Ago(leftover.LastChange)}"
            });
        }

        return category;
    }

    private static CleanupCategory? HibernationFile(ScanContext context)
    {
        long size = Hibernation.FileSize();
        if (size <= 0)
        {
            return null;
        }

        CleanupCategory category = Catalog.Create("hibernation");
        category.Items.Add(new CleanupItem { Name = "hiberfil.sys", Paths = [Hibernation.FilePath], Size = size });
        return category;
    }

    private static CleanupCategory? WindowsOld(ScanContext context)
    {
        string path = Path.Combine(Env.SystemRoot, "Windows.old");
        if (!Directory.Exists(path))
        {
            return null;
        }

        (long bytes, _) = FileOps.Measure(path, null, context.Token);
        CleanupCategory category = Catalog.Create("winold");
        category.Items.Add(new CleanupItem { Name = "Windows.old", Paths = [path], Size = bytes });
        return category;
    }
}
