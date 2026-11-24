using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Roomy.Core;

public sealed record SteamGame(string AppId, string Name, long Size, DateTime? LastPlayed, string InstallPath);

/// <summary>Reads Steam's own library files. Read-only: uninstalling is left to Steam.</summary>
public static class Steam
{
    const string RedistributablesAppId = "228980";

    public static string? Root()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (k?.GetValue("SteamPath") is string s && Directory.Exists(s))
                return Path.GetFullPath(s.Replace('/', '\\'));
        }
        catch { }
        var d = Path.Combine(Env.ProgramFilesX86, "Steam");
        return Directory.Exists(d) ? d : null;
    }

    public static List<string> Libraries()
    {
        var root = Root();
        if (root is null) return [];
        var libs = new List<string> { root };
        try
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { }
        return libs.Where(Directory.Exists)
                   .Select(p => Path.GetFullPath(p).TrimEnd('\\'))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    public static List<SteamGame> Games()
    {
        var list = new List<SteamGame>();
        foreach (var lib in Libraries())
        {
            var apps = Path.Combine(lib, "steamapps");
            List<string> manifests;
            try { manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList(); }
            catch { continue; }

            foreach (var acf in manifests)
            {
                try
                {
                    var text = File.ReadAllText(acf);
                    string? V(string key)
                    {
                        var m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
                        return m.Success ? m.Groups[1].Value : null;
                    }

                    var id = V("appid");
                    var name = V("name");
                    var dir = V("installdir");
                    var lastPlayed = V("LastPlayed");
                    // No LastPlayed means we can't tell, so we don't guess.
                    if (id is null || name is null || dir is null || lastPlayed is null || id == RedistributablesAppId) continue;

                    long.TryParse(V("SizeOnDisk"), out var size);
                    DateTime? lp = long.TryParse(lastPlayed, out var t) && t > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime
                        : null;
                    list.Add(new SteamGame(id, name, size, lp, Path.Combine(apps, "common", dir)));
                }
                catch { }
            }
        }
        return list;
    }
}
