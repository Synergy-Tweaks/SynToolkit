using Roomy.Localization;
namespace Roomy.Core;

public enum Safety { Safe, Review }

public enum CleanAction
{
    /// <summary>Delete files (for folders: delete the contents, keep the folder).</summary>
    Delete,
    EmptyRecycleBin,
    /// <summary>Hand off to Steam's own uninstaller.</summary>
    SteamUninstall,
    /// <summary>Only Windows can remove it safely, so we open Settings.</summary>
    OpenSettings,
    /// <summary>Windows' powercfg turns hibernation off, which removes hiberfil.sys.</summary>
    TurnOffHibernation,
}

public sealed class CleanupItem
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public string Path => Paths.Count > 0 ? Paths[0] : "";
    public long Size { get; set; }
    public int FileCount { get; set; }
    public bool IsFile { get; init; }
    /// <summary>The folder itself is removed (or recycled), not just its contents.</summary>
    public bool WholeFolder { get; init; }
    public string? Detail { get; init; }
    /// <summary>Only files older than this are measured and deleted.</summary>
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

/// <summary>Segoe Fluent Icons / MDL2 code points.</summary>
public static class G
{
    public const string Delete = "";
    public const string Clock = "";
    public const string Globe = "";
    public const string Apps = "";
    public const string Color = "";
    public const string Bug = "";
    public const string Update = "";
    public const string Package = "";
    public const string Download = "";
    public const string Game = "";
    public const string History = "";
    public const string Power = "";
    public const string Folder = "";
    public const string Document = "";
    public const string Shield = "";
    public const string Check = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string ChevronRight = "";
}

/// <summary>The fixed copy for every category, shared by the real scanner and demo mode.</summary>
public static class Catalog
{
    public static CleanupCategory Create(string id) => id switch
    {
        "recycle" => new CleanupCategory
        {
            Id = id, Title = S.CatRecycle, Glyph = G.Delete,
            Description = S.CatRecycleDesc,
            Action = CleanAction.EmptyRecycleBin, SelectedByDefault = true, ShowItems = false,
        },
        "temp" => new CleanupCategory
        {
            Id = id, Title = S.CatTemp, Glyph = G.Clock,
            Description = S.CatTempDesc,
            SelectedByDefault = true,
        },
        "browser" => new CleanupCategory
        {
            Id = id, Title = S.CatBrowser, Glyph = G.Globe,
            Description = S.CatBrowserDesc,
            SelectedByDefault = true,
        },
        "apps" => new CleanupCategory
        {
            Id = id, Title = S.CatApps, Glyph = G.Apps,
            Description = S.CatAppsDesc,
            SelectedByDefault = true,
        },
        "shaders" => new CleanupCategory
        {
            Id = id, Title = S.CatShaders, Glyph = G.Color,
            Description = S.CatShadersDesc,
            SelectedByDefault = false,
        },
        "crash" => new CleanupCategory
        {
            Id = id, Title = S.CatCrash, Glyph = G.Bug,
            Description = S.CatCrashDesc,
            SelectedByDefault = true,
        },
        "windows" => new CleanupCategory
        {
            Id = id, Title = S.CatWindows, Glyph = G.Update,
            Description = S.CatWindowsDesc,
            SelectedByDefault = true,
        },
        "installers" => new CleanupCategory
        {
            Id = id, Title = S.CatInstallers, Glyph = G.Package, Safety = Safety.Review,
            Description = S.CatInstallersDesc,
        },
        "downloads" => new CleanupCategory
        {
            Id = id, Title = S.CatDownloads, Glyph = G.Download, Safety = Safety.Review,
            Description = S.CatDownloadsDesc,
        },
        "games" => new CleanupCategory
        {
            Id = id, Title = S.CatGames, Glyph = G.Game, Safety = Safety.Review,
            Description = S.CatGamesDesc,
            Action = CleanAction.SteamUninstall,
        },
        "winold" => new CleanupCategory
        {
            Id = id, Title = S.CatWinOld, Glyph = G.History, Safety = Safety.Review,
            Description = S.CatWinOldDesc,
            Action = CleanAction.OpenSettings, ShowItems = false,
        },
        "leftovers" => new CleanupCategory
        {
            Id = id, Title = S.CatLeftovers, Glyph = G.Folder, Safety = Safety.Review,
            Description = S.CatLeftoversDesc,
        },
        "hibernation" => new CleanupCategory
        {
            Id = id, Title = S.CatHibernation, Glyph = G.Power, Safety = Safety.Review,
            Description = S.CatHibernationDesc,
            Action = CleanAction.TurnOffHibernation, ShowItems = false,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, S.UnknownCategory),
    };
}
