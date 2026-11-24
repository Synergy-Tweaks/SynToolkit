using System.Diagnostics;
using Roomy.Core;

namespace Roomy.Tests;

public sealed class CacheFinderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "RoomyTests", Guid.NewGuid().ToString("N"));

    string Dir(params string[] parts)
    {
        var p = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void FindsChromiumCachesInsideALauncher()
    {
        var cache = Dir("CEF", "Profile", "Cache");
        Dir("CEF", "Profile", "Cache", "Cache_Data");
        var code = Dir("CEF", "Profile", "Code Cache");
        var gpu = Dir("CEF", "Profile", "GPUCache");

        var found = CacheFinder.ChromiumCachesUnder(_root);

        Assert.Equal(new[] { cache, code, gpu }.OrderBy(x => x), found.OrderBy(x => x));
    }

    [Fact]
    public void IgnoresAnAppsOwnCacheFolder()
    {
        // A lone "cache" with no Chromium layout around it could be logins or game data.
        Dir("cache", "ownership");
        Dir("data", "Cache");
        Assert.Empty(CacheFinder.ChromiumCachesUnder(_root));
    }

    [Fact]
    public void FindsOldChromiumLayoutWithoutCacheData()
    {
        var cache = Dir("Profile", "Cache");
        Dir("Profile", "GPUCache");
        Assert.Contains(cache, CacheFinder.ChromiumCachesUnder(_root));
    }

    [Fact]
    public void DoesNotFollowJunctions()
    {
        var outside = Path.Combine(Path.GetTempPath(), "RoomyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(outside, "Profile", "Cache"));
        Directory.CreateDirectory(Path.Combine(outside, "Profile", "GPUCache"));
        Directory.CreateDirectory(_root);
        var link = Path.Combine(_root, "link");
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            p.WaitForExit();
        try
        {
            Assert.True(Directory.Exists(link));
            Assert.Empty(CacheFinder.ChromiumCachesUnder(_root));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void MissingFolderFindsNothing() => Assert.Empty(CacheFinder.ChromiumCachesUnder(Path.Combine(_root, "nope")));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
