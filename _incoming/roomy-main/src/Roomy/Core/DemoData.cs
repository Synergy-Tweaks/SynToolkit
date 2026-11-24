namespace Roomy.Core;

/// <summary>
/// Made-up but realistic data for `--demo` and `--shots`, so screenshots and videos
/// never show anyone's real files.
/// </summary>
public static class DemoData
{
    const long MB = 1024L * 1024, GB = MB * 1024;
    static long Gb(double v) => (long)(v * GB);
    static long Mb(double v) => (long)(v * MB);

    public static readonly (string Root, string Label, long Total, long Free)[] Drives =
    [
        (@"C:\", "", Gb(237.9), Gb(9.6)),
        (@"D:\", "Games", Gb(931.5), Gb(212.4)),
    ];

    static CleanupItem I(string name, long size, string detail, bool file = false) => new()
    {
        Name = name, Paths = [$@"C:\Users\you\demo\{name}"], Size = size, Detail = detail, IsFile = file,
    };

    public static CleanupCategory? Category(string id)
    {
        var c = Catalog.Create(id);
        void A(string name, long size, string detail, bool file = false) => c.Items.Add(I(name, size, detail, file));
        switch (id)
        {
            case "recycle":
                A("Recycle Bin", Gb(3.21), "214 items");
                break;
            case "temp":
                A("Your temp folder", Gb(4.62), "18,402 files");
                break;
            case "browser":
                A("Google Chrome", Gb(2.08), "9,311 files");
                A("Microsoft Edge", Mb(712), "3,904 files");
                A("Firefox", Mb(388), "2,117 files");
                break;
            case "apps":
                A("npm cache", Gb(2.26), "41,870 files");
                A("Discord", Gb(1.38), "6,220 files");
                A("VS Code", Mb(864), "2,951 files");
                A("Steam's web browser", Mb(412), "1,480 files");
                A("Slack", Mb(296), "1,022 files");
                break;
            case "shaders":
                A("Steam games", Gb(3.62), "12,094 files");
                A("NVIDIA", Gb(1.91), "4,388 files");
                A("DirectX", Mb(742), "1,203 files");
                break;
            case "crash":
                A("App crash dumps", Gb(1.12), "37 files");
                A("Windows error reports", Mb(286), "412 files");
                break;
            case "windows":
                A("Downloaded updates", Gb(6.31), "1,904 files");
                A("Delivery Optimization", Gb(1.82), "96 files");
                A("Windows temp folder", Mb(524), "3,311 files");
                break;
            case "installers":
                A("Blender-4.3-windows-x64.msi", Mb(341), "Downloaded 4 months ago", true);
                A("OBS-Studio-31.0-Windows-Installer.exe", Mb(152), "Downloaded 3 months ago", true);
                A("NVIDIA_App_v11.0.exe", Mb(148), "Downloaded 5 weeks ago", true);
                A("UnityHubSetup.exe", Mb(127), "Downloaded 7 months ago", true);
                A("DiscordSetup.exe", Mb(112), "Downloaded a year ago", true);
                A("VSCodeUserSetup-x64.exe", Mb(98), "Downloaded 2 months ago", true);
                A("python-3.13.1-amd64.exe", Mb(27), "Downloaded 6 months ago", true);
                A("node-v22.11.0-x64.msi", Mb(29), "Downloaded 4 months ago", true);
                A("SteamSetup.exe", Mb(2.3), "Downloaded a year ago", true);
                break;
            case "downloads":
                A("Windows11_InsiderPreview.iso", Gb(6.12), "Untouched for 8 months", true);
                A("Recording 2026-03-14.mkv", Gb(3.24), "Untouched for 7 months", true);
                A("final-edit-v3-export.mp4", Gb(2.41), "Untouched for 2 months", true);
                A("dataset-full.zip", Gb(1.83), "Untouched for 5 months", true);
                break;
            case "games":
                c.Items.Add(new CleanupItem { Name = "Iron Frontier", Paths = [@"D:\Steam\steamapps\common\Iron Frontier"], Size = Gb(118.6), AppId = "0", Detail = "Last played a year ago" });
                c.Items.Add(new CleanupItem { Name = "Neon Drift", Paths = [@"D:\Steam\steamapps\common\Neon Drift"], Size = Gb(71.2), AppId = "0", Detail = "Last played 7 months ago" });
                c.Items.Add(new CleanupItem { Name = "Starfall Odyssey", Paths = [@"D:\Steam\steamapps\common\Starfall Odyssey"], Size = Gb(43.9), AppId = "0", Detail = "Last played 4 months ago" });
                c.Items.Add(new CleanupItem { Name = "Pixel Kingdoms", Paths = [@"D:\Steam\steamapps\common\Pixel Kingdoms"], Size = Gb(3.4), AppId = "0", Detail = "Never played" });
                break;
            case "winold":
                A("Windows.old", Gb(21.4), "");
                break;
            case "leftovers":
                c.Items.Add(new CleanupItem { Name = "OldPhotoEditor", Paths = [@"C:\Users\you\AppData\Roaming\OldPhotoEditor"], Size = Gb(1.4), WholeFolder = true, Detail = "In Roaming · untouched for 8 months" });
                c.Items.Add(new CleanupItem { Name = "TrialVideoTool", Paths = [@"C:\Users\you\AppData\Local\TrialVideoTool"], Size = Mb(612), WholeFolder = true, Detail = "In Local · untouched for a year" });
                c.Items.Add(new CleanupItem { Name = "GameLauncherX", Paths = [@"C:\Users\you\AppData\Local\GameLauncherX"], Size = Mb(208), WholeFolder = true, Detail = "In Local · untouched for 5 months" });
                break;
            case "hibernation":
                A("hiberfil.sys", Gb(12.7), "");
                break;
        }
        return c;
    }

    // ---------- space map ----------

    sealed record N(string Name, double Gb, params N[] Kids);

    static readonly N Tree = new("C:", 0,
        new N("Users", 88.6,
            new N("you", 88.1,
                new N("AppData", 37.9,
                    new N("Local", 29.0, new N("Temp", 4.6), new N("npm-cache", 2.3), new N("Google", 3.1), new N("Packages", 6.4), new N("Microsoft", 5.2), new N("Docker", 6.8)),
                    new N("Roaming", 8.2, new N("discord", 1.6), new N("Code", 1.9), new N("Spotify", 3.1)),
                    new N("LocalLow", 0.7)),
                new N("Downloads", 22.7),
                new N("Videos", 14.2),
                new N("Documents", 6.3),
                new N("Pictures", 4.1),
                new N("Desktop", 1.9),
                new N("OneDrive", 0.8)),
            new N("Public", 0.5)),
        new N("Program Files (x86)", 52.4,
            new N("Steam", 47.8),
            new N("Microsoft", 3.1),
            new N("Epic Games", 1.2)),
        new N("Windows", 28.9,
            new N("WinSxS", 9.6), new N("SoftwareDistribution", 6.4), new N("System32", 6.0), new N("Installer", 3.1), new N("SysWOW64", 1.6)),
        new N("Windows.old", 21.4),
        new N("Program Files", 15.2,
            new N("Adobe", 5.4), new N("NVIDIA Corporation", 2.6), new N("Microsoft Office", 3.9), new N("Docker", 1.8)),
        new N("ProgramData", 5.1),
        new N("$Recycle.Bin", 3.2));

    const double RootFilesGb = 12.0; // pagefile.sys, hiberfil.sys…

    public static SpaceNode SpaceTree() => Build(Tree, null, @"C:\");

    static SpaceNode Build(N n, SpaceNode? parent, string path)
    {
        var node = new SpaceNode { Name = n.Name, FullPath = path, Parent = parent };
        var kids = n.Kids.Select(k => Build(k, node, Path.Combine(path, k.Name))).OrderByDescending(k => k.Size).ToList();
        node.Children = kids;
        long kidBytes = kids.Sum(k => k.Size);
        long own = parent is null ? Gb(RootFilesGb) : Math.Max(0, Gb(n.Gb) - kidBytes);
        node.FileBytes = own;
        node.FileCount = own > 0 ? (int)(own / (3 * MB)) + 1 : 0;
        node.Size = own + kidBytes;
        return node;
    }

    public static IReadOnlyList<BigFile> BigFiles() =>
    [
        new(@"C:\pagefile.sys", Gb(8.0)),
        new(@"C:\Users\you\Downloads\Windows11_InsiderPreview.iso", Gb(6.12)),
        new(@"C:\Users\you\AppData\Local\Docker\wsl\disk\docker_data.vhdx", Gb(5.9)),
        new(@"C:\hiberfil.sys", Gb(3.9)),
        new(@"C:\Users\you\Downloads\Recording 2026-03-14.mkv", Gb(3.24)),
        new(@"C:\Users\you\Videos\Captures\ranked-clutch-4k.mp4", Gb(2.8)),
        new(@"C:\Users\you\Downloads\final-edit-v3-export.mp4", Gb(2.41)),
        new(@"C:\Program Files (x86)\Steam\steamapps\common\Neon Drift\data0.pak", Gb(2.2)),
        new(@"C:\Users\you\Downloads\dataset-full.zip", Gb(1.83)),
        new(@"C:\Windows.old\Windows\System32\MRT.exe", Gb(1.1)),
        new(@"C:\Users\you\AppData\Roaming\Spotify\Data\cache.bnk", Gb(0.96)),
        new(@"C:\Users\you\Videos\vacation-2025.mov", Gb(0.91)),
    ];
}
