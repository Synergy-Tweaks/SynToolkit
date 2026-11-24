using Roomy.Core;

namespace Roomy.Tests;

public sealed class LeftoverFinderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "RoomyTests", Guid.NewGuid().ToString("N"));
    static readonly DateTime Now = DateTime.UtcNow;

    string Folder(string name, long bytes, int daysOld)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "data.bin");
        using (var f = File.Create(file)) f.SetLength(bytes);
        var t = Now.AddDays(-daysOld);
        File.SetLastWriteTimeUtc(file, t);
        Directory.SetLastWriteTimeUtc(dir, t);
        return dir;
    }

    List<LeftoverFinder.Leftover> Find(params string[] installed) =>
        LeftoverFinder.Find([_root], installed.ToHashSet(), Now);

    [Fact]
    public void FindsABigOldFolderThatMatchesNothingInstalled()
    {
        var dir = Folder("OldPhotoEditor", 60L << 20, 200);
        var found = Find("spotify", "discord");
        Assert.Equal(dir, Assert.Single(found).Path);
    }

    [Fact]
    public void SkipsFoldersOfInstalledApps()
    {
        Folder("JetBrains", 60L << 20, 200);
        Folder("obs-studio", 60L << 20, 200);
        // Installed names arrive normalized, the way InstalledNames builds them.
        Assert.Empty(Find("jetbrainssro", "obsstudio"));
    }

    [Fact]
    public void SkipsRecentlyUsedSmallAndKnownFolders()
    {
        Folder("RecentTool", 60L << 20, 10);      // used recently
        Folder("TinyLeftover", 1L << 20, 400);    // too small to matter
        Folder("Microsoft", 60L << 20, 400);      // part of Windows
        Folder(".minecraft", 60L << 20, 400);     // dot folders hold saves
        Folder("abc", 60L << 20, 400);            // name too short to judge
        Assert.Empty(Find());
    }

    [Fact]
    public void MatchingIsLenientSoItErrsOnTheSideOfKeeping()
    {
        Assert.True(LeftoverFinder.BelongsToInstalled("Blender Foundation", new HashSet<string> { "blender" }));
        Assert.True(LeftoverFinder.BelongsToInstalled("obs", new HashSet<string>()));
        Assert.False(LeftoverFinder.BelongsToInstalled("OldPhotoEditor", new HashSet<string> { "spotify" }));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
