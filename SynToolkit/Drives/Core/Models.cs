// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
#nullable enable

namespace SynToolkit.Drives.Core;

public enum Safety
{
    Safe,
    Review
}

public enum CleanAction
{
    Delete,
    EmptyRecycleBin,
    SteamUninstall,
    OpenSettings,
    TurnOffHibernation
}

public sealed class CleanupItem
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public string Path => Paths.Count > 0 ? Paths[0] : string.Empty;
    public long Size { get; set; }
    public int FileCount { get; set; }
    public bool IsFile { get; init; }
    public bool WholeFolder { get; init; }
    public string? Detail { get; init; }
    public TimeSpan? MinAge { get; init; }
    public string? AppId { get; init; }
}

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public Safety Safety { get; init; } = Safety.Safe;
    public CleanAction Action { get; init; } = CleanAction.Delete;
    public bool SelectedByDefault { get; init; }
    public bool ShowItems { get; init; } = true;
    public bool Blocked { get; set; }
    public string? Note { get; set; }
    public List<CleanupItem> Items { get; } = [];
    public long Size => Items.Sum(i => i.Size);
}

public static class G
{
    public const string Delete = "\uE74D";
    public const string Clock = "\uE823";
    public const string Globe = "\uE774";
    public const string Apps = "\uE71D";
    public const string Color = "\uE790";
    public const string Bug = "\uEBE8";
    public const string Update = "\uE895";
    public const string Package = "\uE7B8";
    public const string Download = "\uE896";
    public const string Game = "\uE7FC";
    public const string History = "\uE81C";
    public const string Power = "\uE7E8";
    public const string Folder = "\uE8B7";
    public const string Document = "\uE8A5";
    public const string Shield = "\uEA18";
}

public static class Catalog
{
    public static CleanupCategory Create(string id) => id switch
    {
        "recycle" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatRecycle", "Recycle Bin"),
            Glyph = G.Delete,
            Description = DrivesText.Get("DrivesCleaner_CatRecycleDesc", "Stuff you already deleted. It still takes up space until the bin is emptied."),
            Action = CleanAction.EmptyRecycleBin,
            SelectedByDefault = true,
            ShowItems = false
        },
        "temp" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatTemp", "Temporary files"),
            Glyph = G.Clock,
            Description = DrivesText.Get("DrivesCleaner_CatTempDesc", "Leftovers apps create while they work and forget to clean up. Only files older than a day."),
            SelectedByDefault = true
        },
        "browser" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatBrowser", "Browser cache"),
            Glyph = G.Globe,
            Description = DrivesText.Get("DrivesCleaner_CatBrowserDesc", "Saved copies of websites. You stay logged in. Sites may just load a little slower the first time."),
            SelectedByDefault = true
        },
        "apps" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatApps", "App caches"),
            Glyph = G.Apps,
            Description = DrivesText.Get("DrivesCleaner_CatAppsDesc", "Cached data from Discord, VS Code, Slack and friends. They rebuild it on their own when needed."),
            SelectedByDefault = true
        },
        "shaders" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatShaders", "Graphics shader cache"),
            Glyph = G.Color,
            Description = DrivesText.Get("DrivesCleaner_CatShadersDesc", "Game graphics your GPU pre-built. Safe to clear. Games rebuild it, but the next launch may stutter a bit."),
            SelectedByDefault = false
        },
        "crash" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatCrash", "Crash reports"),
            Glyph = G.Bug,
            Description = DrivesText.Get("DrivesCleaner_CatCrashDesc", "Reports Windows saves whenever an app crashes. Only useful to developers."),
            SelectedByDefault = true
        },
        "windows" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatWindows", "Windows Update leftovers"),
            Glyph = G.Update,
            Description = DrivesText.Get("DrivesCleaner_CatWindowsDesc", "Update files Windows already installed but keeps around just in case, plus Windows' own temp files."),
            SelectedByDefault = true
        },
        "installers" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatInstallers", "Old installers"),
            Glyph = G.Package,
            Safety = Safety.Review,
            Description = DrivesText.Get("DrivesCleaner_CatInstallersDesc", "Setup files in your Downloads you've most likely already run. Only ones older than a week.")
        },
        "downloads" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatDownloads", "Big old downloads"),
            Glyph = G.Download,
            Safety = Safety.Review,
            Description = DrivesText.Get("DrivesCleaner_CatDownloadsDesc", "Large files in your Downloads you haven't touched in over a month: old videos, ISOs, zips.")
        },
        "games" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatGames", "Games you haven't played"),
            Glyph = G.Game,
            Safety = Safety.Review,
            Description = DrivesText.Get("DrivesCleaner_CatGamesDesc", "Steam games you haven't opened in 2+ months. Uninstall through Steam. You can always reinstall."),
            Action = CleanAction.SteamUninstall
        },
        "winold" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatWinOld", "Previous Windows version"),
            Glyph = G.History,
            Safety = Safety.Review,
            Description = DrivesText.Get("DrivesCleaner_CatWinOldDesc", "A backup of your old Windows from the last big update. You only need it if you want to roll back."),
            Action = CleanAction.OpenSettings,
            ShowItems = false
        },
        "leftovers" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatLeftovers", "Leftovers from uninstalled apps"),
            Glyph = G.Folder,
            Safety = Safety.Review,
            Description = DrivesText.Get("DrivesCleaner_CatLeftoversDesc", "Folders in AppData that don't match anything installed and haven't changed in 3+ months.")
        },
        "hibernation" => new CleanupCategory
        {
            Id = id,
            Title = DrivesText.Get("DrivesCleaner_CatHibernation", "Hibernation file"),
            Glyph = G.Power,
            Safety = Safety.Review,
            Description = DrivesText.Get("DrivesCleaner_CatHibernationDesc", "A hidden file as big as your memory that Windows keeps for hibernation and Fast Startup."),
            Action = CleanAction.TurnOffHibernation,
            ShowItems = false
        },
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown category")
    };
}
