using Roomy.Core;

namespace Roomy.Tests;

public sealed class RecycleTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "RoomyTests", Guid.NewGuid().ToString("N"));

    public RecycleTests() => Directory.CreateDirectory(_folder);

    string MakeFile(string name, TimeSpan age)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[4096]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    // On CI there may be no Recycle Bin, and Windows would wait on a "delete permanently?" prompt.
    static bool OnCi => Environment.GetEnvironmentVariable("CI") == "true";

    [Fact]
    public void RecycleMovesTheFileToTheBin()
    {
        if (OnCi) return;
        var path = MakeFile("old-setup.exe", TimeSpan.FromDays(30));
        var (before, _) = Native.QueryRecycleBin();

        var r = FileOps.Recycle(path, TimeSpan.FromDays(7));

        Assert.Equal(1, r.Files);
        Assert.Equal(4096, r.Freed);
        Assert.False(File.Exists(path));
        var (after, _) = Native.QueryRecycleBin();
        Assert.True(after > before, "the file should now be in the Recycle Bin");
    }

    [Fact]
    public void RecycleSkipsFilesNewerThanTheMinimumAge()
    {
        var path = MakeFile("new-setup.exe", TimeSpan.FromDays(1));
        var r = FileOps.Recycle(path, TimeSpan.FromDays(7));
        Assert.Equal(0, r.Files);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RecycleMovesAWholeFolderToTheBin()
    {
        if (OnCi) return;
        var dir = Path.Combine(_folder, "LeftoverApp");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "sub", "a.dat"), new byte[2048]);
        var r = FileOps.Recycle(dir, null);
        Assert.Equal(2048, r.Freed);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void RecycleRefusesProtectedFolders()
    {
        Assert.Throws<InvalidOperationException>(() => FileOps.Recycle(Env.Roaming, null));
    }

    [Fact]
    public void CleanerRecyclesDownloadsButDeletesCaches()
    {
        if (OnCi) return;
        var download = MakeFile("big-download.zip", TimeSpan.FromDays(60));
        var cacheDir = Path.Combine(_folder, "cache");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllBytes(Path.Combine(cacheDir, "blob"), new byte[2048]);

        var downloads = new CleanupCategory { Id = "downloads", Title = "Downloads", Description = "", Glyph = "", Safety = Safety.Review };
        downloads.Items.Add(new CleanupItem { Name = "zip", Paths = [download], Size = 4096, IsFile = true });
        var caches = new CleanupCategory { Id = "apps", Title = "Caches", Description = "", Glyph = "" };
        caches.Items.Add(new CleanupItem { Name = "cache", Paths = [cacheDir], Size = 2048 });

        var r = Cleaner.Run([new(downloads, downloads.Items), new(caches, caches.Items)], null, recycleReview: true);

        Assert.Equal(4096, r.Recycled);
        Assert.Equal(2048, r.Freed);
        Assert.False(File.Exists(download));
        Assert.Empty(Directory.GetFiles(cacheDir));
    }

    [Fact]
    public void WithoutTheOptionDownloadsAreDeleted()
    {
        var download = MakeFile("old.msi", TimeSpan.FromDays(60));
        var downloads = new CleanupCategory { Id = "downloads", Title = "Downloads", Description = "", Glyph = "", Safety = Safety.Review };
        downloads.Items.Add(new CleanupItem { Name = "msi", Paths = [download], Size = 4096, IsFile = true });

        var r = Cleaner.Run([new(downloads, downloads.Items)], null);

        Assert.Equal(0, r.Recycled);
        Assert.Equal(4096, r.Freed);
        Assert.False(File.Exists(download));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }
}
